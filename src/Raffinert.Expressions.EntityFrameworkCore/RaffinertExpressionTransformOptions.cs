using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>Configures ordered, context-specific transformations applied after Raffinert expansion.</summary>
public sealed class RaffinertExpressionTransformOptions
{
    private readonly List<Func<Expression, Expression>> transformers = [];

    /// <summary>Adds a transformation of the complete EF query expression, in registration order.</summary>
    /// <remarks>Callbacks must be thread-safe and preserve EF-translatable semantics. Avoid capturing scoped services.</remarks>
    public RaffinertExpressionTransformOptions Transform(Func<Expression, Expression> transformer)
    {
        ArgumentNullException.ThrowIfNull(transformer);
        transformers.Add(transformer);
        return this;
    }

    internal Func<Expression, Expression>[] Snapshot() => transformers.ToArray();
}
