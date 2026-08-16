using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenSorSe.Application.Semantic;

/// <summary>Signals a bounded, user-correctable local Search query validation failure.</summary>
public sealed class SearchQueryValidationException : ArgumentException
{
    /// <summary>Initializes a query validation failure.</summary>
    public SearchQueryValidationException(string message)
        : base(message)
    {
    }
}

/// <summary>Provides deterministic Unicode folding used by queries, fields, and snippets.</summary>
public static class SearchTextNormalizer
{
    /// <summary>Folds casing, diacritics, punctuation, separators, and repeated whitespace.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var output = new StringBuilder(Math.Min(value.Length, SearchLimits.MaximumQueryCharacters * 2));
        var pendingSpace = false;
        var safeValue = ContainsMalformedUnicode(value)
            ? ReplaceMalformedUnicode(value)
            : value;
        foreach (var character in safeValue.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && output.Length > 0)
                {
                    output.Append(' ');
                }

                output.Append(char.ToLowerInvariant(character));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return output.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Returns whether a string contains an unpaired UTF-16 surrogate.</summary>
    public static bool ContainsMalformedUnicode(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (!char.IsSurrogate(value[index]))
            {
                continue;
            }

            if (!char.IsHighSurrogate(value[index]) ||
                index + 1 >= value.Length ||
                !char.IsLowSurrogate(value[index + 1]))
            {
                return true;
            }

            index++;
        }

        return false;
    }

    private static string ReplaceMalformedUnicode(string value)
    {
        var output = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character) &&
                index + 1 < value.Length &&
                char.IsLowSurrogate(value[index + 1]))
            {
                output.Append(character).Append(value[++index]);
            }
            else if (char.IsSurrogate(character))
            {
                output.Append('\uFFFD');
            }
            else
            {
                output.Append(character);
            }
        }

        return output.ToString();
    }
}

/// <summary>
/// Interprets a conservative grammar for file types, dates, sizes, folders, sources,
/// tags, and indexing state. Uncertain words remain ordinary topic terms.
/// </summary>
public sealed class DeterministicSearchQueryInterpreter : ISearchQueryInterpreter
{
    private const RegexOptions Options =
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant |
        RegexOptions.NonBacktracking;
    // Inputs are capped before interpretation and every expression uses the
    // non-backtracking engine. Keep a bounded timeout without treating brief
    // scheduler starvation during concurrent indexing as an invalid query.
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex ExtensionPattern = Pattern(
        @"\b(?:extension|ext)\s*[:=]?\s*\.?(?<value>[\p{L}\p{N}]{1,16})\b");
    private static readonly Regex TagPattern = Pattern(
        @"\b(?:tagged|tag)\s*[:=]?\s*(?<value>[\p{L}\p{N}_-]{2,64})\b");
    private static readonly Regex SourcePattern = Pattern(
        """\bsource\s*[:=]\s*(?:"(?<value>[\p{L}\p{N}][\p{L}\p{N} _.-]{0,63})"|(?<value>[\p{L}\p{N}][\p{L}\p{N}_.-]{0,63}))""");
    private static readonly Regex FolderPattern = Pattern(
        @"\b(?:in|under)\s+(?:the\s+)?(?<value>[\p{L}\p{N}][\p{L}\p{N} _.-]{0,63}?)\s+folder\b");
    private static readonly Regex SizePattern = Pattern(
        @"\b(?<operator>larger|over|at\s+least|smaller|under|at\s+most)\s+(?:than\s+)?(?<number>\d{1,9}(?:[.,]\d{1,2})?)\s*(?<unit>b|kb|kib|mb|mib|gb|gib)\b");
    private static readonly Regex AbsoluteDatePattern = Pattern(
        @"\b(?<field>created|modified)\s*[:=]\s*(?<date>\d{4}-\d{2}-\d{2})\b");
    private static readonly Regex RelativeDatePattern = Pattern(
        @"\b(?:(?<field>created|modified)\s+)?(?:(?:from|in|during)\s+)?(?<range>last\s+year|this\s+year|last\s+month|this\s+month)\b");
    private static readonly Regex YearPattern = Pattern(
        @"\b(?:(?<field>created|modified)\s+)?(?:from|in|during)\s+(?<year>19\d{2}|20\d{2}|21\d{2})\b");
    private static readonly IReadOnlyDictionary<string, string> FileTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["pdf"] = "pdf",
            ["pdfs"] = "pdf",
            ["document"] = "document",
            ["documents"] = "document",
            ["spreadsheet"] = "spreadsheet",
            ["spreadsheets"] = "spreadsheet",
            ["presentation"] = "presentation",
            ["presentations"] = "presentation",
            ["photo"] = "image",
            ["photos"] = "image",
            ["image"] = "image",
            ["images"] = "image",
            ["video"] = "video",
            ["videos"] = "video",
            ["audio"] = "audio",
            ["archive"] = "archive",
            ["archives"] = "archive",
        };
    private readonly Regex _monthPattern;
    private readonly CultureInfo _culture;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the local interpreter with injectable time and locale.</summary>
    public DeterministicSearchQueryInterpreter(
        TimeProvider? timeProvider = null,
        CultureInfo? culture = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _culture = culture ?? CultureInfo.CurrentCulture;
        var monthNames = _culture.DateTimeFormat.MonthNames
            .Concat(_culture.DateTimeFormat.AbbreviatedMonthNames)
            .Concat(CultureInfo.InvariantCulture.DateTimeFormat.MonthNames)
            .Concat(CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Create(_culture, ignoreCase: true))
            .OrderByDescending(value => value.Length)
            .Select(Regex.Escape);
        _monthPattern = Pattern(
            $@"\b(?:(?<field>created|modified)\s+)?(?:from|in|im|during)\s+(?<month>{string.Join("|", monthNames)})(?:\s+(?<year>19\d{{2}}|20\d{{2}}|21\d{{2}}))?\b");
    }

    /// <inheritdoc />
    public SearchInterpretation Interpret(SearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = request.QueryText?.Trim() ?? string.Empty;
        ValidateQuery(query, request.ActiveFilters?.Count > 0);
        if (!request.InterpretFilters)
        {
            var explicitFilters = ValidateExplicitFilters(request.ActiveFilters ?? []);
            var topic = (request.TopicTextOverride ?? query).Trim();
            return CreateInterpretation(query, topic, explicitFilters);
        }

        var filters = new List<SearchFilter>();
        var consumed = new List<TextSpan>();
        AddSingleValueMatches(query, ExtensionPattern, SearchFilterKind.Extension, "Extension", filters, consumed);
        AddSingleValueMatches(query, TagPattern, SearchFilterKind.Tag, "Tag", filters, consumed);
        AddSingleValueMatches(query, SourcePattern, SearchFilterKind.Source, "Source", filters, consumed);
        AddSingleValueMatches(query, FolderPattern, SearchFilterKind.Folder, "Folder", filters, consumed);
        AddFileTypes(query, filters, consumed);
        AddSizes(query, filters, consumed);
        AddAbsoluteDates(query, filters, consumed);
        AddRelativeDates(query, filters, consumed);
        AddMonths(query, filters, consumed);
        AddYears(query, filters, consumed);
        AddStateFilters(query, filters, consumed);

        var topicText = RemoveConsumed(query, consumed);
        return CreateInterpretation(
            query,
            topicText,
            Array.AsReadOnly(filters.Take(SearchLimits.MaximumFilters).ToArray()));
    }

    private static Regex Pattern(string expression) => new(expression, Options, RegexTimeout);

    private static void ValidateQuery(string query, bool hasExplicitFilters)
    {
        if ((!hasExplicitFilters && query.Length == 0) ||
            query.Length > SearchLimits.MaximumQueryCharacters)
        {
            throw new SearchQueryValidationException(
                $"Enter a Search query of up to {SearchLimits.MaximumQueryCharacters} characters.");
        }

        if (query.Any(char.IsControl) || SearchTextNormalizer.ContainsMalformedUnicode(query))
        {
            throw new SearchQueryValidationException(
                "The Search query contains unsupported control or malformed text.");
        }
    }

    private static IReadOnlyList<SearchFilter> ValidateExplicitFilters(IReadOnlyList<SearchFilter> filters)
    {
        if (filters.Count > SearchLimits.MaximumFilters ||
            filters.Any(filter =>
                filter is null ||
                !Enum.IsDefined(filter.Kind) ||
                string.IsNullOrWhiteSpace(filter.Id) ||
                filter.Id.Length > 160 ||
                string.IsNullOrWhiteSpace(filter.Value) ||
                filter.Value.Length > 256 ||
                string.IsNullOrWhiteSpace(filter.DisplayName) ||
                filter.DisplayName.Length > 256))
        {
            throw new SearchQueryValidationException("One or more active Search filters are invalid.");
        }

        return Array.AsReadOnly(filters.DistinctBy(filter => filter.Id, StringComparer.Ordinal).ToArray());
    }

    private static SearchInterpretation CreateInterpretation(
        string original,
        string topic,
        IReadOnlyList<SearchFilter> filters)
    {
        var normalizedTopic = string.Join(' ', topic.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var normalizedTokens = SearchTextNormalizer.Normalize(normalizedTopic)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (normalizedTokens.Length > SearchLimits.MaximumQueryTokens)
        {
            throw new SearchQueryValidationException(
                $"Use no more than {SearchLimits.MaximumQueryTokens} topic terms in one Search.");
        }

        var tokens = SemanticTokenizer.Tokenize(normalizedTopic, SearchLimits.MaximumQueryTokens)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (tokens.Length == 0 && filters.Count == 0)
        {
            throw new SearchQueryValidationException(
                "Enter a filename, topic, or supported Search filter.");
        }

        return new SearchInterpretation(
            original,
            normalizedTopic,
            Array.AsReadOnly(tokens),
            filters);
    }

    private static void AddSingleValueMatches(
        string query,
        Regex regex,
        SearchFilterKind kind,
        string label,
        ICollection<SearchFilter> filters,
        ICollection<TextSpan> consumed)
    {
        foreach (Match match in regex.Matches(query))
        {
            var value = BoundValue(match.Groups["value"].Value);
            AddFilter(filters, kind, value, $"{label}: {value}");
            consumed.Add(new TextSpan(match.Index, match.Length));
        }
    }

    private static void AddFileTypes(
        string query,
        ICollection<SearchFilter> filters,
        ICollection<TextSpan> consumed)
    {
        foreach (var pair in FileTypes)
        {
            var regex = Pattern($@"\b{Regex.Escape(pair.Key)}\b");
            foreach (Match match in regex.Matches(query))
            {
                if (match.Index > 0 && query[match.Index - 1] == '.')
                {
                    continue;
                }

                if (consumed.Any(span => Overlaps(span, match.Index, match.Length)))
                {
                    continue;
                }

                AddFilter(filters, SearchFilterKind.FileType, pair.Value, $"File type: {pair.Value}");
                consumed.Add(new TextSpan(match.Index, match.Length));
            }
        }
    }

    private void AddSizes(
        string query,
        ICollection<SearchFilter> filters,
        ICollection<TextSpan> consumed)
    {
        foreach (Match match in SizePattern.Matches(query))
        {
            var rawNumber = match.Groups["number"].Value.Replace(',', '.');
            if (!double.TryParse(rawNumber, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ||
                number < 0)
            {
                continue;
            }

            var multiplier = match.Groups["unit"].Value.ToLowerInvariant() switch
            {
                "b" => 1d,
                "kb" => 1000d,
                "kib" => 1024d,
                "mb" => 1_000_000d,
                "mib" => 1024d * 1024,
                "gb" => 1_000_000_000d,
                "gib" => 1024d * 1024 * 1024,
                _ => 0d,
            };
            if (multiplier <= 0 || number > long.MaxValue / multiplier)
            {
                continue;
            }

            var bytes = checked((long)Math.Round(number * multiplier, MidpointRounding.AwayFromZero));
            var minimum = match.Groups["operator"].Value.StartsWith("larger", StringComparison.OrdinalIgnoreCase) ||
                match.Groups["operator"].Value.StartsWith("over", StringComparison.OrdinalIgnoreCase) ||
                match.Groups["operator"].Value.StartsWith("at least", StringComparison.OrdinalIgnoreCase);
            AddFilter(
                filters,
                minimum ? SearchFilterKind.MinimumSizeBytes : SearchFilterKind.MaximumSizeBytes,
                bytes.ToString(CultureInfo.InvariantCulture),
                $"{(minimum ? "Minimum" : "Maximum")} size: {match.Groups["number"].Value} {match.Groups["unit"].Value}");
            consumed.Add(new TextSpan(match.Index, match.Length));
        }
    }

    private void AddAbsoluteDates(
        string query,
        ICollection<SearchFilter> filters,
        ICollection<TextSpan> consumed)
    {
        foreach (Match match in AbsoluteDatePattern.Matches(query))
        {
            if (!DateOnly.TryParseExact(
                    match.Groups["date"].Value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
            {
                continue;
            }

            var created = match.Groups["field"].Value.Equals("created", StringComparison.OrdinalIgnoreCase);
            AddDateRange(filters, created, date, date.AddDays(1), match.Groups["date"].Value);
            consumed.Add(new TextSpan(match.Index, match.Length));
        }
    }

    private void AddRelativeDates(
        string query,
        ICollection<SearchFilter> filters,
        ICollection<TextSpan> consumed)
    {
        foreach (Match match in RelativeDatePattern.Matches(query))
        {
            var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().Date);
            var range = SearchTextNormalizer.Normalize(match.Groups["range"].Value);
            DateOnly start;
            DateOnly end;
            switch (range)
            {
                case "last year":
                    start = new DateOnly(today.Year - 1, 1, 1);
                    end = new DateOnly(today.Year, 1, 1);
                    break;
                case "this year":
                    start = new DateOnly(today.Year, 1, 1);
                    end = start.AddYears(1);
                    break;
                case "last month":
                    end = new DateOnly(today.Year, today.Month, 1);
                    start = end.AddMonths(-1);
                    break;
                case "this month":
                    start = new DateOnly(today.Year, today.Month, 1);
                    end = start.AddMonths(1);
                    break;
                default:
                    continue;
            }

            var created = match.Groups["field"].Success &&
                match.Groups["field"].Value.Equals("created", StringComparison.OrdinalIgnoreCase);
            AddDateRange(filters, created, start, end, match.Groups["range"].Value);
            consumed.Add(new TextSpan(match.Index, match.Length));
        }
    }

    private void AddYears(
        string query,
        ICollection<SearchFilter> filters,
        ICollection<TextSpan> consumed)
    {
        foreach (Match match in YearPattern.Matches(query))
        {
            if (consumed.Any(span => Overlaps(span, match.Index, match.Length)))
            {
                continue;
            }

            if (!int.TryParse(match.Groups["year"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year))
            {
                continue;
            }

            var created = match.Groups["field"].Success &&
                match.Groups["field"].Value.Equals("created", StringComparison.OrdinalIgnoreCase);
            AddDateRange(
                filters,
                created,
                new DateOnly(year, 1, 1),
                new DateOnly(year + 1, 1, 1),
                year.ToString(CultureInfo.InvariantCulture));
            consumed.Add(new TextSpan(match.Index, match.Length));
        }
    }

    private void AddMonths(
        string query,
        ICollection<SearchFilter> filters,
        ICollection<TextSpan> consumed)
    {
        foreach (Match match in _monthPattern.Matches(query))
        {
            var monthText = match.Groups["month"].Value;
            if (!DateTime.TryParseExact(
                    monthText,
                    ["MMMM", "MMM"],
                    _culture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var parsedMonth) &&
                !DateTime.TryParseExact(
                    monthText,
                    ["MMMM", "MMM"],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out parsedMonth))
            {
                continue;
            }

            var year = match.Groups["year"].Success &&
                int.TryParse(
                    match.Groups["year"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var explicitYear)
                    ? explicitYear
                    : _timeProvider.GetLocalNow().Year;
            var start = new DateOnly(year, parsedMonth.Month, 1);
            var created = match.Groups["field"].Success &&
                match.Groups["field"].Value.Equals("created", StringComparison.OrdinalIgnoreCase);
            AddDateRange(filters, created, start, start.AddMonths(1), $"{monthText} {year}");
            consumed.Add(new TextSpan(match.Index, match.Length));
        }
    }

    private void AddDateRange(
        ICollection<SearchFilter> filters,
        bool created,
        DateOnly start,
        DateOnly end,
        string display)
    {
        var startUtc = ToUtc(start);
        var endUtc = ToUtc(end);
        AddFilter(
            filters,
            created ? SearchFilterKind.CreatedOnOrAfter : SearchFilterKind.ModifiedOnOrAfter,
            startUtc.ToString("O", CultureInfo.InvariantCulture),
            $"{(created ? "Created" : "Modified")}: {display}");
        AddFilter(
            filters,
            created ? SearchFilterKind.CreatedBefore : SearchFilterKind.ModifiedBefore,
            endUtc.ToString("O", CultureInfo.InvariantCulture),
            $"{(created ? "Created" : "Modified")} before {end:yyyy-MM-dd}");
    }

    private DateTimeOffset ToUtc(DateOnly value)
    {
        var local = value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var offset = _timeProvider.LocalTimeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    private static void AddStateFilters(
        string query,
        ICollection<SearchFilter> filters,
        ICollection<TextSpan> consumed)
    {
        AddPhrase(query, "fully indexed", SearchFilterKind.IndexingCompletion, "full", "Indexing: fully indexed", filters, consumed);
        AddPhrase(query, "partially indexed", SearchFilterKind.IndexingCompletion, "partial", "Indexing: partial", filters, consumed);
        AddPhrase(query, "metadata only", SearchFilterKind.IndexingLevel, "basic", "Indexing level: Basic", filters, consumed);
        AddPhrase(query, "ocr available", SearchFilterKind.OcrAvailability, "true", "OCR text: available", filters, consumed);
        AddPhrase(query, "without ocr", SearchFilterKind.OcrAvailability, "false", "OCR text: unavailable", filters, consumed);
        AddPhrase(query, "semantic available", SearchFilterKind.SemanticAvailability, "true", "Related-concept data: available", filters, consumed);
        AddPhrase(query, "failed indexing", SearchFilterKind.FailureState, "true", "Indexing failures: present", filters, consumed);
    }

    private static void AddPhrase(
        string query,
        string phrase,
        SearchFilterKind kind,
        string value,
        string display,
        ICollection<SearchFilter> filters,
        ICollection<TextSpan> consumed)
    {
        var regex = Pattern($@"\b{Regex.Escape(phrase).Replace(@"\ ", @"\s+")}\b");
        foreach (Match match in regex.Matches(query))
        {
            AddFilter(filters, kind, value, display);
            consumed.Add(new TextSpan(match.Index, match.Length));
        }
    }

    private static void AddFilter(
        ICollection<SearchFilter> filters,
        SearchFilterKind kind,
        string value,
        string display)
    {
        if (filters.Count >= SearchLimits.MaximumFilters)
        {
            return;
        }

        var normalized = BoundValue(value);
        var id = $"{kind}:{normalized}";
        if (filters.Any(filter => string.Equals(filter.Id, id, StringComparison.Ordinal)))
        {
            return;
        }

        filters.Add(new SearchFilter(id, kind, normalized, display.Trim()));
    }

    private static string BoundValue(string value) =>
        value.Trim().Length <= 256 ? value.Trim() : value.Trim()[..256];

    private static string RemoveConsumed(string query, IReadOnlyCollection<TextSpan> spans)
    {
        if (spans.Count == 0)
        {
            return query;
        }

        var removed = new bool[query.Length];
        foreach (var span in spans)
        {
            var end = Math.Min(query.Length, span.Start + span.Length);
            for (var index = Math.Max(0, span.Start); index < end; index++)
            {
                removed[index] = true;
            }
        }

        var builder = new StringBuilder(query.Length);
        for (var index = 0; index < query.Length; index++)
        {
            builder.Append(removed[index] ? ' ' : query[index]);
        }

        return string.Join(' ', builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool Overlaps(TextSpan span, int start, int length) =>
        start < span.Start + span.Length && span.Start < start + length;

    private readonly record struct TextSpan(int Start, int Length);
}
