using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenSorSe.Application;
using OpenSorSe.Application.Models;
using OpenSorSe.Core.Logging;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies process-lifetime processing-session state transitions.</summary>
public sealed class ProcessingSessionManagerTests
{
    /// <summary>Verifies completed sessions receive unique identifiers, terminal state, and explicit close behavior.</summary>
    [Fact]
    public async Task RunAsync_CompletedProcessing_TracksAndClosesSession()
    {
        var manager = new ProcessingSessionManager(new CompletedOrchestrator(), new Logging());

        var result = await manager.RunAsync(Request());

        Assert.Equal(ProcessingSessionStatus.Completed, result.Session.Status);
        Assert.StartsWith("session:", result.Session.Id);
        Assert.NotNull(result.Session.CompletedAtUtc);
        Assert.Single(manager.Sessions);
        Assert.True(manager.TryClose(result.Session.Id));
        Assert.Equal(ProcessingSessionStatus.Closed, manager.Sessions[0].Status);
    }

    /// <summary>Verifies unexpected orchestrator failure remains represented by a user-safe terminal session.</summary>
    [Fact]
    public async Task RunAsync_UnexpectedFailure_ReturnsTrackedFailedSession()
    {
        var manager = new ProcessingSessionManager(new FailingOrchestrator(), new Logging());

        var result = await manager.RunAsync(Request());

        Assert.Equal(ProcessingSessionStatus.Failed, result.Session.Status);
        Assert.Null(result.Processing);
        Assert.Equal("The processing session could not be completed.", result.Session.FailureMessage);
    }

    /// <summary>Verifies caller cancellation is represented as a safe terminal session instead of escaping as success.</summary>
    [Fact]
    public async Task RunAsync_CallerCancellation_ReturnsCancelledTerminalSession()
    {
        var manager = new ProcessingSessionManager(new CancellingOrchestrator(), new Logging());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await manager.RunAsync(Request(), cancellationToken: cancellation.Token);

        Assert.Equal(ProcessingSessionStatus.Cancelled, result.Session.Status);
        Assert.NotNull(result.Session.CompletedAtUtc);
        Assert.Null(result.Processing);
        Assert.Single(manager.Sessions);
    }

    /// <summary>Verifies failing observers are isolated so later observers and processing still complete.</summary>
    [Fact]
    public async Task SessionChanged_ObserverFailure_DoesNotInterruptLifecycle()
    {
        using var logging = new LoggingService();
        logging.Initialize(new LoggingOptions(LogLevel.Trace, FileLoggingEnabled: false));
        var manager = new ProcessingSessionManager(new CompletedOrchestrator(), logging);
        var received = new List<ProcessingSessionStatus>();
        manager.SessionChanged += (_, _) => throw new InvalidOperationException("observer failure");
        manager.SessionChanged += (_, session) => received.Add(session.Status);

        var result = await manager.RunAsync(Request());

        Assert.Equal(ProcessingSessionStatus.Completed, result.Session.Status);
        Assert.Equal(
            [ProcessingSessionStatus.Running, ProcessingSessionStatus.Completed],
            received);
        Assert.Equal(2L, logging.GetStatistics().WarningEntries);
    }

    /// <summary>Verifies process-lifetime session history remains bounded while retaining the newest terminal sessions.</summary>
    [Fact]
    public async Task RunAsync_MoreThanRetentionLimit_EvictsOldestTerminalSessions()
    {
        var manager = new ProcessingSessionManager(new CompletedOrchestrator(), new Logging());
        string? firstSessionId = null;
        string? newestSessionId = null;
        for (var index = 0; index < ProcessingSessionLimits.MaximumRetainedSessions + 44; index++)
        {
            var result = await manager.RunAsync(Request());
            firstSessionId ??= result.Session.Id;
            newestSessionId = result.Session.Id;
        }

        Assert.Equal(ProcessingSessionLimits.MaximumRetainedSessions, manager.Sessions.Count);
        Assert.DoesNotContain(manager.Sessions, session => session.Id == firstSessionId);
        Assert.Contains(manager.Sessions, session => session.Id == newestSessionId);
        Assert.All(manager.Sessions, session => Assert.Equal(ProcessingSessionStatus.Completed, session.Status));
    }

    /// <summary>Verifies concurrent session creation remains coherent and produces unique identifiers.</summary>
    [Fact]
    public async Task RunAsync_ConcurrentSessions_ProduceUniqueCoherentHistory()
    {
        var manager = new ProcessingSessionManager(new CompletedOrchestrator(), new Logging());

        var results = await Task.WhenAll(
            Enumerable.Range(0, 64).Select(_ => manager.RunAsync(Request())));

        Assert.Equal(64, results.Select(result => result.Session.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(64, manager.Sessions.Count);
        Assert.All(manager.Sessions, session => Assert.Equal(ProcessingSessionStatus.Completed, session.Status));
    }

    private static ProcessingRequest Request() => new(new ScanRequest(["C:\\Root"], ScanOptions.Default), []);
    private static ProcessingResult CompletedResult() => new(ProcessingStatus.Completed, new ScanResult([], [], new ScanStatistics(0, 0, 0), [], ScanStatus.Completed, TimeSpan.Zero), null, null, null, null, null, null, null);

    private sealed class CompletedOrchestrator : IProcessingOrchestrator
    {
        public Task<ProcessingResult> ProcessAsync(ProcessingRequest request, IProgress<ProcessingProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(CompletedResult());
    }
    private sealed class FailingOrchestrator : IProcessingOrchestrator
    {
        public Task<ProcessingResult> ProcessAsync(ProcessingRequest request, IProgress<ProcessingProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromException<ProcessingResult>(new InvalidOperationException());
    }
    private sealed class CancellingOrchestrator : IProcessingOrchestrator
    {
        public Task<ProcessingResult> ProcessAsync(ProcessingRequest request, IProgress<ProcessingProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CompletedResult());
        }
    }
    private sealed class Logging : ILoggingService
    {
        public void Initialize(LogLevel minimumLevel) { }
        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;
        public void Dispose() { }
    }
}
