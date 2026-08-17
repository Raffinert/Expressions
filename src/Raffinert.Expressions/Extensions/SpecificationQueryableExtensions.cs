namespace Raffinert.Expressions;

/// <summary>Provides specification-based LINQ operations for queryable sequences.</summary>
public static class SpecificationQueryableExtensions
{
    /// <summary>Filters a query using an expanded composable predicate expression.</summary>
    /// <typeparam name="T">The type of elements in <paramref name="source"/>.</typeparam>
    /// <param name="source">The query to filter.</param>
    /// <param name="specification">The expression used to test each element.</param>
    /// <returns>A query containing the elements accepted by <paramref name="specification"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="specification"/> is null.</exception>
    public static IQueryable<T> Where<T>(this IQueryable<T> source, ComposableExpression<T, bool> specification)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (specification == null) throw new ArgumentNullException(nameof(specification));
        return source.Where(specification.GetExpandedExpression());
    }
}
