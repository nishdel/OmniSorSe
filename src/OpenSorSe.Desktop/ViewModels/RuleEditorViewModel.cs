using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Rules.Models;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Edits an in-memory ordered collection of file rules without persisting or executing them.
/// </summary>
public sealed class RuleEditorViewModel : ViewModelBase
{
    private readonly ObservableCollection<FileRule> _rules = [];
    private FileRule? _selectedRule;
    private string _statusText = "Ready";

    /// <summary>
    /// Initializes commands for in-memory rule maintenance.
    /// </summary>
    public RuleEditorViewModel()
    {
        Rules = new ReadOnlyObservableCollection<FileRule>(_rules);
        DeleteSelectedRuleCommand = new RelayCommand(DeleteSelectedRule, () => SelectedRule is not null);
        ToggleSelectedRuleCommand = new RelayCommand(ToggleSelectedRule, () => SelectedRule is not null);
        SaveCommand = new RelayCommand(RequestSave);
    }

    /// <summary>
    /// Occurs when the user requests persistence of the current valid rule snapshot.
    /// </summary>
    public event EventHandler<IReadOnlyList<FileRule>>? SaveRequested;

    /// <summary>
    /// Gets the in-memory rules in supplied user order.
    /// </summary>
    public ReadOnlyObservableCollection<FileRule> Rules { get; }

    /// <summary>
    /// Gets whether any in-memory rules are available for review.
    /// </summary>
    public bool HasRules => _rules.Count > 0;

    /// <summary>
    /// Gets whether the page should show its no-rules explanation.
    /// </summary>
    public bool IsEmpty => !HasRules;

    /// <summary>
    /// Gets whether a rule is selected for the available in-memory edit actions.
    /// </summary>
    public bool HasSelectedRule => SelectedRule is not null;

    /// <summary>
    /// Gets the user-facing current-release no-rules explanation.
    /// </summary>
    public string EmptyStateMessage => "No rules are loaded in this application session. OpenSorSe 1.0 does not create, persist, or execute rules, and scanning remains read-only.";

    /// <summary>
    /// Gets or sets the selected rule for non-executing edit operations.
    /// </summary>
    public FileRule? SelectedRule
    {
        get => _selectedRule;
        set
        {
            if (SetProperty(ref _selectedRule, value))
            {
                DeleteSelectedRuleCommand.NotifyCanExecuteChanged();
                ToggleSelectedRuleCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(HasSelectedRule));
            }
        }
    }

    /// <summary>
    /// Gets the current user-safe editor status.
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>
    /// Gets the command that deletes the selected in-memory rule.
    /// </summary>
    public IRelayCommand DeleteSelectedRuleCommand { get; }

    /// <summary>
    /// Gets the command that toggles only the selected rule's enabled state.
    /// </summary>
    public IRelayCommand ToggleSelectedRuleCommand { get; }

    /// <summary>
    /// Gets the command that emits a save request for a valid snapshot.
    /// </summary>
    public IRelayCommand SaveCommand { get; }

    /// <summary>
    /// Replaces the editor contents with a validated ordered rule collection.
    /// </summary>
    /// <param name="rules">Rules to load in their supplied order.</param>
    public void Load(IReadOnlyList<FileRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var validation = ValidateRules(rules);
        if (!validation.IsValid)
        {
            throw new ArgumentException(validation.Errors[0], nameof(rules));
        }

        _rules.Clear();
        foreach (var rule in rules)
        {
            _rules.Add(rule);
        }

        SelectedRule = null;
        StatusText = "Rules loaded.";
        NotifyRuleCollectionChanged();
    }

    /// <summary>
    /// Adds a new valid rule or replaces an existing rule with the same identifier without reordering it.
    /// </summary>
    /// <param name="rule">The immutable rule to add or replace.</param>
    /// <returns>The validation result for the attempted edit.</returns>
    public RuleEditorValidationResult AddOrUpdate(FileRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var candidateRules = _rules.ToList();
        var matchingIndex = candidateRules.FindIndex(candidate => string.Equals(candidate.Id, rule.Id, StringComparison.OrdinalIgnoreCase));
        if (matchingIndex >= 0)
        {
            candidateRules[matchingIndex] = rule;
        }
        else
        {
            candidateRules.Add(rule);
        }

        var validation = ValidateRules(candidateRules);
        if (!validation.IsValid)
        {
            StatusText = validation.Errors[0];
            return validation;
        }

        if (matchingIndex >= 0)
        {
            _rules[matchingIndex] = rule;
            StatusText = "Rule updated.";
        }
        else
        {
            _rules.Add(rule);
            StatusText = "Rule added.";
        }

        SelectedRule = rule;
        NotifyRuleCollectionChanged();
        return validation;
    }

    /// <summary>
    /// Validates rule data without evaluating rules or accessing the filesystem.
    /// </summary>
    /// <param name="rules">Rules to validate in supplied order.</param>
    /// <returns>Deterministic validation feedback.</returns>
    public RuleEditorValidationResult ValidateRules(IReadOnlyList<FileRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var errors = new List<string>();
        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            if (rule is null)
            {
                errors.Add("A rule cannot be null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(rule.Id))
            {
                errors.Add("A rule identifier is required.");
            }
            else if (!identifiers.Add(rule.Id))
            {
                errors.Add("Rule identifiers must be unique.");
            }

            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                errors.Add("A rule name is required.");
            }

            if (rule.Conditions is null || rule.Conditions.Count == 0 || rule.Conditions.Any(condition => condition is null))
            {
                errors.Add("At least one complete rule condition is required.");
            }
            else if (rule.Conditions.Any(condition => !IsValidCondition(condition)))
            {
                errors.Add("A rule condition is invalid.");
            }

            if (!IsValidAction(rule.Action))
            {
                errors.Add("The rule action is invalid.");
            }
        }

        return new RuleEditorValidationResult(errors.Count == 0, errors.AsReadOnly());
    }

    private static bool IsValidAction(RuleAction? action) => action is not null && action.Kind switch
    {
        RuleActionKind.NoAction => action.DestinationPath is null && action.NameTemplate is null,
        RuleActionKind.Move or RuleActionKind.Copy => !string.IsNullOrWhiteSpace(action.DestinationPath) && action.NameTemplate is null,
        RuleActionKind.Rename => action.DestinationPath is null && !string.IsNullOrWhiteSpace(action.NameTemplate),
        RuleActionKind.Delete => action.DestinationPath is null && action.NameTemplate is null,
        _ => false,
    };

    private static bool IsValidCondition(RuleCondition condition) => condition.Kind switch
    {
        RuleConditionKind.FileCategoryEquals =>
            condition.CategoryValue is { } category && category != FileCategory.Unknown &&
            condition.StringValue is null && condition.LongValue is null && condition.DuplicateStatusValue is null,
        RuleConditionKind.DuplicateStatusEquals =>
            condition.DuplicateStatusValue is not null && condition.StringValue is null && condition.LongValue is null && condition.CategoryValue is null,
        RuleConditionKind.ExtensionEquals =>
            !string.IsNullOrWhiteSpace(condition.StringValue) && condition.StringValue.StartsWith(".", StringComparison.Ordinal) &&
            condition.LongValue is null && condition.CategoryValue is null && condition.DuplicateStatusValue is null,
        RuleConditionKind.ExactFileNameEquals =>
            !string.IsNullOrWhiteSpace(condition.StringValue) && condition.LongValue is null && condition.CategoryValue is null && condition.DuplicateStatusValue is null,
        RuleConditionKind.MinimumSizeInBytes or RuleConditionKind.MaximumSizeInBytes =>
            condition.LongValue is >= 0 && condition.StringValue is null && condition.CategoryValue is null && condition.DuplicateStatusValue is null,
        _ => false,
    };

    private void DeleteSelectedRule()
    {
        if (SelectedRule is not null && _rules.Remove(SelectedRule))
        {
            SelectedRule = null;
            StatusText = "Rule deleted.";
            NotifyRuleCollectionChanged();
        }
    }

    private void ToggleSelectedRule()
    {
        if (SelectedRule is null)
        {
            return;
        }

        var index = _rules.IndexOf(SelectedRule);
        if (index >= 0)
        {
            var replacement = SelectedRule with { IsEnabled = !SelectedRule.IsEnabled };
            _rules[index] = replacement;
            SelectedRule = replacement;
            StatusText = replacement.IsEnabled ? "Rule enabled." : "Rule disabled.";
        }
    }

    private void RequestSave()
    {
        var validation = ValidateRules(_rules);
        if (!validation.IsValid)
        {
            StatusText = validation.Errors[0];
            return;
        }

        SaveRequested?.Invoke(this, Array.AsReadOnly(_rules.ToArray()));
        StatusText = "Save requested.";
    }

    private void NotifyRuleCollectionChanged()
    {
        OnPropertyChanged(nameof(HasRules));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
