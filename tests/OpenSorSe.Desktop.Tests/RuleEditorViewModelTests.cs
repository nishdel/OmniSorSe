using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Rules.Models;

namespace OpenSorSe.Desktop.Tests;

/// <summary>
/// Verifies deterministic in-memory rule editing without evaluation or persistence.
/// </summary>
public sealed class RuleEditorViewModelTests
{
    /// <summary>
    /// Verifies adding and replacing a rule preserves its original order.
    /// </summary>
    [Fact]
    public void AddOrUpdate_AddsAndReplacesWithoutReordering()
    {
        var first = CreateRule("first", "First");
        var second = CreateRule("second", "Second");
        var viewModel = new RuleEditorViewModel();

        Assert.True(viewModel.AddOrUpdate(first).IsValid);
        Assert.True(viewModel.AddOrUpdate(second).IsValid);
        var replacement = first with { Name = "First replacement" };
        Assert.True(viewModel.AddOrUpdate(replacement).IsValid);

        Assert.Equal([replacement, second], viewModel.Rules);
        Assert.Equal("Rule updated.", viewModel.StatusText);
    }

    /// <summary>
    /// Verifies invalid rules are rejected before they mutate the editor collection.
    /// </summary>
    [Fact]
    public void AddOrUpdate_InvalidRule_ReturnsValidationWithoutMutation()
    {
        var viewModel = new RuleEditorViewModel();
        var invalid = new FileRule("", "", 0, [], new RuleAction(RuleActionKind.Move));

        var validation = viewModel.AddOrUpdate(invalid);

        Assert.False(validation.IsValid);
        Assert.NotEmpty(validation.Errors);
        Assert.Empty(viewModel.Rules);
        Assert.True(viewModel.IsEmpty);
        Assert.Contains("Rules produce proposals", viewModel.EmptyStateMessage, StringComparison.Ordinal);
        Assert.Contains("no rule directly executes a file change", viewModel.EmptyStateMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies toggling and deleting affect only the selected immutable rule.
    /// </summary>
    [Fact]
    public void SelectedRuleCommands_ToggleAndDeleteSelectedRule()
    {
        var rule = CreateRule("first", "First");
        var viewModel = new RuleEditorViewModel();
        Assert.True(viewModel.AddOrUpdate(rule).IsValid);

        viewModel.ToggleSelectedRuleCommand.Execute(null);
        var toggled = Assert.Single(viewModel.Rules);
        Assert.False(toggled.IsEnabled);
        viewModel.DeleteSelectedRuleCommand.Execute(null);

        Assert.Empty(viewModel.Rules);
        Assert.Null(viewModel.SelectedRule);
        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.HasSelectedRule);
    }

    /// <summary>
    /// Verifies saving emits a read-only snapshot without persistence or rule execution.
    /// </summary>
    [Fact]
    public void Save_EmitsCurrentValidSnapshot()
    {
        var rule = CreateRule("first", "First");
        var viewModel = new RuleEditorViewModel();
        Assert.True(viewModel.AddOrUpdate(rule).IsValid);
        IReadOnlyList<FileRule>? savedRules = null;
        viewModel.SaveRequested += (_, rules) => savedRules = rules;

        viewModel.SaveCommand.Execute(null);

        var snapshot = Assert.IsAssignableFrom<IReadOnlyList<FileRule>>(savedRules);
        Assert.Equal([rule], snapshot);
        Assert.Equal("Save requested.", viewModel.StatusText);
    }

    private static FileRule CreateRule(string identifier, string name) =>
        new(identifier, name, 0, [new RuleCondition(RuleConditionKind.ExtensionEquals, StringValue: ".txt")], new RuleAction(RuleActionKind.Move, "C:\\Destination"));
}
