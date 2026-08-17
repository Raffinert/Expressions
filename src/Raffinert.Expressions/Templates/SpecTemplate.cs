using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>Compatibility facade for creating Boolean structural expression templates.</summary>
public static class SpecTemplate<TSample>
{
    /// <summary>Creates a specification template.</summary>
    public static SpecTemplate<TSample, TTemplate> Create<TTemplate>(
        Expression<Func<TSample, TTemplate>> template,
        Expression<Func<TTemplate, bool>> expression)
    {
        return new SpecTemplate<TSample, TTemplate>(template, expression);
    }
}

/// <summary>Compatibility wrapper over <see cref="ExpressionTemplate{TSample,TTemplate}"/>.</summary>
/// <param name="template">The sample shape selector.</param>
/// <param name="expression">The Boolean expression written over the structural shape.</param>
public sealed class SpecTemplate<TSample, TTemplate>(
    Expression<Func<TSample, TTemplate>> template,
    Expression<Func<TTemplate, bool>> expression)
    : ISpecTemplate<TTemplate>
{
    private readonly ExpressionTemplate<TSample, TTemplate> _inner = new(template, expression);

    /// <summary>Adapts the template to a compatible target type.</summary>
    public Spec<TTarget> Adapt<TTarget>(string? newParameterName = null) =>
        _inner.AdaptSpec<TTarget>(newParameterName);

    /// <summary>Adapts the template to a compatible target type.</summary>
    public Spec<TTarget> AdaptSpec<TTarget>(string? newParameterName = null) =>
        _inner.AdaptSpec<TTarget>(newParameterName);
}

/// <summary>Non-sample-specific compatibility contract for a specification template.</summary>
public interface ISpecTemplate<TTemplate>
{
    /// <summary>Adapts the template to a compatible target type.</summary>
    Spec<TTarget> Adapt<TTarget>(string? newParameterName = null);
}
