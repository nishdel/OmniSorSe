using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenSorSe.Core.DependencyInjection;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Core.Tests.Logging;

/// <summary>
/// Verifies local, privacy-preserving centralized logging behavior.
/// </summary>
public sealed class LoggingServiceTests
{
    /// <summary>
    /// Verifies enabled categories write UTC daily text entries and accurate process-lifetime counts.
    /// </summary>
    [Fact]
    public void Initialize_WritesCategorizedEntriesAndStatistics()
    {
        using var directory = new TemporaryDirectory();
        using var service = new LoggingService();
        service.Initialize(new LoggingOptions(LogLevel.Information, true, directory.Path));
        var logger = service.CreateLogger("CoreTests");

        logger.LogInformation("information message");
        logger.LogWarning("warning message");
        logger.LogError(new InvalidOperationException("sensitive exception"), "error message");

        var filePath = Assert.Single(Directory.GetFiles(directory.Path, "opensorse-owned-*.log"));
        var output = File.ReadAllText(filePath);
        Assert.StartsWith("# OmniSorSe owned log v1", output, StringComparison.Ordinal);
        Assert.Contains("[Information] [CoreTests] information message", output, StringComparison.Ordinal);
        Assert.Contains("[Warning] [CoreTests] warning message", output, StringComparison.Ordinal);
        Assert.Contains("[Error] [CoreTests] error message", output, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive exception", output, StringComparison.Ordinal);
        Assert.Equal(new LoggingStatistics(0, 0, 1, 1, 1, 0, 0), service.GetStatistics());
    }

    /// <summary>
    /// Verifies the configured minimum level filters both local output and statistics.
    /// </summary>
    [Fact]
    public void Initialize_FiltersEntriesBelowMinimumLevel()
    {
        using var directory = new TemporaryDirectory();
        using var service = new LoggingService();
        service.Initialize(new LoggingOptions(LogLevel.Warning, true, directory.Path));
        var logger = service.CreateLogger("CoreTests");

        logger.LogInformation("filtered");
        logger.LogWarning("retained");

        var output = File.ReadAllText(Assert.Single(Directory.GetFiles(directory.Path, "opensorse-owned-*.log")));
        Assert.DoesNotContain("filtered", output, StringComparison.Ordinal);
        Assert.Contains("retained", output, StringComparison.Ordinal);
        Assert.Equal(new LoggingStatistics(0, 0, 0, 1, 0, 0, 0), service.GetStatistics());
    }

    /// <summary>
    /// Verifies disabling the file sink produces no application-log directory while remaining safe to log through Debug.
    /// </summary>
    [Fact]
    public void Initialize_WithFileSinkDisabled_DoesNotCreateLogDirectory()
    {
        using var directory = new TemporaryDirectory();
        var logDirectoryPath = Path.Combine(directory.Path, "logs");
        using var service = new LoggingService();
        service.Initialize(new LoggingOptions(LogLevel.Information, false, logDirectoryPath));

        service.CreateLogger("CoreTests").LogInformation("debug fallback remains available");

        Assert.False(Directory.Exists(logDirectoryPath));
        Assert.Equal(1L, service.GetStatistics().InformationEntries);
    }

    /// <summary>
    /// Verifies only matching daily log files beyond the configured retention count are removed.
    /// </summary>
    [Fact]
    public void Initialize_AppliesBoundedDailyRetention()
    {
        using var directory = new TemporaryDirectory();
        for (var day = 1; day <= 9; day++)
        {
            File.WriteAllText(
                Path.Combine(directory.Path, $"opensorse-owned-2024-01-{day:00}.log"),
                "# OmniSorSe owned log v1\nold");
        }

        var unrelatedPath = Path.Combine(directory.Path, "unrelated.log");
        File.WriteAllText(unrelatedPath, "retain");
        using var service = new LoggingService();
        service.Initialize(new LoggingOptions(LogLevel.Information, true, directory.Path, 7));

        Assert.Equal(7, Directory.GetFiles(directory.Path, "opensorse-owned-*.log").Length);
        Assert.True(File.Exists(unrelatedPath));
    }

    /// <summary>
    /// Verifies a coincidentally named user-owned file is never appended to or removed by logging or retention.
    /// </summary>
    [Fact]
    public void Initialize_UnownedMatchingFiles_ArePreservedAndActiveCollisionFailsClosed()
    {
        using var directory = new TemporaryDirectory();
        var oldUnownedPath = directory.CreateFile("opensorse-owned-2024-01-01.log", "user data");
        var activeUnownedPath = directory.CreateFile(
            $"opensorse-owned-{DateTimeOffset.UtcNow:yyyy-MM-dd}.log",
            "active user data");
        using var service = new LoggingService();
        service.Initialize(new LoggingOptions(LogLevel.Information, true, directory.Path, 1));

        service.CreateLogger("CoreTests").LogInformation("must not be appended");

        Assert.Equal("user data", File.ReadAllText(oldUnownedPath));
        Assert.Equal("active user data", File.ReadAllText(activeUnownedPath));
        Assert.Equal(1L, service.GetStatistics().FileWriteFailures);
    }

    /// <summary>Verifies one daily file cannot grow beyond the fixed diagnostic-output bound.</summary>
    [Fact]
    public void Log_WhenDailyFileReachedCapacity_FailsClosedWithoutGrowingFile()
    {
        using var directory = new TemporaryDirectory();
        var activePath = Path.Combine(directory.Path, $"opensorse-owned-{DateTimeOffset.UtcNow:yyyy-MM-dd}.log");
        File.WriteAllText(activePath, "# OmniSorSe owned log v1\n");
        using (var stream = new FileStream(activePath, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(LoggingLimits.MaximumDailyFileBytes);
        }

        using var service = new LoggingService();
        service.Initialize(new LoggingOptions(LogLevel.Information, true, directory.Path));

        service.CreateLogger("CoreTests").LogInformation("must not grow the file");

        Assert.Equal(LoggingLimits.MaximumDailyFileBytes, new FileInfo(activePath).Length);
        Assert.Equal(1L, service.GetStatistics().FileWriteFailures);
    }

    /// <summary>
    /// Verifies an unavailable local file sink cannot interrupt an application log call.
    /// </summary>
    [Fact]
    public void Initialize_WhenFileSinkFails_ContinuesAndCountsFailure()
    {
        using var directory = new TemporaryDirectory();
        var blockingPath = directory.CreateFile("not-a-directory", "block");
        using var service = new LoggingService();
        service.Initialize(new LoggingOptions(LogLevel.Information, true, blockingPath));

        service.CreateLogger("CoreTests").LogError("still safe");

        Assert.Equal(1L, service.GetStatistics().ErrorEntries);
        Assert.True(service.GetStatistics().FileWriteFailures >= 1);
    }

    /// <summary>
    /// Verifies invalid output options are rejected and disposal prevents later use.
    /// </summary>
    [Fact]
    public void Initialize_RejectsInvalidOptionsAndDisposedServiceCannotBeUsed()
    {
        using var service = new LoggingService();
        Assert.Throws<ArgumentOutOfRangeException>(() => service.Initialize(new LoggingOptions(LogLevel.Information, RetainedFileCount: 0)));
        Assert.Throws<ArgumentException>(() => service.Initialize(new LoggingOptions(LogLevel.Information, true, "relative")));
        Assert.Throws<ArgumentException>(() => service.Initialize(new LoggingOptions(LogLevel.Information, false, "relative")));
        service.Dispose();

        Assert.Throws<ObjectDisposedException>(() => service.CreateLogger("CoreTests"));
        Assert.Throws<ObjectDisposedException>(() => service.Initialize(LogLevel.Information));
    }

    /// <summary>Verifies inspectable session events are newest-first, bounded, and contain no exception stack.</summary>
    [Fact]
    public void GetRecentEvents_MoreThanCapacity_ReturnsBoundedSafeNewestFirstSnapshot()
    {
        using var service = new LoggingService();
        service.Initialize(new LoggingOptions(LogLevel.Information, FileLoggingEnabled: false));
        var logger = service.CreateLogger("BoundedCategory");
        for (var index = 0; index < DiagnosticEventLimits.MaximumRetainedEvents + 3; index++)
        {
            logger.LogInformation(new EventId(index, $"event-{index}"), "message {Index}", index);
        }

        logger.LogError(new InvalidOperationException("safe exception summary"), "final failure");
        var events = service.GetRecentEvents();

        Assert.Equal(DiagnosticEventLimits.MaximumRetainedEvents, events.Count);
        Assert.Equal("final failure", events[0].Summary);
        Assert.Equal("InvalidOperationException", events[0].ExceptionType);
        Assert.Null(events[0].ExceptionSummary);
        Assert.Equal("BoundedCategory", events[0].Category);
        Assert.True(events.Zip(events.Skip(1), (newer, older) => newer.Sequence > older.Sequence).All(value => value));
    }

    /// <summary>
    /// Verifies shared Core registration resolves the centralized logging contract without UI infrastructure.
    /// </summary>
    [Fact]
    public void DependencyInjection_ResolvesLoggingService()
    {
        using var directory = new TemporaryDirectory();
        var services = new ServiceCollection();
        services.AddOpenSorSeCore(new OpenSorSeCoreOptions { ConfigurationFilePath = Path.Combine(directory.Path, "settings.json") });
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<LoggingService>(provider.GetRequiredService<ILoggingService>());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"OpenSorSe.Logging.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateFile(string name, string contents)
        {
            var filePath = System.IO.Path.Combine(Path, name);
            File.WriteAllText(filePath, contents);
            return filePath;
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
