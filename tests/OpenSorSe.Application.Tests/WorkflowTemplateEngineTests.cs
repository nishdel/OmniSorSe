#pragma warning disable CS1591

using OpenSorSe.Application.Workflows;

namespace OpenSorSe.Application.Tests;

public sealed class WorkflowTemplateEngineTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "OpenSorSe.Template.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly WorkflowTemplateEngine _engine = new();

    public WorkflowTemplateEngineTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_FormatsDatesUsesFallbacksAndPreservesExtension()
    {
        var recipe = Recipe() with
        {
            NamingTemplate = "{date:yyyy-MM-dd}_{vendor}_{amount}",
            DestinationTemplate = "Invoices/{date:yyyy}/{vendor}",
            RequiredFields = ["date", "vendor"],
            OptionalFields = ["amount"],
            FallbackValues = new Dictionary<string, string> { ["amount"] = "Unknown" },
        };
        var result = Evaluate(
            recipe,
            ("date", "2026-07-26T10:00:00Z"),
            ("vendor", "Example Ltd"));

        Assert.True(result.IsValid);
        Assert.Equal("2026-07-26_Example Ltd_Unknown.pdf", result.ProposedFileName);
        Assert.EndsWith(
            Path.Combine("Invoices", "2026", "Example Ltd", result.ProposedFileName!),
            result.ProposedDestinationPath);
        Assert.Contains("amount", result.FallbackValues, StringComparer.OrdinalIgnoreCase);
        Assert.True(ChangePlanRootContains(_root, result.ProposedDestinationPath!));
    }

    [Fact]
    public void Evaluate_MissingRequiredValue_BlocksProposal()
    {
        var recipe = Recipe() with
        {
            NamingTemplate = "{vendor}",
            DestinationTemplate = "Invoices/{vendor}",
            RequiredFields = ["vendor"],
            FallbackValues = new Dictionary<string, string>(),
            Normalization = Recipe().Normalization with
            {
                MissingValuePolicy = WorkflowMissingValuePolicy.SkipItem,
            },
        };

        var result = Evaluate(recipe);

        Assert.False(result.IsValid);
        Assert.Contains("vendor", result.MissingValues, StringComparer.OrdinalIgnoreCase);
        Assert.Null(result.ProposedDestinationPath);
    }

    [Fact]
    public void Evaluate_ExplicitFallbackForRequiredValueProducesReviewableProposal()
    {
        var recipe = Recipe() with
        {
            NamingTemplate = "{documentType}_{originalName}",
            RequiredFields = ["documentType"],
            FallbackValues = new Dictionary<string, string> { ["documentType"] = "Unclassified" },
            Normalization = Recipe().Normalization with
            {
                MissingValuePolicy = WorkflowMissingValuePolicy.UseFallback,
            },
        };

        var result = Evaluate(recipe);

        Assert.True(result.IsValid);
        Assert.Equal("Unclassified_sample.pdf", result.ProposedFileName);
        Assert.Contains("documentType", result.FallbackValues, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../Outside", "travers")]
    [InlineData("C:\\Outside", "relative")]
    [InlineData("/Outside", "relative")]
    public void ValidateRecipeTemplates_RejectsTraversalAndRootedDestinations(
        string destination,
        string expected)
    {
        var recipe = Recipe() with { DestinationTemplate = destination };

        var validation = _engine.ValidateRecipeTemplates(recipe);

        Assert.False(validation.IsValid);
        Assert.Contains(
            validation.Issues,
            issue => issue.Message.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_SanitizesInjectedSeparatorsAndReportsChanges()
    {
        var recipe = Recipe() with
        {
            NamingTemplate = "{vendor}",
            DestinationTemplate = "Vendors/{vendor}",
            RequiredFields = ["vendor"],
        };

        var result = Evaluate(recipe, ("vendor", "..\\Outside/ACME:*?"));

        Assert.True(result.IsValid);
        Assert.True(ChangePlanRootContains(_root, result.ProposedDestinationPath!));
        Assert.DoesNotContain(
            Path.GetRelativePath(_root, result.ProposedDestinationPath!)
                .Split(Path.DirectorySeparatorChar),
            segment => segment is "." or "..");
        Assert.NotEmpty(result.SanitizationChanges);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("NUL.txt")]
    [InlineData("LPT9")]
    public void Evaluate_ReservedWindowsNames_AreBlockedOnEveryPlatform(string name)
    {
        var result = Evaluate(Recipe() with { NamingTemplate = name });

        Assert.False(result.IsValid);
        Assert.Contains(result.Conflicts, conflict =>
            conflict.Contains("reserved", StringComparison.OrdinalIgnoreCase) ||
            conflict.Contains("unsafe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_LengthAndCollisionConstraints_BlockWithoutOverwrite()
    {
        var longRecipe = Recipe() with
        {
            NamingTemplate = new string('a', 50),
            MaximumFileNameLength = 10,
        };
        var tooLong = Evaluate(longRecipe);
        Assert.False(tooLong.IsValid);
        Assert.Contains(tooLong.Conflicts, conflict =>
            conflict.Contains("exceeds", StringComparison.OrdinalIgnoreCase));

        var first = Evaluate(Recipe());
        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            first.ProposedDestinationPath!,
        };
        var collision = _engine.Evaluate(
            Recipe(),
            Context(new Dictionary<string, RecipeFieldValue>(), occupied));
        Assert.False(collision.IsValid);
        Assert.Contains(collision.Conflicts, conflict =>
            conflict.Contains("occupied", StringComparison.OrdinalIgnoreCase));

        var reviewCollision = _engine.Evaluate(
            Recipe() with { CollisionPolicy = WorkflowCollisionPolicy.RequireReview },
            Context(new Dictionary<string, RecipeFieldValue>(), occupied));
        Assert.True(reviewCollision.IsValid);
        Assert.Contains(reviewCollision.Warnings, warning =>
            warning.Contains("preflight", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_IsDeterministicAndNormalizesUnicodeAndCase()
    {
        var recipe = Recipe() with
        {
            NamingTemplate = "{vendor}",
            RequiredFields = ["vendor"],
            Normalization = Recipe().Normalization with
            {
                CasePolicy = WorkflowCasePolicy.Upper,
                NormalizeUnicode = true,
            },
        };
        var decomposed = "Cafe\u0301";

        var first = Evaluate(recipe, ("vendor", decomposed));
        var second = Evaluate(recipe, ("vendor", decomposed));

        Assert.Equal(first.IsValid, second.IsValid);
        Assert.Equal(first.ProposedFileName, second.ProposedFileName);
        Assert.Equal(first.ProposedDestinationPath, second.ProposedDestinationPath);
        Assert.Equal(first.ValuesUsed, second.ValuesUsed);
        Assert.Equal(first.MissingValues, second.MissingValues);
        Assert.Equal(first.FallbackValues, second.FallbackValues);
        Assert.Equal(first.SanitizationChanges, second.SanitizationChanges);
        Assert.Equal(first.Conflicts, second.Conflicts);
        Assert.Equal(first.Warnings, second.Warnings);
        Assert.Equal("CAFÉ.pdf", first.ProposedFileName);
    }

    [Fact]
    public void Evaluate_AiValueIsDataNotExecutableTemplateSyntax()
    {
        var recipe = Recipe() with
        {
            NamingTemplate = "{vendor}",
            DestinationTemplate = "Vendors/{vendor}",
            RequiredFields = ["vendor"],
        };
        var values = new Dictionary<string, RecipeFieldValue>
        {
            ["vendor"] = new("{date:yyyy}/../../escape", "approved AI metadata", true),
        };

        var result = _engine.Evaluate(recipe, Context(values));

        Assert.True(result.IsValid);
        Assert.True(result.RequiresAiDerivedValues);
        Assert.True(ChangePlanRootContains(_root, result.ProposedDestinationPath!));
        Assert.Contains("{date", result.ProposedFileName, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_EmptyNamingPatternPreservesOriginalNameAndExtension()
    {
        var result = Evaluate(Recipe() with
        {
            NamingTemplate = string.Empty,
            DestinationTemplate = "Organized",
        });

        Assert.True(result.IsValid);
        Assert.Equal("sample.pdf", result.ProposedFileName);
        Assert.EndsWith(Path.Combine("Organized", "sample.pdf"), result.ProposedDestinationPath);
    }

    [Fact]
    public void Evaluate_EmptyDestinationPatternPreservesCurrentFolder()
    {
        var result = Evaluate(Recipe() with
        {
            NamingTemplate = "{originalName}_reviewed",
            DestinationTemplate = string.Empty,
        });

        Assert.True(result.IsValid);
        Assert.Equal(Path.Combine(_root, "sample_reviewed.pdf"), result.ProposedDestinationPath);
    }

    [Fact]
    public void ValidateRecipeTemplates_RejectsRecipeWithNoNamingOrDestinationChange()
    {
        var validation = _engine.ValidateRecipeTemplates(Recipe() with
        {
            NamingTemplate = string.Empty,
            DestinationTemplate = string.Empty,
        });

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, issue => issue.Code == "template.no-operation");
    }

    [Fact]
    public void Evaluate_ExplicitFilesystemDateFormatsAreSupported()
    {
        var result = Evaluate(
            Recipe() with
            {
                NamingTemplate = "{filesystemCreatedDate:yyyy-MM-dd}_{originalName}",
                RequiredFields = ["filesystemCreatedDate", "originalName"],
            },
            ("filesystemCreatedDate", "2026-05-03T10:00:00Z"));

        Assert.True(result.IsValid);
        Assert.Equal("2026-05-03_sample.pdf", result.ProposedFileName);
    }

    [Fact]
    public void Evaluate_PreservesOriginalExtensionCasingExactly()
    {
        var original = Path.Combine(_root, "sample.PdF");
        var context = new RecipeEvaluationContext(
            _root,
            original,
            new Dictionary<string, RecipeFieldValue>());

        var result = _engine.Evaluate(Recipe() with { NamingTemplate = "review.PDF" }, context);

        Assert.True(result.IsValid);
        Assert.Equal("review.PdF", result.ProposedFileName);
    }

    private RecipeEvaluationResult Evaluate(
        SortingRecipe recipe,
        params (string Key, string Value)[] values) =>
        _engine.Evaluate(
            recipe,
            Context(values.ToDictionary(
                pair => pair.Key,
                pair => new RecipeFieldValue(pair.Value, "test metadata"),
                StringComparer.OrdinalIgnoreCase)));

    private RecipeEvaluationContext Context(
        IReadOnlyDictionary<string, RecipeFieldValue> values,
        IReadOnlySet<string>? occupied = null) =>
        new(
            _root,
            Path.Combine(_root, "sample.pdf"),
            values,
            occupied);

    private static SortingRecipe Recipe() =>
        BuiltInWorkflowLibrary.Recipes[0] with
        {
            NamingTemplate = "{originalName}",
            DestinationTemplate = "Organized",
            RequiredFields = ["originalName"],
            OptionalFields = [],
            FallbackValues = new Dictionary<string, string>(),
            PreserveExtension = true,
        };

    private static bool ChangePlanRootContains(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(path);
        return string.Equals(normalizedRoot, normalizedPath, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(
                   normalizedRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }
}
