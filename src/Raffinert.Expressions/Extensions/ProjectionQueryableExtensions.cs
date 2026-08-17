namespace Raffinert.Expressions;

/// <summary>Provides projection-based LINQ operations for queryable sequences.</summary>
public static class ProjectionQueryableExtensions
{
    /// <summary>Projects each element of a query using an expanded composable expression.</summary>
    /// <typeparam name="TSource">The type of elements in <paramref name="source"/>.</typeparam>
    /// <typeparam name="TResult">The type of value produced for each element.</typeparam>
    /// <param name="source">The query whose elements are projected.</param>
    /// <param name="projection">The expression used to project each element.</param>
    /// <returns>A query whose elements are the results of applying <paramref name="projection"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="projection"/> is null.</exception>
    public static IQueryable<TResult> Select<TSource, TResult>(
        this IQueryable<TSource> source,
        ComposableExpression<TSource, TResult> projection)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (projection == null) throw new ArgumentNullException(nameof(projection));

        return source.Select(projection.GetExpandedExpression());
    }
}
