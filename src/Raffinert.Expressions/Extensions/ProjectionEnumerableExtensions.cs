namespace Raffinert.Expressions;

/// <summary>Provides projection-based LINQ operations for in-memory sequences.</summary>
public static class ProjectionEnumerableExtensions
{
    /// <summary>Projects each element of a sequence using a composable expression.</summary>
    /// <typeparam name="TSource">The type of elements in <paramref name="source"/>.</typeparam>
    /// <typeparam name="TResult">The type of value produced for each element.</typeparam>
    /// <param name="source">The sequence whose elements are projected.</param>
    /// <param name="projection">The expression used to project each element.</param>
    /// <returns>A sequence whose elements are the results of evaluating <paramref name="projection"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="projection"/> is null.</exception>
    public static IEnumerable<TResult> Select<TSource, TResult>(
        this IEnumerable<TSource> source,
        ComposableExpression<TSource, TResult> projection)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (projection == null) throw new ArgumentNullException(nameof(projection));

        return source.Select(projection.Invoke);
    }
}
