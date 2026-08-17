using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>Provides a factory for specification templates defined from a sample type.</summary>
/// <typeparam name="TSample">The sample type whose members define the available structural shape.</typeparam>
public static class SpecificationTemplate<TSample>
{
    /// <summary>Creates a specification template over a structural shape selected from the sample type.</summary>
    /// <typeparam name="TTemplate">The type of structural shape selected by <paramref name="template"/>.</typeparam>
    /// <param name="template">An expression that selects the members required by the specification.</param>
    /// <param name="expression">The predicate expression defined over the selected shape.</param>
    /// <returns>A template that can adapt the specification to types exposing compatible members.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="template"/> or <paramref name="expression"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="template"/> is not a supported structural member selection.</exception>
    public static SpecificationTemplate<TSample, TTemplate> Create<TTemplate>(
        Expression<Func<TSample, TTemplate>> template,
        Expression<Func<TTemplate, bool>> expression)
    {
        return new SpecificationTemplate<TSample, TTemplate>(template, expression);
    }
}

/// <summary>Represents a specification template that can be adapted to types exposing compatible members.</summary>
/// <typeparam name="TSample">The sample type whose members define the available structural shape.</typeparam>
/// <typeparam name="TTemplate">The type of structural shape used by the specification predicate.</typeparam>
/// <param name="template">An expression that selects the members required by the specification.</param>
/// <param name="expression">The predicate expression defined over the selected shape.</param>
/// <exception cref="ArgumentNullException"><paramref name="template"/> or <paramref name="expression"/> is null.</exception>
/// <exception cref="ArgumentException"><paramref name="template"/> is not a supported structural member selection.</exception>
public sealed class SpecificationTemplate<TSample, TTemplate>(
    Expression<Func<TSample, TTemplate>> template,
    Expression<Func<TTemplate, bool>> expression)
    : ISpecificationTemplate<TTemplate>
{
    private readonly ExpressionTemplate<TSample, TTemplate> _inner = new(template, expression);

    /// <summary>Creates a specification for a target type that exposes compatible members.</summary>
    /// <typeparam name="TTarget">The type to which the predicate is adapted.</typeparam>
    /// <param name="newParameterName">
    /// The name of the parameter in the generated expression, or <see langword="null"/> to reuse the template predicate's parameter name.
    /// </param>
    /// <returns>A specification containing the adapted predicate.</returns>
    /// <exception cref="InvalidOperationException">
    /// A required target member is missing, ambiguous, unreadable, or has an incompatible type.
    /// </exception>
    /// <exception cref="NotSupportedException">The predicate uses the structural parameter in an unsupported way.</exception>
    public Specification<TTarget> Adapt<TTarget>(string? newParameterName = null) =>
        _inner.AdaptSpecification<TTarget>(newParameterName);

    /// <summary>Creates a specification for a target type that exposes compatible members.</summary>
    /// <typeparam name="TTarget">The type to which the predicate is adapted.</typeparam>
    /// <param name="newParameterName">
    /// The name of the parameter in the generated expression, or <see langword="null"/> to reuse the template predicate's parameter name.
    /// </param>
    /// <returns>A specification containing the adapted predicate.</returns>
    /// <exception cref="InvalidOperationException">
    /// A required target member is missing, ambiguous, unreadable, or has an incompatible type.
    /// </exception>
    /// <exception cref="NotSupportedException">The predicate uses the structural parameter in an unsupported way.</exception>
    public Specification<TTarget> AdaptSpecification<TTarget>(string? newParameterName = null) =>
        _inner.AdaptSpecification<TTarget>(newParameterName);
}

/// <summary>Represents a specification template that can be adapted to compatible target types.</summary>
/// <typeparam name="TTemplate">The type of structural shape used by the specification predicate.</typeparam>
public interface ISpecificationTemplate<TTemplate>
{
    /// <summary>Creates a specification for a target type that exposes compatible members.</summary>
    /// <typeparam name="TTarget">The type to which the predicate is adapted.</typeparam>
    /// <param name="newParameterName">
    /// The name of the parameter in the generated expression, or <see langword="null"/> to reuse the template predicate's parameter name.
    /// </param>
    /// <returns>A specification containing the adapted predicate.</returns>
    /// <exception cref="InvalidOperationException">
    /// A required target member is missing, ambiguous, unreadable, or has an incompatible type.
    /// </exception>
    /// <exception cref="NotSupportedException">The predicate uses the structural parameter in an unsupported way.</exception>
    Specification<TTarget> Adapt<TTarget>(string? newParameterName = null);
}
