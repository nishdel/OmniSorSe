namespace OpenSorSe.Application.KnowledgeGraph;

/// <summary>Validates explicit legal transitions across four independent durable graph-state axes.</summary>
public sealed class GraphStateValidator : IGraphStateValidator
{
    private static readonly IReadOnlyDictionary<GraphRunControlState, IReadOnlySet<GraphRunControlState>> RunTransitions =
        new Dictionary<GraphRunControlState, IReadOnlySet<GraphRunControlState>>
        {
            [GraphRunControlState.Pending] = Set(GraphRunControlState.Running, GraphRunControlState.CancelRequested),
            [GraphRunControlState.Running] = Set(GraphRunControlState.PauseRequested, GraphRunControlState.CancelRequested, GraphRunControlState.Complete),
            [GraphRunControlState.PauseRequested] = Set(GraphRunControlState.Running, GraphRunControlState.Paused, GraphRunControlState.CancelRequested),
            [GraphRunControlState.Paused] = Set(GraphRunControlState.Running, GraphRunControlState.CancelRequested),
            [GraphRunControlState.CancelRequested] = Set(GraphRunControlState.Cancelled),
            [GraphRunControlState.Cancelled] = Set<GraphRunControlState>(),
            [GraphRunControlState.Complete] = Set<GraphRunControlState>(),
        };

    private static readonly IReadOnlyDictionary<GraphJobExecutionState, IReadOnlySet<GraphJobExecutionState>> JobTransitions =
        new Dictionary<GraphJobExecutionState, IReadOnlySet<GraphJobExecutionState>>
        {
            [GraphJobExecutionState.Pending] = Set(
                GraphJobExecutionState.Running,
                GraphJobExecutionState.Cancelled,
                GraphJobExecutionState.WaitingForDependency,
                GraphJobExecutionState.WaitingForResources,
                GraphJobExecutionState.PermanentFailure),
            [GraphJobExecutionState.Running] = Set(
                GraphJobExecutionState.Complete,
                GraphJobExecutionState.Cancelled,
                GraphJobExecutionState.RetryableFailure,
                GraphJobExecutionState.PermanentFailure,
                GraphJobExecutionState.WaitingForDependency,
                GraphJobExecutionState.WaitingForResources),
            [GraphJobExecutionState.RetryableFailure] = Set(GraphJobExecutionState.Pending, GraphJobExecutionState.Running, GraphJobExecutionState.Cancelled),
            [GraphJobExecutionState.PermanentFailure] = Set(GraphJobExecutionState.Pending, GraphJobExecutionState.Cancelled),
            [GraphJobExecutionState.WaitingForDependency] = Set(GraphJobExecutionState.Pending, GraphJobExecutionState.Running, GraphJobExecutionState.Cancelled, GraphJobExecutionState.PermanentFailure),
            [GraphJobExecutionState.WaitingForResources] = Set(GraphJobExecutionState.Pending, GraphJobExecutionState.Running, GraphJobExecutionState.Cancelled, GraphJobExecutionState.PermanentFailure),
            [GraphJobExecutionState.Cancelled] = Set<GraphJobExecutionState>(),
            [GraphJobExecutionState.Complete] = Set<GraphJobExecutionState>(),
        };

    /// <inheritdoc />
    public GraphStateValidationResult Validate(GraphStateVector state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!Enum.IsDefined(state.RunControl) || !Enum.IsDefined(state.JobExecution) ||
            !Enum.IsDefined(state.Freshness) || !Enum.IsDefined(state.Integrity))
        {
            return GraphStateValidationResult.Invalid("unknown-state", "A persisted graph state contains an unknown value.");
        }

        if (state.RunControl == GraphRunControlState.Cancelled &&
            state.JobExecution is GraphJobExecutionState.Pending or GraphJobExecutionState.Running or
                GraphJobExecutionState.WaitingForDependency or GraphJobExecutionState.WaitingForResources)
        {
            return GraphStateValidationResult.Invalid("cancel-not-acknowledged", "A cancelled run cannot retain claimable, active, or waiting work.");
        }

        if (state.RunControl == GraphRunControlState.Complete &&
            state.JobExecution is GraphJobExecutionState.Pending or GraphJobExecutionState.Running or
                GraphJobExecutionState.WaitingForDependency or GraphJobExecutionState.WaitingForResources)
        {
            return GraphStateValidationResult.Invalid("complete-with-active-work", "A complete run cannot retain active or waiting work.");
        }

        if (state.JobExecution == GraphJobExecutionState.Running &&
            state.RunControl is not (GraphRunControlState.Running or GraphRunControlState.PauseRequested or GraphRunControlState.CancelRequested))
        {
            return GraphStateValidationResult.Invalid("running-without-admission", "A running job requires a running or draining run.");
        }

        return GraphStateValidationResult.Valid();
    }

    /// <inheritdoc />
    public GraphStateValidationResult ValidateTransition(GraphStateVector previous, GraphStateVector next)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);
        var previousValidation = Validate(previous);
        if (!previousValidation.IsValid)
        {
            return GraphStateValidationResult.Invalid("invalid-previous-state", previousValidation.Message);
        }

        var nextValidation = Validate(next);
        if (!nextValidation.IsValid)
        {
            return nextValidation;
        }

        if (previous.RunControl != next.RunControl && !RunTransitions[previous.RunControl].Contains(next.RunControl))
        {
            return GraphStateValidationResult.Invalid("illegal-run-transition", "The requested run-control transition is not legal.");
        }

        if (previous.JobExecution != next.JobExecution && !JobTransitions[previous.JobExecution].Contains(next.JobExecution))
        {
            return GraphStateValidationResult.Invalid("illegal-job-transition", "The requested job transition is not legal.");
        }

        if (previous.Freshness != next.Freshness &&
            !((previous.Freshness == GraphFreshnessState.Current && next.Freshness == GraphFreshnessState.Stale) ||
              (previous.Freshness == GraphFreshnessState.Stale && next.Freshness == GraphFreshnessState.Current)))
        {
            return GraphStateValidationResult.Invalid("illegal-freshness-transition", "The requested freshness transition is not legal.");
        }

        if (previous.Integrity != next.Integrity &&
            !((previous.Integrity == GraphIntegrityState.Valid && next.Integrity == GraphIntegrityState.RepairRequired) ||
              (previous.Integrity == GraphIntegrityState.RepairRequired && next.Integrity == GraphIntegrityState.Valid)))
        {
            return GraphStateValidationResult.Invalid("illegal-integrity-transition", "The requested integrity transition is not legal.");
        }

        return GraphStateValidationResult.Valid();
    }

    private static IReadOnlySet<T> Set<T>(params T[] values) where T : notnull => new HashSet<T>(values);
}
