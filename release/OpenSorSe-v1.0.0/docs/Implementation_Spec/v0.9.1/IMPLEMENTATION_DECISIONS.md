# v0.9.1 Implementation Decisions

## Save-driven active visibility

Dependent controls inside Settings react to the editable draft immediately. Shell navigation and Results actions update only after a successful explicit Save, matching the established persistence workflow and avoiding unsaved draft state changing the rest of the application.

## Conservative feature classification

Scanning, Results, duplicates, catalog browsing/search, saved searches, tags, rules, Settings, About, and safety feedback remain regular. Historical comparison, detailed diagnostics, and operation-history internals are advanced because they are specialist review/troubleshooting surfaces rather than primary workflows.

## Two supported AI capabilities

The earlier mixed file-organization response is narrowed to file rename only. Tags, category, and destination values are not requested from AI in v0.9.1. Deterministic tagging/classification and existing user-managed tags remain non-AI workflows. Folder structure remains a separate hierarchy-and-assignment proposal.

## Unknown JSON properties

Unknown properties are ignored for forward compatibility. Required properties, task/status values, identities, counts, graphs, confidence, and all filename/path safety rules remain strict. Markdown-fenced output is rejected because the provider was explicitly instructed to return JSON only.

## Reject-whole-response policy

No partially valid suggestion is published. A duplicate, invented identity, invalid folder graph, unsafe name, or other issue rejects the complete response so no ambiguous subset can reach the review workflow.

## Exact-model preflight before generation

After local AI, capability, context, endpoint, model, and timeout gates pass, generation performs a bounded `/api/tags` preflight and requires an ordinal match for the configured model. This makes a stale or unavailable model explicit before the larger generation request. The preflight is never performed while AI or the requested capability is disabled, and its timeout/cancellation behavior uses the same request-scoped policy as generation.

## Essential setup is regular AI UI

Endpoint, connection check, discovery/refresh, exact model selection, timeout, and capability controls remain visible whenever AI is enabled. Advanced mode is reserved for technical inspection such as raw AI request diagnostics, detailed logging, comparison, Diagnostics, and Operation history. Raw request capture additionally requires its own default-off switch and is cleared when any of its three gates becomes inactive.

## Safe duplicate comparison opening

Duplicate View may open only validated paths from the current in-memory snapshot. The Desktop owns this narrow launcher abstraction; commands are capped at five targets, continue after individual failures, and never concatenate a shell command. Historical catalog paths and unknown/stale rows are not eligible.

## Review decisions are not execution

Accept and edit actions validate again and record a local decision where supported. They do not invoke `File`, `Directory`, action executor, rules executor, or undo APIs. Automatic filesystem changes remain outside v0.9.1 and require a separately specified mature preview/confirmation workflow.

## v1.0 boundary

Plugins, full localization, packaging overhaul, multi-provider architecture, content understanding, semantic indexes, background agents, automatic batch actions, and broad release engineering remain deferred.
