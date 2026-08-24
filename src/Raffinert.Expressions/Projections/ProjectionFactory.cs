using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>Provides projection creation with a source type and an inferred result type.</summary>
/// <typeparam name="TSource">The type of value supplied to the projection.</typeparam>
/// <remarks>
/// This factory is useful when the result is an anonymous type. The result type is inferred from the
/// expression, so only the source type needs to be written:
/// <c>Projection&lt;Order&gt;.Create(order =&gt; new { order.Id })</c>.
/// </remarks>
public static class Projection<TSource>
{
    /// <summary>Creates a projection whose result type is inferred from the expression.</summary>
    /// <typeparam name="TResult">The inferred type produced by the projection.</typeparam>
    /// <param name="expression">The transformation expression represented by the projection.</param>
    /// <returns>A projection that represents <paramref name="expression"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is null.</exception>
    public static Projection<TSource, TResult> Create<TResult>(
        Expression<Func<TSource, TResult>> expression) =>
        Projection<TSource, TResult>.Create(expression);
}
