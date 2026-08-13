using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Desktop.Tests;

/// <summary>
/// Verifies scan-root selection without invoking scanner infrastructure.
/// </summary>
public sealed class FolderSelectionViewModelTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "OpenSorSe.Tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes an isolated temporary directory for each test.
    /// </summary>
    public FolderSelectionViewModelTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    /// <summary>
    /// Removes the isolated temporary directory created for the test.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    /// <summary>Verifies manual scans capture the chosen profile and session-only narrowing without mutating the profile.</summary>
    [Fact]
    public void RequestScan_CapturesSelectedWorkflowAndOneTimeOverride()
    {
        var viewModel = new FolderSelectionViewModel();
        Assert.True(viewModel.AddFolder(_temporaryDirectory));
        Assert.True(viewModel.SelectProfile(BuiltInWorkflowIds.Photos));
        viewModel.UseOneTimeOverride = true;
        viewModel.OverrideDuplicateAnalysisEnabled = false;
        viewModel.OverrideAiEnabled = false;
        viewModel.OverrideOcrEnabled = false;
        viewModel.OverrideMaximumFileSizeMiB = 25;
        viewModel.SelectedInitialScanDepth = InitialScanDepth.DeepInitialAnalysis;
        ScanRequest? request = null;
        viewModel.ScanRequested += (_, value) => request = value;

        viewModel.RequestScan();

        Assert.NotNull(request);
        Assert.Equal(BuiltInWorkflowIds.Photos, request.ProfileId);
        Assert.Equal(25 * 1024 * 1024, request.OneTimeOverride!.MaximumFileSizeBytes);
        Assert.False(request.OneTimeOverride.DuplicateAnalysisEnabled);
        Assert.Equal(InitialScanDepth.DeepInitialAnalysis, request.InitialScanDepth);
        Assert.Equal(
            1024L * 1024 * 1024,
            BuiltInWorkflowLibrary.Profiles.Single(profile => profile.Id == BuiltInWorkflowIds.Photos)
                .Files.MaximumFileSizeBytes);
    }

    /// <summary>
    /// Verifies an accessible absolute directory is normalized, selected, and remembered.
    /// </summary>
    [Fact]
    public void AddFolder_AddsNormalizedPathAndRecentLocation()
    {
        var viewModel = new FolderSelectionViewModel();

        var added = viewModel.AddFolder(_temporaryDirectory + Path.DirectorySeparatorChar);

        Assert.True(added);
        var normalizedPath = Path.GetFullPath(_temporaryDirectory + Path.DirectorySeparatorChar);
        Assert.Equal([normalizedPath], viewModel.SelectedFolders);
        Assert.Equal([normalizedPath], viewModel.RecentFolders);
        Assert.Equal("Folder added.", viewModel.StatusText);
    }

    /// <summary>
    /// Verifies invalid and duplicate folders do not change the selected roots.
    /// </summary>
    [Fact]
    public void AddFolder_InvalidOrDuplicatePath_DoesNotChangeSelection()
    {
        var viewModel = new FolderSelectionViewModel();

        Assert.False(viewModel.AddFolder("relative"));
        Assert.False(viewModel.AddFolder(Path.Combine(_temporaryDirectory, "missing")));
        Assert.True(viewModel.AddFolder(_temporaryDirectory));
        Assert.False(viewModel.AddFolder(_temporaryDirectory));

        Assert.Single(viewModel.SelectedFolders);
        Assert.Equal("The folder is already selected.", viewModel.StatusText);
    }

    /// <summary>
    /// Verifies a selected folder can be removed without changing recent locations.
    /// </summary>
    [Fact]
    public void RemoveSelectedFolder_RemovesOnlySelectedFolder()
    {
        var firstDirectory = Path.Combine(_temporaryDirectory, "first");
        var secondDirectory = Path.Combine(_temporaryDirectory, "second");
        Directory.CreateDirectory(firstDirectory);
        Directory.CreateDirectory(secondDirectory);
        var viewModel = new FolderSelectionViewModel();
        Assert.True(viewModel.AddFolder(firstDirectory));
        Assert.True(viewModel.AddFolder(secondDirectory));
        viewModel.SelectedFolder = Path.GetFullPath(firstDirectory);

        var removed = viewModel.RemoveSelectedFolder();

        Assert.True(removed);
        Assert.Equal([Path.GetFullPath(secondDirectory)], viewModel.SelectedFolders);
        Assert.Equal(2, viewModel.RecentFolders.Count);
    }

    /// <summary>
    /// Verifies a request preserves selection order and does not run a scanner.
    /// </summary>
    [Fact]
    public void RequestScan_EmitsImmutableSelectionInUserOrder()
    {
        var firstDirectory = Path.Combine(_temporaryDirectory, "first");
        var secondDirectory = Path.Combine(_temporaryDirectory, "second");
        Directory.CreateDirectory(firstDirectory);
        Directory.CreateDirectory(secondDirectory);
        var viewModel = new FolderSelectionViewModel();
        Assert.True(viewModel.AddFolder(firstDirectory));
        Assert.True(viewModel.AddFolder(secondDirectory));
        ScanRequest? capturedRequest = null;
        viewModel.ScanRequested += (_, request) => capturedRequest = request;

        viewModel.RequestScan();

        var request = Assert.IsType<ScanRequest>(capturedRequest);
        Assert.Equal([Path.GetFullPath(firstDirectory), Path.GetFullPath(secondDirectory)], request.FolderPaths);
        Assert.Equal("Scan request created.", viewModel.StatusText);
        Assert.Equal(2, viewModel.SelectedFolders.Count);
    }

    /// <summary>
    /// Verifies a scan request without selected folders has no event side effect.
    /// </summary>
    [Fact]
    public void RequestScan_WithoutFolders_ReportsValidationStatus()
    {
        var viewModel = new FolderSelectionViewModel();
        var eventRaised = false;
        viewModel.ScanRequested += (_, _) => eventRaised = true;

        viewModel.RequestScan();

        Assert.False(eventRaised);
        Assert.Equal("Select at least one folder before starting a scan.", viewModel.StatusText);
    }

    /// <summary>Verifies native picker failures leave manual path entry available.</summary>
    [Fact]
    public void ReportFolderPickerFailure_ProvidesSafeManualFallback()
    {
        var viewModel = new FolderSelectionViewModel();

        viewModel.ReportFolderPickerFailure();

        Assert.Equal(
            "The folder picker could not be opened. Enter an absolute folder path instead.",
            viewModel.StatusText);
        Assert.Empty(viewModel.SelectedFolders);
    }
}
