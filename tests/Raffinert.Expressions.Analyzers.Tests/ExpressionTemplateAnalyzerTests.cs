using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Raffinert.Expressions.Analyzers.Tests;

public class ExpressionTemplateAnalyzerTests
{
    [Fact]
    public async Task ReportsUnsupportedShape()
    {
        const string source = """
            using Raffinert.Expressions;
            class Product { public decimal Price { get; set; } }
            class C
            {
                void M()
                {
                    var template = ExpressionTemplate<Product>.Create(
                        p => new { Calculated = p.Price + 1m },
                        x => x.Calculated > 10m);
                }
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "REX001");
    }

    [Fact]
    public async Task ReportsMissingTargetMember()
    {
        const string source = """
            using Raffinert.Expressions;
            class Product { public string Name { get; set; } = ""; public decimal Price { get; set; } }
            class Target { public string Name { get; set; } = ""; }
            class C
            {
                void M()
                {
                    var template = ExpressionTemplate<Product>.Create(
                        p => new { p.Name, p.Price },
                        x => x.Price > 10m);
                    var adapted = template.AdaptSpec<Target>();
                }
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "REX002" && diagnostic.GetMessage().Contains("Price"));
    }

    [Fact]
    public async Task ReportsIncompatibleTargetMember()
    {
        const string source = """
            using Raffinert.Expressions;
            class Product { public decimal Price { get; set; } }
            class Target { public string Price { get; set; } = ""; }
            class C
            {
                void M()
                {
                    var template = SpecTemplate<Product>.Create(p => new { p.Price }, x => x.Price > 10m);
                    var adapted = template.Adapt<Target>();
                }
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "REX003");
    }

    [Fact]
    public async Task ValidPropertyAndFieldTargetsProduceNoDiagnostics()
    {
        const string source = """
            using Raffinert.Expressions;
            class Product { public string Name { get; set; } = ""; public decimal Price { get; set; } }
            class PropertyTarget { public string Name { get; set; } = ""; public decimal Price { get; set; } }
            class FieldTarget { public string Name = ""; public decimal Price; }
            class C
            {
                void M()
                {
                    var template = ExpressionTemplate<Product>.Create(
                        p => new { p.Name, p.Price },
                        x => x.Price > 10m && x.Name != null);
                    _ = template.AdaptSpec<PropertyTarget>();
                    _ = template.AdaptSpec<FieldTarget>();
                }
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.Empty(diagnostics);
    }

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(Spec<>).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "AnalyzerTests",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new ExpressionTemplateAnalyzer();
        return await compilation
            .WithAnalyzers([analyzer])
            .GetAnalyzerDiagnosticsAsync();
    }
}
