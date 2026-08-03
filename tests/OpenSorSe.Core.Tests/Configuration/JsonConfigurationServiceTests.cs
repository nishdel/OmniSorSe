using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Configuration;
using System.Text.Json;

namespace OpenSorSe.Core.Tests.Configuration;

/// <summary>
/// Tests JSON-backed application configuration behavior.
/// </summary>
public sealed class JsonConfigurationServiceTests
{
    /// <summary>
    /// Verifies that environment values take precedence over persisted user settings.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_UsesEnvironmentValueOverUserSetting()
    {
        var settingsFilePath = Path.Combine(Path.GetTempPath(), $"opensorse-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(settingsFilePath, "{\"Logging\":{\"MinimumLevel\":\"Warning\"}}");

        try
        {
            var service = new JsonConfigurationService(
                settingsFilePath,
                variableName => variableName == "OPENSORSE_LOGGING__MINIMUMLEVEL" ? "Debug" : null);

            await service.InitializeAsync(CancellationToken.None);

            Assert.Equal(LogLevel.Debug, service.Current.Logging.MinimumLevel);
        }
        finally
        {
            File.Delete(settingsFilePath);
        }
    }

    /// <summary>
    /// Verifies that missing user configuration uses safe defaults.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_UsesInformationLoggingByDefault()
    {
        var settingsFilePath = Path.Combine(Path.GetTempPath(), $"opensorse-{Guid.NewGuid():N}.json");
        var service = new JsonConfigurationService(settingsFilePath, _ => null);

        await service.InitializeAsync(CancellationToken.None);

        Assert.Equal(LogLevel.Information, service.Current.Logging.MinimumLevel);
        Assert.False(service.Current.Features.ShowAdvancedFeatures);
        Assert.Equal(
            FeatureSettings.DefaultFilesPageDetailsPanelWidthRatio,
            service.Current.Features.FilesPageDetailsPanelWidthRatio);
        Assert.False(service.Current.Ai.Enabled);
        Assert.False(service.Current.Ai.FileRenameSuggestionsEnabled);
        Assert.False(service.Current.Ai.FolderStructureSuggestionsEnabled);
        Assert.False(service.Current.Ai.RequestDiagnosticsEnabled);
        Assert.False(service.Current.Diagnostics.EnableDiagnostics);
        Assert.False(service.Current.Diagnostics.AiDiagnostics);
        Assert.False(service.Current.Diagnostics.ShowUnredactedDiagnosticContent);
        Assert.False(service.Current.Ai.DocumentTextInterpretationEnabled);
        Assert.True(service.Current.Content.MetadataExtractionEnabled);
        Assert.False(service.Current.Content.OcrEnabled);
        Assert.True(service.Current.Content.OcrOnlyWhenNativeTextUnavailable);
        Assert.Equal(240, service.Current.Content.PdfRasterizationDpi);
        Assert.Equal(4096, service.Current.Content.MaximumRasterDimension);
        Assert.Equal(65_536, service.Current.Content.MaximumOcrTextCharacters);
        Assert.Equal(256, service.Current.Content.MaximumTemporaryStorageMiB);
        Assert.Null(service.Current.Content.TesseractExecutablePath);
        Assert.False(service.Current.SemanticSearch.Enabled);
        Assert.True(service.Current.DeepIndexing.Enabled);
        Assert.Equal(IndexingLevel.Basic, service.Current.DeepIndexing.DefaultLevel);
        Assert.Equal(1, service.Current.DeepIndexing.MaximumConcurrency);
        Assert.True(service.Current.DeepIndexing.SummaryProcessingEnabled);
        Assert.True(service.Current.DeepIndexing.SemanticProcessingEnabled);
    }

    /// <summary>Verifies the master, category, and privacy controls persist independently.</summary>
    [Fact]
    public async Task SaveAsync_PersistsAdvancedDiagnosticsSettings()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"opensorse-{Guid.NewGuid():N}");
        var settingsFilePath = Path.Combine(directoryPath, "settings.json");
        try
        {
            var writer = new JsonConfigurationService(settingsFilePath, _ => null);
            await writer.InitializeAsync(CancellationToken.None);
            await writer.SaveAsync(new ApplicationSettings
            {
                Diagnostics = new DiagnosticsSettings
                {
                    EnableDiagnostics = true,
                    AiDiagnostics = true,
                    OcrAndTextExtractionDiagnostics = true,
                    ScanningDiagnostics = true,
                    DuplicateDetectionDiagnostics = true,
                    ShowUnredactedDiagnosticContent = true,
                },
            }, CancellationToken.None);

            var reader = new JsonConfigurationService(settingsFilePath, _ => null);
            await reader.InitializeAsync(CancellationToken.None);

            Assert.True(reader.Current.Diagnostics.EnableDiagnostics);
            Assert.True(reader.Current.Diagnostics.AiDiagnostics);
            Assert.True(reader.Current.Diagnostics.OcrAndTextExtractionDiagnostics);
            Assert.True(reader.Current.Diagnostics.ScanningDiagnostics);
            Assert.True(reader.Current.Diagnostics.DuplicateDetectionDiagnostics);
            Assert.True(reader.Current.Diagnostics.ShowUnredactedDiagnosticContent);
            Assert.False(reader.Current.Diagnostics.SearchAndIndexingDiagnostics);
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
    }

    /// <summary>Verifies pre-v0.9.1 JSON keeps established values while new opt-ins receive safe defaults.</summary>
    [Fact]
    public async Task InitializeAsync_PreV091Settings_DefaultsNewSwitchesOffWithoutResettingProviderValues()
    {
        var settingsFilePath = Path.Combine(Path.GetTempPath(), $"opensorse-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(settingsFilePath, """{"Ai":{"Enabled":true,"Endpoint":"http://127.0.0.1:11434","SelectedModel":"existing-model","RequestTimeoutSeconds":45}}""");

        try
        {
            var service = new JsonConfigurationService(settingsFilePath, _ => null);

            await service.InitializeAsync(CancellationToken.None);

            Assert.True(service.Current.Ai.Enabled);
            Assert.Equal("existing-model", service.Current.Ai.SelectedModel);
            Assert.Equal(45, service.Current.Ai.RequestTimeoutSeconds);
            Assert.False(service.Current.Ai.FileRenameSuggestionsEnabled);
            Assert.False(service.Current.Ai.FolderStructureSuggestionsEnabled);
            Assert.False(service.Current.Ai.RequestDiagnosticsEnabled);
            Assert.False(service.Current.Ai.DocumentTextInterpretationEnabled);
            Assert.False(service.Current.Features.ShowAdvancedFeatures);
            Assert.Equal(
                FeatureSettings.DefaultFilesPageDetailsPanelWidthRatio,
                service.Current.Features.FilesPageDetailsPanelWidthRatio);
            Assert.True(service.Current.Content.MetadataExtractionEnabled);
            Assert.False(service.Current.Content.OcrEnabled);
            Assert.False(service.Current.SemanticSearch.Enabled);
        }
        finally
        {
            File.Delete(settingsFilePath);
        }
    }

    /// <summary>
    /// Verifies logging file settings remain intact when the minimum level is overridden from the environment.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_PreservesLoggingOutputSettingsDuringEnvironmentOverride()
    {
        var settingsFilePath = Path.Combine(Path.GetTempPath(), $"opensorse-{Guid.NewGuid():N}.json");
        var logDirectoryPath = Path.Combine(Path.GetTempPath(), "OpenSorSe-Logs");
        await File.WriteAllTextAsync(
            settingsFilePath,
            JsonSerializer.Serialize(new
            {
                Logging = new
                {
                    MinimumLevel = "Warning",
                    FileLoggingEnabled = false,
                    LogDirectoryPath = logDirectoryPath,
                    RetainedFileCount = 3,
                },
            }));

        try
        {
            var service = new JsonConfigurationService(
                settingsFilePath,
                variableName => variableName == "OPENSORSE_LOGGING__MINIMUMLEVEL" ? "Error" : null);

            await service.InitializeAsync(CancellationToken.None);

            Assert.Equal(LogLevel.Error, service.Current.Logging.MinimumLevel);
            Assert.False(service.Current.Logging.FileLoggingEnabled);
            Assert.Equal(logDirectoryPath, service.Current.Logging.LogDirectoryPath);
            Assert.Equal(3, service.Current.Logging.RetainedFileCount);
        }
        finally
        {
            File.Delete(settingsFilePath);
        }
    }

    /// <summary>
    /// Verifies malformed owned JSON is preserved while safe defaults and a user-visible recovery warning are activated.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_MalformedJson_UsesDefaultsAndPreservesInvalidFile()
    {
        var settingsFilePath = Path.Combine(Path.GetTempPath(), $"opensorse-{Guid.NewGuid():N}.json");
        const string invalidJson = "{invalid";
        await File.WriteAllTextAsync(settingsFilePath, invalidJson);

        try
        {
            var service = new JsonConfigurationService(settingsFilePath, _ => null);

            await service.InitializeAsync(CancellationToken.None);

            Assert.Equal(LogLevel.Information, service.Current.Logging.MinimumLevel);
            Assert.NotNull(service.InitializationWarning);
            Assert.Equal(invalidJson, await File.ReadAllTextAsync(settingsFilePath));
        }
        finally
        {
            File.Delete(settingsFilePath);
        }
    }

    /// <summary>
    /// Verifies an oversized application-owned settings file activates safe defaults and remains untouched.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_OversizedSettings_UsesDefaultsAndPreservesFile()
    {
        var settingsFilePath = Path.Combine(Path.GetTempPath(), $"opensorse-{Guid.NewGuid():N}.json");
        await using (var stream = new FileStream(settingsFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(ConfigurationLimits.MaximumSettingsFileBytes + 1);
        }

        try
        {
            var service = new JsonConfigurationService(settingsFilePath, _ => null);

            await service.InitializeAsync(CancellationToken.None);

            Assert.Equal(LogLevel.Information, service.Current.Logging.MinimumLevel);
            Assert.NotNull(service.InitializationWarning);
            Assert.Equal(ConfigurationLimits.MaximumSettingsFileBytes + 1, new FileInfo(settingsFilePath).Length);
        }
        finally
        {
            File.Delete(settingsFilePath);
        }
    }

    /// <summary>Verifies syntactically valid but unsafe settings also recover without rewriting the owned file.</summary>
    [Fact]
    public async Task InitializeAsync_InvalidSettingsValues_UseDefaultsAndPreserveFile()
    {
        var settingsFilePath = Path.Combine(Path.GetTempPath(), $"opensorse-{Guid.NewGuid():N}.json");
        const string invalidSettings = "{\"Logging\":{\"RetainedFileCount\":0}}";
        await File.WriteAllTextAsync(settingsFilePath, invalidSettings);

        try
        {
            var service = new JsonConfigurationService(settingsFilePath, _ => null);

            await service.InitializeAsync(CancellationToken.None);

            Assert.Equal(7, service.Current.Logging.RetainedFileCount);
            Assert.NotNull(service.InitializationWarning);
            Assert.Equal(invalidSettings, await File.ReadAllTextAsync(settingsFilePath));
        }
        finally
        {
            File.Delete(settingsFilePath);
        }
    }

    /// <summary>
    /// Verifies saving and loading round-trips validated settings through the configured application path only.
    /// </summary>
    [Fact]
    public async Task SaveAsync_RoundTripsValidatedSettings()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"opensorse-{Guid.NewGuid():N}");
        var settingsFilePath = Path.Combine(directoryPath, "settings.json");

        try
        {
            var writer = new JsonConfigurationService(settingsFilePath, _ => null);
            await writer.InitializeAsync(CancellationToken.None);
            await writer.SaveAsync(CancellationToken.None);
            var reader = new JsonConfigurationService(settingsFilePath, _ => null);

            await reader.InitializeAsync(CancellationToken.None);

            Assert.Equal(LogLevel.Information, reader.Current.Logging.MinimumLevel);
            Assert.True(reader.Current.Logging.FileLoggingEnabled);
            Assert.Equal(7, reader.Current.Logging.RetainedFileCount);
            Assert.Empty(Directory.GetFiles(directoryPath, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies replacement settings are validated, persisted, and exposed only after successful serialization.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ReplacementSettings_PersistsAndUpdatesCurrent()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"opensorse-{Guid.NewGuid():N}");
        var settingsFilePath = Path.Combine(directoryPath, "settings.json");
        var tesseractExecutablePath = Path.Combine(directoryPath, "tools", "tesseract");
        var settings = new ApplicationSettings
        {
            Features = new FeatureSettings
            {
                ShowAdvancedFeatures = true,
                FilesPageDetailsPanelWidthRatio = 0.41,
            },
            Logging = new LoggingSettings
            {
                MinimumLevel = LogLevel.Warning,
                FileLoggingEnabled = false,
                RetainedFileCount = 3,
            },
            Ai = new AiSettings
            {
                Enabled = true,
                FileRenameSuggestionsEnabled = true,
                FolderStructureSuggestionsEnabled = false,
                RequestDiagnosticsEnabled = true,
                ShowUnredactedDiagnosticContent = true,
                Endpoint = "http://127.0.0.1:11434",
                SelectedModel = "llama3:latest",
                RequestTimeoutSeconds = 45,
                PreferenceAdaptationEnabled = false,
                DocumentTextInterpretationEnabled = true,
            },
            Catalog = new CatalogSettings
            {
                Enabled = true,
            },
            Content = new ContentSettings
            {
                MetadataExtractionEnabled = true,
                OcrEnabled = true,
                OcrOnlyWhenNativeTextUnavailable = false,
                MaximumPagesPerDocument = 12,
                MaximumFileSizeMiB = 20,
                OcrLanguage = "deu+eng",
                MaximumOcrDurationSeconds = 60,
                PdfRasterizationDpi = 300,
                MaximumRasterDimension = 5000,
                MaximumOcrTextCharacters = 32_768,
                MaximumTemporaryStorageMiB = 128,
                TesseractExecutablePath = tesseractExecutablePath,
                BackgroundProcessingEnabled = true,
            },
            SemanticSearch = new SemanticSearchSettings
            {
                Enabled = true,
                MaximumDocumentCount = 5000,
                MaximumResultCount = 100,
            },
            DeepIndexing = new DeepIndexingSettings
            {
                Enabled = true,
                DefaultLevel = IndexingLevel.Deep,
                ResourceMode = IndexingResourceMode.Fast,
                MaximumIndexSizeMiB = 2048,
                MaximumExtractedTextCharacters = 262_144,
                MaximumOcrTextCharacters = 131_072,
                MaximumSemanticChunksPerDocument = 16,
                DeletedFileRetentionDays = 60,
                FailedJobHistoryRetentionDays = 21,
                MaximumRetryCount = 5,
                MaximumConcurrency = 4,
                ProcessOnlyWhileIdle = true,
                ProcessOnlyWhileConnectedToPower = true,
                PauseBelowBatteryPercentage = 20,
                OcrProcessingEnabled = true,
                AiProcessingEnabled = true,
                SummaryProcessingEnabled = false,
                SemanticProcessingEnabled = false,
                RelationshipAnalysisEnabled = false,
                RelationshipExcludedExtensions = [".pem", ".key"],
                MaximumRelationshipCandidates = 320,
                MaximumRelationshipsPerFile = 80,
                MaximumSmartCollectionMembers = 1200,
                ArchiveIndexingEnabled = true,
                ExcludeGeneratedFolders = false,
                BinaryAndExecutableMetadataOnly = false,
                ProcessingWindowStartHour = 23,
                ProcessingWindowEndHour = 6,
            },
        };

        try
        {
            var service = new JsonConfigurationService(settingsFilePath, _ => null);

            await service.SaveAsync(settings, CancellationToken.None);

            Assert.Same(settings, service.Current);
            var reader = new JsonConfigurationService(settingsFilePath, _ => null);
            await reader.InitializeAsync(CancellationToken.None);
            Assert.Equal(LogLevel.Warning, reader.Current.Logging.MinimumLevel);
            Assert.False(reader.Current.Logging.FileLoggingEnabled);
            Assert.Equal(3, reader.Current.Logging.RetainedFileCount);
            Assert.True(reader.Current.Ai.Enabled);
            Assert.True(reader.Current.Features.ShowAdvancedFeatures);
            Assert.Equal(0.41, reader.Current.Features.FilesPageDetailsPanelWidthRatio);
            Assert.True(reader.Current.Ai.FileRenameSuggestionsEnabled);
            Assert.False(reader.Current.Ai.FolderStructureSuggestionsEnabled);
            Assert.True(reader.Current.Ai.RequestDiagnosticsEnabled);
            Assert.True(reader.Current.Ai.ShowUnredactedDiagnosticContent);
            Assert.Equal("llama3:latest", reader.Current.Ai.SelectedModel);
            Assert.Equal(45, reader.Current.Ai.RequestTimeoutSeconds);
            Assert.False(reader.Current.Ai.PreferenceAdaptationEnabled);
            Assert.True(reader.Current.Ai.DocumentTextInterpretationEnabled);
            Assert.True(reader.Current.Catalog.Enabled);
            Assert.True(reader.Current.Content.OcrEnabled);
            Assert.False(reader.Current.Content.OcrOnlyWhenNativeTextUnavailable);
            Assert.Equal(12, reader.Current.Content.MaximumPagesPerDocument);
            Assert.Equal(20, reader.Current.Content.MaximumFileSizeMiB);
            Assert.Equal("deu+eng", reader.Current.Content.OcrLanguage);
            Assert.Equal(60, reader.Current.Content.MaximumOcrDurationSeconds);
            Assert.Equal(300, reader.Current.Content.PdfRasterizationDpi);
            Assert.Equal(5000, reader.Current.Content.MaximumRasterDimension);
            Assert.Equal(32_768, reader.Current.Content.MaximumOcrTextCharacters);
            Assert.Equal(128, reader.Current.Content.MaximumTemporaryStorageMiB);
            Assert.Equal(tesseractExecutablePath, reader.Current.Content.TesseractExecutablePath);
            Assert.True(reader.Current.Content.BackgroundProcessingEnabled);
            Assert.True(reader.Current.SemanticSearch.Enabled);
            Assert.Equal(5000, reader.Current.SemanticSearch.MaximumDocumentCount);
            Assert.Equal(100, reader.Current.SemanticSearch.MaximumResultCount);
            Assert.Equal(IndexingLevel.Deep, reader.Current.DeepIndexing.DefaultLevel);
            Assert.Equal(IndexingResourceMode.Fast, reader.Current.DeepIndexing.ResourceMode);
            Assert.Equal(2048, reader.Current.DeepIndexing.MaximumIndexSizeMiB);
            Assert.Equal(4, reader.Current.DeepIndexing.MaximumConcurrency);
            Assert.Equal(20, reader.Current.DeepIndexing.PauseBelowBatteryPercentage);
            Assert.True(reader.Current.DeepIndexing.OcrProcessingEnabled);
            Assert.True(reader.Current.DeepIndexing.AiProcessingEnabled);
            Assert.False(reader.Current.DeepIndexing.SummaryProcessingEnabled);
            Assert.False(reader.Current.DeepIndexing.SemanticProcessingEnabled);
            Assert.False(reader.Current.DeepIndexing.RelationshipAnalysisEnabled);
            Assert.Equal([".pem", ".key"], reader.Current.DeepIndexing.RelationshipExcludedExtensions);
            Assert.Equal(320, reader.Current.DeepIndexing.MaximumRelationshipCandidates);
            Assert.Equal(80, reader.Current.DeepIndexing.MaximumRelationshipsPerFile);
            Assert.Equal(1200, reader.Current.DeepIndexing.MaximumSmartCollectionMembers);
            Assert.Equal(23, reader.Current.DeepIndexing.ProcessingWindowStartHour);
            Assert.Equal(6, reader.Current.DeepIndexing.ProcessingWindowEndHour);
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies cancellation occurs before configuration file access or parent-directory creation.
    /// </summary>
    [Fact]
    public async Task InitializeAndSaveAsync_PreCancelled_LeaveFilesystemUntouched()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"opensorse-{Guid.NewGuid():N}");
        var settingsFilePath = Path.Combine(directoryPath, "settings.json");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new JsonConfigurationService(settingsFilePath, _ => null);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.InitializeAsync(cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.SaveAsync(cancellation.Token));
        Assert.False(Directory.Exists(directoryPath));
    }

    /// <summary>
    /// Verifies relative configuration paths are rejected before runtime configuration can begin.
    /// </summary>
    [Fact]
    public void Constructor_RelativePath_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new JsonConfigurationService("settings.json", _ => null));
    }

    /// <summary>Verifies provider-controlled model identifiers cannot persist control characters.</summary>
    [Fact]
    public void ApplicationSettings_ControlCharacterModel_IsRejected()
    {
        var settings = new ApplicationSettings
        {
            Ai = new AiSettings { Enabled = true, SelectedModel = "bad\nmodel" },
        };

        Assert.Throws<ConfigurationValidationException>(settings.Validate);
    }

    /// <summary>Credentials and request components cannot be persisted inside the Ollama endpoint.</summary>
    [Theory]
    [InlineData("http://user:password@127.0.0.1:11434")]
    [InlineData("http://127.0.0.1:11434?token=secret")]
    [InlineData("http://127.0.0.1:11434/#secret")]
    public void ApplicationSettings_CredentialBearingAiEndpoint_IsRejected(string endpoint)
    {
        var settings = new ApplicationSettings
        {
            Ai = new AiSettings { Enabled = true, Endpoint = endpoint },
        };

        Assert.Throws<ConfigurationValidationException>(settings.Validate);
    }

    /// <summary>Verifies a corrupt Files-page proportion activates the established safe-default recovery path.</summary>
    [Fact]
    public async Task InitializeAsync_InvalidFilesPanelRatio_UsesDefaultsAndPreservesFile()
    {
        var settingsFilePath = Path.Combine(Path.GetTempPath(), $"opensorse-{Guid.NewGuid():N}.json");
        const string invalidSettings = """{"Features":{"FilesPageDetailsPanelWidthRatio":0.95}}""";
        await File.WriteAllTextAsync(settingsFilePath, invalidSettings);

        try
        {
            var service = new JsonConfigurationService(settingsFilePath, _ => null);

            await service.InitializeAsync(CancellationToken.None);

            Assert.Equal(
                FeatureSettings.DefaultFilesPageDetailsPanelWidthRatio,
                service.Current.Features.FilesPageDetailsPanelWidthRatio);
            Assert.NotNull(service.InitializationWarning);
            Assert.Equal(invalidSettings, await File.ReadAllTextAsync(settingsFilePath));
        }
        finally
        {
            File.Delete(settingsFilePath);
        }
    }

    /// <summary>Verifies independent service instances cannot interleave concurrent writes to one settings file.</summary>
    [Fact]
    public async Task SaveAsync_MultipleInstancesConcurrently_LeavesOneValidCompleteDocument()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"opensorse-{Guid.NewGuid():N}");
        var settingsFilePath = Path.Combine(directoryPath, "settings.json");
        try
        {
            var levels = new[]
            {
                LogLevel.Trace,
                LogLevel.Debug,
                LogLevel.Information,
                LogLevel.Warning,
                LogLevel.Error,
                LogLevel.Critical,
            };
            var services = Enumerable.Range(0, 24)
                .Select(_ => new JsonConfigurationService(settingsFilePath, _ => null))
                .ToArray();

            await Task.WhenAll(services.Select((service, index) =>
                service.SaveAsync(
                    new ApplicationSettings
                    {
                        Logging = new LoggingSettings
                        {
                            MinimumLevel = levels[index % levels.Length],
                        },
                    },
                    CancellationToken.None)));

            var reader = new JsonConfigurationService(settingsFilePath, _ => null);
            await reader.InitializeAsync(CancellationToken.None);
            Assert.Contains(reader.Current.Logging.MinimumLevel, levels);
            Assert.Null(reader.InitializationWarning);
            Assert.Empty(Directory.EnumerateFiles(directoryPath, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
    }

    /// <summary>Verifies cancellation preserves a previously persisted settings document byte-for-byte.</summary>
    [Fact]
    public async Task SaveAsync_PreCancelled_PreservesExistingDocument()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"opensorse-{Guid.NewGuid():N}");
        var settingsFilePath = Path.Combine(directoryPath, "settings.json");
        try
        {
            Directory.CreateDirectory(directoryPath);
            const string original = """{"Logging":{"MinimumLevel":"Warning"}}""";
            await File.WriteAllTextAsync(settingsFilePath, original);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var service = new JsonConfigurationService(settingsFilePath, _ => null);

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                service.SaveAsync(new ApplicationSettings(), cancellation.Token));

            Assert.Equal(original, await File.ReadAllTextAsync(settingsFilePath));
            Assert.Empty(Directory.EnumerateFiles(directoryPath, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
    }
}
