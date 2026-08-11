using Xunit;

// SQLite integration fixtures create independent databases, but their cleanup
// deliberately invokes SQLite's process-global pool reset. Running fixture
// classes in parallel therefore creates artificial cross-test interference and
// severe platform-dependent lock contention. Concurrency remains covered by
// the tests that explicitly coordinate concurrent readers, writers, and
// cancellation within a single fixture.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
