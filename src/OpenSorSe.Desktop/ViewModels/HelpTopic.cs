namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Identifies one centrally maintained internal Help topic.</summary>
public enum HelpTopicId
{
    /// <summary>General Help overview.</summary>
    HelpOverview,
    /// <summary>Dashboard guidance.</summary>
    Dashboard,
    /// <summary>Folder selection and scan guidance.</summary>
    ScanFolders,
    /// <summary>Results exploration guidance.</summary>
    Results,
    /// <summary>Grouped duplicate-review guidance.</summary>
    DuplicateView,
    /// <summary>Saved-catalog guidance.</summary>
    SavedCatalog,
    /// <summary>Catalog metadata-search guidance.</summary>
    CatalogSearch,
    /// <summary>Historical comparison guidance.</summary>
    CompareSnapshots,
    /// <summary>Local hybrid Semantic Search Beta guidance.</summary>
    SemanticSearch,
    /// <summary>Folder restructuring history and diagram guidance.</summary>
    StructureHistory,
    /// <summary>Deterministic rule guidance.</summary>
    Rules,
    /// <summary>Settings guidance.</summary>
    Settings,
    /// <summary>Application diagnostics guidance.</summary>
    Diagnostics,
    /// <summary>Operation-history guidance.</summary>
    OperationHistory,
    /// <summary>Optional Ollama setup guidance.</summary>
    AiSetup,
    /// <summary>Review-only AI suggestion guidance.</summary>
    AiSuggestions,
    /// <summary>Advanced-interface guidance.</summary>
    AdvancedFeatures,
    /// <summary>Product identity guidance.</summary>
    About,
}

/// <summary>Contains structured, maintainable content for one internal Help topic.</summary>
public sealed record HelpTopic(
    HelpTopicId Id,
    string Title,
    string Purpose,
    string Reads,
    string Changes,
    string DoesNotChange,
    IReadOnlyList<string> Workflow,
    IReadOnlyList<string> ImportantControls,
    string EmptyStates,
    string CommonErrors,
    string SafetyNotes,
    IReadOnlyList<HelpTopicId> RelatedTopics);

/// <summary>Owns the complete bounded OpenSorSe 1.0 internal Help catalog.</summary>
public static class HelpCatalog
{
    private static readonly IReadOnlyList<HelpTopic> TopicsValue = Array.AsReadOnly(
    [
        Topic(HelpTopicId.HelpOverview, "Help", "Choose a topic to understand a page before using it.",
            "Only this built-in help catalog.", "Only the selected Help topic.",
            "Files, settings, snapshots, tags, and saved searches.",
            ["Choose a topic.", "Read its workflow and safety notes.", "Use Back to previous page when available."],
            ["Topic list", "Back to previous page"], "Select a topic when no details are shown.",
            "Unknown topic requests open this overview.", "Help is local and never opens a browser automatically.",
            [HelpTopicId.Dashboard, HelpTopicId.Settings]),
        Topic(HelpTopicId.Dashboard, "Dashboard", "Summarizes the latest completed scan and offers primary workflow shortcuts.",
            "In-memory summary values from the current session.", "Only the current page selection.",
            "Files, folders, catalog entries, or settings.",
            ["Choose Scan folder.", "Complete a scan.", "Return here for totals or open Results."],
            ["Scan folder", "View results", "Settings"], "Before the first scan, the Dashboard explains how to begin.",
            "If Results is unavailable, complete a scan first.", "Dashboard actions never organize files.",
            [HelpTopicId.ScanFolders, HelpTopicId.Results]),
        Topic(HelpTopicId.ScanFolders, "Scan folders", "Selects local roots for read-only analysis and shows scan progress.",
            "Names, paths, metadata, and supported hashes under explicitly selected roots.", "In-memory scan state and progress only.",
            "Selected files or folders.",
            ["Add one or more accessible folders.", "Start scan.", "Review progress or cancel.", "Open Results when complete."],
            ["Browse", "Add folder", "Remove selected", "Start scan", "Cancel scan"], "An empty folder produces a valid empty Results snapshot.",
            "Permission and disappearing-file warnings are isolated and shown safely.", "OpenSorSe does not modify scanned items.",
            [HelpTopicId.Results, HelpTopicId.Diagnostics]),
        Topic(HelpTopicId.Results, "Results", "Searches, filters, sorts, and reviews the immutable current or saved snapshot.",
            "Snapshot metadata, classifications, hashes, planned-operation previews, and local tags.", "In-memory filters and explicitly managed OpenSorSe tags.",
            "File content, names, locations, or timestamps.",
            ["Complete a scan or open a saved snapshot.", "Search or filter.", "Select a row for details.", "Open Duplicate View for grouped exact matches."],
            ["Search", "Filters", "Paging", "Duplicate View", "Tag controls"], "No rows means no snapshot or no rows match current filters.",
            "Clear filters when a known file is not visible.", "All Results actions are review-only in 1.0.",
            [HelpTopicId.DuplicateView, HelpTopicId.AiSuggestions]),
        Topic(HelpTopicId.DuplicateView, "Duplicate View", "Groups files with matching supported SHA-256 hashes for comparison.",
            "Known members and metadata from the current immutable Results snapshot.", "Selection and external open requests only.",
            "Duplicate files; OpenSorSe never deletes, renames, or moves them.",
            ["Open Duplicate View from Results.", "Select a group.", "Review filenames, paths, sizes, and possible space saved.", "Open known files or folders if desired.", "Return to all results."],
            ["Back to all results", "Show group files in results", "Open both files", "Open selected files", "Open containing folder"],
            "No groups means no supported exact hash matches were found.", "Missing files or launch failures are reported per item without breaking the page.",
            "Opening is non-destructive; edits made later in an external application are outside OpenSorSe.", [HelpTopicId.Results]),
        Topic(HelpTopicId.SavedCatalog, "Saved catalog", "Reviews opt-in bounded historical snapshots stored in OpenSorSe application data.",
            "Saved snapshot metadata, source scope, names, and accepted tags.", "Explicit snapshot names or catalog entries.",
            "Scanned files or folders.",
            ["Enable the catalog.", "Complete a scan.", "Select a saved snapshot.", "Open, name, or explicitly remove it."],
            ["Refresh", "Open selected snapshot", "Save snapshot name", "Remove", "Clear all"], "An empty catalog is normal until an enabled scan completes.",
            "Catalog I/O failures preserve current Results.", "Saved snapshots are historical metadata, not live filesystem state.",
            [HelpTopicId.CatalogSearch, HelpTopicId.CompareSnapshots]),
        Topic(HelpTopicId.CatalogSearch, "Catalog Search", "Searches metadata across saved snapshots without rereading disk.",
            "Saved filenames, paths, extensions, categories, and accepted tags.", "In-memory hits and explicit saved-query definitions.",
            "Files, snapshots, catalog entries, or tags.",
            ["Enter a query and Search.", "Select a hit to open its snapshot.", "Optionally name and save the query.", "Run, rename, or remove saved definitions."],
            ["Search", "Clear query", "Open selected saved snapshot", "Save current query", "Run selected", "Rename", "Remove", "Clear all"],
            "One empty state explains no query, no matches, or no saved definitions.", "Enable catalog storage before searching current saved metadata.",
            "Deleting a saved search deletes only its name and query.", [HelpTopicId.SavedCatalog]),
        Topic(HelpTopicId.CompareSnapshots, "Compare snapshots", "Compares two stored metadata snapshots.",
            "Two explicit saved snapshots and their scope metadata.", "In-memory comparison filters and selection.",
            "Saved snapshots or filesystem state.",
            ["Choose baseline and current snapshots.", "Compare.", "Filter changes.", "Open a selected historical snapshot if needed."],
            ["Baseline", "Current", "Compare", "Filters"], "At least two compatible snapshots are needed.",
            "Scope differences are reported rather than hidden.", "Comparison never verifies or changes stored paths.",
            [HelpTopicId.SavedCatalog, HelpTopicId.AdvancedFeatures]),
        Topic(HelpTopicId.SemanticSearch, "Semantic Search (Beta)", "Searches a bounded local index using filename, tag, metadata, native-text, OCR, and deterministic similarity signals.",
            "Only the application-owned local content cache and semantic index.", "Only the rebuildable local semantic index and in-memory search results.",
            "Source files; indexing never renames, moves, deletes, or edits them.",
            ["Enable Semantic Search Beta in Settings.", "Build or refresh the local index.", "Enter a natural-language or exact query.", "Review why each result matched.", "Clear or rebuild the index when needed."],
            ["Build / refresh index", "Rebuild index", "Search", "Clear local index", "Open selected result"],
            "An empty index is normal until content metadata has been indexed.", "Missing files are removed during refresh; corrupt optional indexes recover to an empty rebuild state.",
            "Search quality may evolve and future upgrades may require rebuilding. OCR and extracted text remain local.", [HelpTopicId.Results, HelpTopicId.Settings]),
        Topic(HelpTopicId.StructureHistory, "Structure history", "Reviews folder restructuring proposals, applied outcomes, repeat protection, and historical structures.",
            "Bounded relative paths, sizes, timestamps, fingerprints, and application-owned structure history.", "Preview/history records; only an exact separately confirmed proposal may move listed in-root files.",
            "File contents. Diagrams, comparisons, refresh, filters, and preview do not modify files.",
            ["Enter an absolute disposable root.", "Preview restructuring.", "Review source and proposed structures.", "Use Apply only after reviewing every move.", "Capture current structure or inspect past records."],
            ["Preview restructuring", "Propose restructuring again", "Apply reviewed proposal", "Snapshot selector", "Capture current structure", "History filters"],
            "No history is expected until a proposal is created.", "Changed roots, traversal, conflicts, missing sources, reparse destinations, cancellation, and bounds fail closed.",
            "Only successful applies activate repeat protection. Clearing history never changes files but removes that protection record.", [HelpTopicId.AdvancedFeatures, HelpTopicId.OperationHistory]),
        Topic(HelpTopicId.Rules, "Rules", "Reviews deterministic rule data supplied in memory.",
            "Rule names, conditions, and action previews.", "Only in-memory rule state exposed by the current surface.",
            "Files; the 1.0 desktop does not execute rules.", ["Select a rule.", "Review its state and preview information."],
            ["Current rules", "Enable or disable", "Delete"], "No rules is a valid state.",
            "Unsupported or incomplete rule data is reported safely.", "Rule complexity does not grant file mutation authority.", [HelpTopicId.Results]),
        Topic(HelpTopicId.Settings, "Settings", "Controls persisted OpenSorSe preferences and optional features.",
            "The current OpenSorSe settings file.", "Only values explicitly saved to application data.",
            "Scanned files, folders, or external Ollama configuration.",
            ["Enable metadata extraction if needed.", "For scanned PDFs, install Tesseract plus eng/deu language data, enable OCR, and recheck capability.", "Use connection/model actions only if AI is enabled.", "Save or discard changes.", "Restart only when logging changes say so."],
            ["Enable local OCR", "OCR language", "Check local OCR capability", "Clear cache / reprocess next scan", "Enable AI features", "Show advanced features", "Save", "Discard changes", "Restore defaults"],
            "Hidden settings remain saved and are not reset.", "Invalid values remain unsaved with an actionable error.",
            "OCR and AI remain off by default. Native PDF text is preferred page by page; image-only pages use temporary local rendering and cleanup. Disabled AI prevents provider communication.", [HelpTopicId.AiSetup, HelpTopicId.AdvancedFeatures]),
        Topic(HelpTopicId.Diagnostics, "Diagnostics", "Inspects bounded application events and opt-in AI request diagnostics.",
            "Up to 500 safe session events and, when enabled, 20 redacted session AI requests.", "Filters, selection, clipboard text, or explicit AI diagnostic clearing.",
            "Operation History, scan warnings, user files, or ordinary log files.",
            ["Refresh.", "Filter by severity/category.", "Select and copy safe details.", "Enable advanced AI diagnostics separately when troubleshooting."],
            ["Refresh", "Severity", "Category", "Copy diagnostic details", "Clear AI diagnostics"],
            "No events is a valid early-session state.", "Daily-log write failures do not prevent in-memory diagnostics.",
            "Ordinary details omit raw stacks; AI raw data is opt-in and may contain filenames.", [HelpTopicId.OperationHistory, HelpTopicId.AiSetup]),
        Topic(HelpTopicId.OperationHistory, "Operation history", "Reviews operation records supplied to the application.",
            "In-memory operation/undo session records.", "Selection only.",
            "Files or current Results.", ["Select a supplied session.", "Review its record count and status."],
            ["Operation-history sessions"], "No sessions is expected because 1.0 has no generic rule-execution workflow.",
            "Unavailable records remain isolated.", "This page is not a duplicate of application Diagnostics.", [HelpTopicId.Diagnostics]),
        Topic(HelpTopicId.AiSetup, "AI setup", "Connects optional Ollama-compatible local AI and selects an exact installed model.",
            "The configured endpoint, provider version/model metadata, and selected settings.", "OpenSorSe settings only after Save.",
            "Ollama installation, models, or user files.",
            ["Enable AI features.", "Check connection.", "Discover models.", "Select an installed model.", "Set a 5–300 second timeout.", "Enable only the capabilities you need.", "Save."],
            ["Ollama endpoint", "Check connection", "Discover / refresh models", "Installed model", "Request timeout", "Allow local AI to analyze extracted document text"],
            "No discovered models means the provider reported none; OpenSorSe does not download one.", "Unavailable endpoints, models, cancellation, and timeout are reported separately.",
            "No provider check runs while AI is disabled. A non-local endpoint receives any bounded metadata or extracted text used by an explicit request.", [HelpTopicId.Settings, HelpTopicId.AiSuggestions]),
        Topic(HelpTopicId.AiSuggestions, "AI suggestions", "Generates narrowly scoped rename, logical folder, or document-text interpretation proposals for review.",
            "Exact filenames and bounded metadata; extracted native/OCR text only when its separate switch is enabled and the user explicitly requests interpretation.", "Only in-memory proposals and optional local review decisions.",
            "Source files and folders. AI output never changes them automatically.",
            ["Select eligible Results context.", "Generate one explicit request.", "Observe progress.", "Review the unverified validated proposal.", "Edit a rename if useful.", "Record, reject, dismiss, or cancel."],
            ["Generate", "Interpret extracted text", "Cancel AI request", "Record accept or edit", "Reject", "Dismiss"],
            "No suggestion or no extracted text is a valid outcome.", "Unsafe, malformed, unknown-identity, duplicate, incomplete, or oversized responses are rejected as a whole.",
            "OCR text can be inaccurate. AI output is untrusted and never automatically reaches a file operation.", [HelpTopicId.Results, HelpTopicId.AiSetup]),
        Topic(HelpTopicId.AdvancedFeatures, "Advanced features", "Reveals specialist comparison, diagnostics, history, and troubleshooting controls.",
            "Existing saved settings and advanced page data.", "Only interface visibility after Save.",
            "Hidden values or user files.",
            ["Enable Show advanced features.", "Save.", "Use only the specialist pages needed.", "Disable and save to simplify the interface."],
            ["Show advanced features"], "Hidden pages safely return to Dashboard.",
            "A hidden page cannot be reached through stale navigation.", "Critical safety messages remain visible in regular mode.", [HelpTopicId.Settings, HelpTopicId.Diagnostics]),
        Topic(HelpTopicId.About, "About", "Shows product identity, version, license, and documentation addresses.",
            "Static assembly and repository metadata.", "Nothing.",
            "Files, settings, network state, or clipboard unless the user copies text.",
            ["Review version and license.", "Copy a repository or documentation address if needed."],
            ["Repository address", "Documentation address"], "About always has static content.",
            "Addresses are shown as copyable text; no browser is opened automatically.", "About is read-only.", [HelpTopicId.HelpOverview]),
    ]);

    /// <summary>Gets all topics in stable display order.</summary>
    public static IReadOnlyList<HelpTopic> Topics => TopicsValue;

    /// <summary>Gets a registered topic or the overview fallback.</summary>
    public static HelpTopic Get(HelpTopicId topicId) =>
        TopicsValue.FirstOrDefault(topic => topic.Id == topicId) ?? TopicsValue[0];

    private static HelpTopic Topic(
        HelpTopicId id,
        string title,
        string purpose,
        string reads,
        string changes,
        string doesNotChange,
        IReadOnlyList<string> workflow,
        IReadOnlyList<string> controls,
        string emptyStates,
        string commonErrors,
        string safety,
        IReadOnlyList<HelpTopicId> related) =>
        new(id, title, purpose, reads, changes, doesNotChange, workflow, controls, emptyStates, commonErrors, safety, related);
}
