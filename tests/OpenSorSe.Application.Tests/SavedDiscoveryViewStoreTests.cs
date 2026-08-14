using System.Text.Json;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Application.Tests;

/// <summary>Validates local dynamic Saved View rule persistence independently from result membership.</summary>
public sealed class SavedDiscoveryViewStoreTests
{
    /// <summary>A canonical rule survives reopen and retains typed OR/AND filter identity.</summary>
    [Fact]
    public async Task SavedViewPersistsCanonicalQueryRuleWithoutMembership()
    {
        using var fixture = new StoreFixture();
        var store = fixture.CreateStore();
        var created = DateTimeOffset.UtcNow;
        var view = new SavedDiscoveryView(
            "saved-view:finance-2025",
            "Finance 2025",
            new DiscoveryQueryState(
                "insurance",
                [
                    new SearchFilter("theme:finance", SearchFilterKind.SmartTagTheme, "theme.finance", "Theme: Finance"),
                    new SearchFilter("theme:insurance", SearchFilterKind.SmartTagTheme, "theme.insurance", "Theme: Insurance"),
                    new SearchFilter("modified:2025", SearchFilterKind.ModifiedYear, "2025", "Modified year: 2025"),
                ]),
            1,
            created,
            created);

        await store.SaveAsync(view);
        var reopened = fixture.CreateStore();
        var loaded = Assert.Single(await reopened.ListAsync());

        Assert.Equal(view.Id, loaded.Id);
        Assert.Equal("insurance", loaded.Query.QueryText);
        Assert.Equal(3, loaded.Query.Filters.Count);
        Assert.DoesNotContain("Results", await File.ReadAllTextAsync(fixture.Path), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Updating and deleting operate on one stable rule identity without touching the index.</summary>
    [Fact]
    public async Task SavedViewCanBeRenamedUpdatedAndDeleted()
    {
        using var fixture = new StoreFixture();
        var store = fixture.CreateStore();
        var created = DateTimeOffset.UtcNow;
        var saved = await store.SaveAsync(new SavedDiscoveryView(
            "saved-view:review",
            "Review",
            new DiscoveryQueryState("", [
                new SearchFilter("tag:review", SearchFilterKind.SmartTagUser, "user.review", "User Tag: Review"),
            ]),
            1,
            created,
            created));

        await store.SaveAsync(saved with
        {
            Name = "Review queue",
            Query = new DiscoveryQueryState("invoice", saved.Query.Filters),
            UpdatedAtUtc = created.AddMinutes(1),
        });

        var updated = Assert.Single(await store.ListAsync());
        Assert.Equal("Review queue", updated.Name);
        Assert.Equal("invoice", updated.Query.QueryText);
        Assert.True(await store.DeleteAsync(updated.Id));
        Assert.Empty(await store.ListAsync());
    }

    /// <summary>Malformed or obsolete rule data fails closed without exposing partial state.</summary>
    [Fact]
    public async Task InvalidSavedViewStoreIsIgnoredSafely()
    {
        using var fixture = new StoreFixture();
        await File.WriteAllTextAsync(fixture.Path, JsonSerializer.Serialize(new
        {
            SchemaVersion = 99,
            Views = Array.Empty<object>(),
        }));

        Assert.Empty(await fixture.CreateStore().ListAsync());
    }

    private sealed class StoreFixture : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "OmniSorSe-saved-view-tests",
            Guid.NewGuid().ToString("N"));

        public StoreFixture()
        {
            Directory.CreateDirectory(_root);
            Path = System.IO.Path.Combine(_root, "saved-discovery-views.json");
        }

        public string Path { get; }

        public JsonSavedDiscoveryViewStore CreateStore() => new(Path, new LoggingService());

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
