using System.Security.Cryptography;
using System.Text;

namespace OpenSorSe.Core.Platform;

/// <summary>Describes whether the current process owns the one-writer lease for an application profile.</summary>
public enum ProfileOwnershipStatus
{
    /// <summary>The caller is not a desktop profile owner, such as an isolated test or helper.</summary>
    NotRequired,
    /// <summary>The current process owns the profile.</summary>
    Owned,
    /// <summary>Another process already owns the profile.</summary>
    Contended,
    /// <summary>The ownership primitive could not be created or inspected.</summary>
    Unavailable,
}

/// <summary>Exposes bounded profile-ownership state to health reporting without exposing lock internals.</summary>
public interface IProfileOwnershipState
{
    /// <summary>Gets the current ownership state.</summary>
    ProfileOwnershipStatus Status { get; }

    /// <summary>Gets a safe user-facing explanation.</summary>
    string Message { get; }

    /// <summary>Gets a non-path profile fingerprint suitable for diagnostics.</summary>
    string ProfileFingerprint { get; }
}

/// <summary>
/// Holds an operating-system mutex for one profile. The informational marker is intentionally retained;
/// ownership is represented by the mutex, so process death releases it without stale-lock cleanup.
/// </summary>
public sealed class ProfileOwnershipLease : IProfileOwnershipState, IDisposable
{
    private const string LockFileName = "profile.owner.lock";
    private MutexOwner? _owner;

    private ProfileOwnershipLease(
        MutexOwner? owner,
        ProfileOwnershipStatus status,
        string message,
        string fingerprint)
    {
        _owner = owner;
        Status = status;
        Message = message;
        ProfileFingerprint = fingerprint;
    }

    /// <inheritdoc />
    public ProfileOwnershipStatus Status { get; private set; }

    /// <inheritdoc />
    public string Message { get; private set; }

    /// <inheritdoc />
    public string ProfileFingerprint { get; }

    /// <summary>Gets a state used by isolated helpers that do not own the desktop profile.</summary>
    public static IProfileOwnershipState NotRequired { get; } = new ProfileOwnershipLease(
        null,
        ProfileOwnershipStatus.NotRequired,
        "Profile ownership is not required for this isolated operation.",
        "isolated");

    /// <summary>Acquires exclusive one-writer ownership for an absolute profile state directory.</summary>
    public static ProfileOwnershipLease Acquire(string stateDirectory)
    {
        if (string.IsNullOrWhiteSpace(stateDirectory) || !Path.IsPathFullyQualified(stateDirectory))
        {
            throw new ArgumentException("An absolute profile state directory is required.", nameof(stateDirectory));
        }

        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stateDirectory));
        var identityPath = OperatingSystem.IsWindows() ? normalized.ToUpperInvariant() : normalized;
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identityPath)))[..16];
        var lockPath = Path.Combine(normalized, LockFileName);
        MutexOwner? owner = null;
        try
        {
            Directory.CreateDirectory(normalized);
            var userScope = $"{Environment.UserDomainName}\\{Environment.UserName}";
            var mutexFingerprint = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{userScope}\n{identityPath}")))[..32];
            var mutexName = OperatingSystem.IsWindows()
                ? $"Local\\OmniSorSe.Profile.{mutexFingerprint}"
                : $"OmniSorSe.Profile.{mutexFingerprint}";
            owner = MutexOwner.Start(mutexName);
            if (!owner.Acquired)
            {
                owner.Dispose();
                owner = null;
                throw new ProfileOwnershipException(
                    "Another OmniSorSe process already owns this profile. Close the other instance and try again.",
                    fingerprint);
            }

            var metadata = Encoding.UTF8.GetBytes(
                $"OmniSorSe profile owner\nProcessId={Environment.ProcessId}\nStartedUtc={DateTimeOffset.UtcNow:O}\nProfile={fingerprint}\n");
            using (var stream = new FileStream(
                       lockPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.Read,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(metadata);
                stream.Flush(flushToDisk: true);
            }

            return new ProfileOwnershipLease(
                owner,
                ProfileOwnershipStatus.Owned,
                "This process owns the OmniSorSe profile.",
                fingerprint);
        }
        catch (ProfileOwnershipException)
        {
            owner?.Dispose();
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            owner?.Dispose();
            throw new ProfileOwnershipException(
                "OmniSorSe cannot create the profile ownership lock. Check application-data permissions and try again.",
                fingerprint,
                exception);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        if (owner is null)
        {
            return;
        }

        owner.Dispose();
        Status = ProfileOwnershipStatus.NotRequired;
        Message = "Profile ownership was released cleanly.";
    }

    /// <summary>
    /// Owns the thread-affine named mutex on a dedicated thread. Desktop shutdown
    /// may resume on a different thread, so callers signal this owner instead of
    /// releasing the mutex themselves.
    /// </summary>
    private sealed class MutexOwner : IDisposable
    {
        private readonly ManualResetEventSlim _release = new(false);
        private readonly ManualResetEventSlim _started = new(false);
        private readonly Thread _thread;
        private Exception? _failure;
        private int _disposed;

        private MutexOwner(string mutexName)
        {
            _thread = new Thread(() => Own(mutexName))
            {
                IsBackground = true,
                Name = "OmniSorSe profile owner",
            };
        }

        public bool Acquired { get; private set; }

        public static MutexOwner Start(string mutexName)
        {
            var owner = new MutexOwner(mutexName);
            owner._thread.Start();
            if (!owner._started.Wait(TimeSpan.FromSeconds(5)))
            {
                owner.Dispose();
                throw new InvalidOperationException("Profile ownership initialization timed out.");
            }

            if (owner._failure is not null)
            {
                var failure = owner._failure;
                owner.Dispose();
                throw new InvalidOperationException("Profile ownership initialization failed.", failure);
            }

            return owner;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _release.Set();
            if (_thread.IsAlive && !_thread.Join(TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException("Profile ownership release timed out.");
            }

            _release.Dispose();
            _started.Dispose();
        }

        private void Own(string mutexName)
        {
            var ownsMutex = false;
            try
            {
                using var mutex = new Mutex(initiallyOwned: false, mutexName);
                try
                {
                    ownsMutex = mutex.WaitOne(TimeSpan.Zero);
                }
                catch (AbandonedMutexException)
                {
                    ownsMutex = true;
                }

                Acquired = ownsMutex;
                _started.Set();
                if (ownsMutex)
                {
                    _release.Wait();
                    mutex.ReleaseMutex();
                    ownsMutex = false;
                }
            }
            catch (Exception exception)
            {
                _failure = exception;
                _started.Set();
                if (ownsMutex)
                {
                    // A failure after acquisition ends this thread, which abandons
                    // the OS mutex and makes it safely acquirable by the next owner.
                }
            }
        }
    }
}

/// <summary>Reports explicit profile ownership failure without leaking the profile path.</summary>
public sealed class ProfileOwnershipException : IOException
{
    /// <summary>Creates one bounded ownership-contention error.</summary>
    public ProfileOwnershipException(string message, string profileFingerprint)
        : base(message)
    {
        ProfileFingerprint = profileFingerprint;
    }

    /// <summary>Creates one bounded ownership error.</summary>
    public ProfileOwnershipException(string message, string profileFingerprint, Exception innerException)
        : base(message, innerException)
    {
        ProfileFingerprint = profileFingerprint;
    }

    /// <summary>Gets the non-path profile fingerprint used for diagnostics.</summary>
    public string ProfileFingerprint { get; }
}
