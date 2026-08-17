using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Raffinert.Expressions.Analyzers;

/// <summary>Validates structural expression-template creation and adaptation.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExpressionTemplateAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Diagnostic emitted for an unsupported template-selector shape.</summary>
    public static readonly DiagnosticDescriptor UnsupportedShape = new(
        "REX001",
        "Unsupported expression-template shape",
        "Expression-template shape must select direct sample members with an anonymous object or member initializer",
        "Raffinert.Expressions",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>Diagnostic emitted when an adaptation target lacks a required member.</summary>
    public static readonly DiagnosticDescriptor MissingMember = new(
        "REX002",
        "Target type is missing a required member",
        "Target type '{0}' is missing readable public instance member '{1}' required by the expression template",
        "Raffinert.Expressions",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>Diagnostic emitted when an adaptation target member has the wrong type.</summary>
    public static readonly DiagnosticDescriptor IncompatibleMember = new(
        "REX003",
        "Target member has incompatible type",
        "Target member '{0}.{1}' has type '{2}', but the expression template requires '{3}'",
        "Raffinert.Expressions",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [UnsupportedShape, MissingMember, IncompatibleMember];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method ||
            method.ContainingNamespace.ToDisplayString() != "Raffinert.Expressions")
        {
            return;
        }

        if (method.Name == "Create" && IsTemplateType(method.ContainingType))
        {
            AnalyzeCreate(context, invocation);
            return;
        }

        if ((method.Name == "Adapt" || method.Name == "AdaptSpec") && method.TypeArguments.Length == 1)
        {
            AnalyzeAdapt(context, invocation, method.TypeArguments[0]);
        }
    }

    private static void AnalyzeCreate(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList.Arguments.Count == 0 ||
            !TryGetLambda(invocation.ArgumentList.Arguments[0].Expression, out var lambda) ||
            !TryReadRequirements(context.SemanticModel, lambda, out _))
        {
            context.ReportDiagnostic(Diagnostic.Create(UnsupportedShape, invocation.GetLocation()));
        }
    }

    private static void AnalyzeAdapt(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        ITypeSymbol targetType)
    {
        var create = FindCreateInvocation(context.SemanticModel, invocation, context.CancellationToken);
        if (create == null ||
            create.ArgumentList.Arguments.Count == 0 ||
            !TryGetLambda(create.ArgumentList.Arguments[0].Expression, out var lambda) ||
            !TryReadRequirements(context.SemanticModel, lambda, out var requirements))
        {
            return;
        }

        foreach (var requirement in requirements)
        {
            var candidates = targetType.GetMembers(requirement.Name)
                .Where(IsReadablePublicInstanceMember)
                .ToArray();
            if (candidates.Length != 1)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    MissingMember,
                    invocation.GetLocation(),
                    targetType.ToDisplayString(),
                    requirement.Name));
                continue;
            }

            var memberType = candidates[0] switch
            {
                IPropertySymbol property => property.Type,
                IFieldSymbol field => field.Type,
                _ => null
            };
            if (memberType == null) continue;

            var conversion = context.Compilation.ClassifyConversion(memberType, requirement.Type);
            if (!SymbolEqualityComparer.Default.Equals(memberType, requirement.Type) && !conversion.IsImplicit)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    IncompatibleMember,
                    invocation.GetLocation(),
                    targetType.ToDisplayString(),
                    requirement.Name,
                    memberType.ToDisplayString(),
                    requirement.Type.ToDisplayString()));
            }
        }
    }

    private static InvocationExpressionSyntax? FindCreateInvocation(
        SemanticModel semanticModel,
        InvocationExpressionSyntax adaptInvocation,
        CancellationToken cancellationToken)
    {
        if (adaptInvocation.Expression is not MemberAccessExpressionSyntax memberAccess) return null;
        if (memberAccess.Expression is InvocationExpressionSyntax direct && IsCreate(direct, semanticModel, cancellationToken))
        {
            return direct;
        }

        var symbol = semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol;
        var declaration = symbol?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
        return declaration switch
        {
            VariableDeclaratorSyntax { Initializer.Value: InvocationExpressionSyntax initializer }
                when IsCreate(initializer, semanticModel, cancellationToken) => initializer,
            PropertyDeclarationSyntax { Initializer.Value: InvocationExpressionSyntax initializer }
                when IsCreate(initializer, semanticModel, cancellationToken) => initializer,
            _ => null
        };
    }

    private static bool IsCreate(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method &&
               method.Name == "Create" &&
               method.ContainingNamespace.ToDisplayString() == "Raffinert.Expressions" &&
               IsTemplateType(method.ContainingType);
    }

    private static bool TryReadRequirements(
        SemanticModel semanticModel,
        LambdaExpressionSyntax lambda,
        out ImmutableArray<Requirement> requirements)
    {
        var builder = ImmutableArray.CreateBuilder<Requirement>();

        if (lambda.Body is AnonymousObjectCreationExpressionSyntax anonymous)
        {
            foreach (var initializer in anonymous.Initializers)
            {
                var name = initializer.NameEquals?.Name.Identifier.ValueText ?? GetMemberName(initializer.Expression);
                if (name == null || GetMemberName(initializer.Expression) != name ||
                    semanticModel.GetTypeInfo(initializer.Expression).Type is not { } type)
                {
                    requirements = default;
                    return false;
                }

                builder.Add(new Requirement(name, type));
            }
        }
        else if (lambda.Body is ObjectCreationExpressionSyntax { Initializer: { } objectInitializer })
        {
            foreach (var expression in objectInitializer.Expressions)
            {
                if (expression is not AssignmentExpressionSyntax assignment ||
                    GetAssignedName(assignment.Left) is not { } assignedName ||
                    GetMemberName(assignment.Right) != assignedName ||
                    semanticModel.GetTypeInfo(assignment.Right).Type is not { } type)
                {
                    requirements = default;
                    return false;
                }

                builder.Add(new Requirement(assignedName, type));
            }
        }
        else
        {
            requirements = default;
            return false;
        }

        requirements = builder.ToImmutable();
        return requirements.Length != 0;
    }

    private static string? GetMemberName(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized) expression = parenthesized.Expression;
        return expression is MemberAccessExpressionSyntax memberAccess &&
               memberAccess.Expression is IdentifierNameSyntax
            ? memberAccess.Name.Identifier.ValueText
            : null;
    }

    private static string? GetAssignedName(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        _ => null
    };

    private static bool TryGetLambda(ExpressionSyntax expression, out LambdaExpressionSyntax lambda)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized) expression = parenthesized.Expression;
        lambda = expression as LambdaExpressionSyntax ?? null!;
        return lambda != null;
    }

    private static bool IsReadablePublicInstanceMember(ISymbol symbol) => symbol switch
    {
        IPropertySymbol property => !property.IsStatic &&
                                    property.DeclaredAccessibility == Accessibility.Public &&
                                    property.GetMethod?.DeclaredAccessibility == Accessibility.Public,
        IFieldSymbol field => !field.IsStatic && field.DeclaredAccessibility == Accessibility.Public,
        _ => false
    };

    private static bool IsTemplateType(INamedTypeSymbol type) =>
        type.Name == "ExpressionTemplate" || type.Name == "SpecTemplate";

    private readonly struct Requirement(string name, ITypeSymbol type)
    {
        public string Name { get; } = name;

        public ITypeSymbol Type { get; } = type;
    }
}
