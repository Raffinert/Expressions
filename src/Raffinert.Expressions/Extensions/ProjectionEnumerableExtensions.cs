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

    /// <summary>Projects each sequence element to a sequence and flattens the resulting sequences.</summary>
    public static IEnumerable<TResult> SelectMany<TSource, TResult>(
        this IEnumerable<TSource> source,
        ComposableExpression<TSource, IEnumerable<TResult>> projection)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (projection == null) throw new ArgumentNullException(nameof(projection));

        return source.SelectMany(projection.Invoke);
    }

    /// <summary>Sorts sequence elements in ascending order using a composable key selector.</summary>
    public static IOrderedEnumerable<TSource> OrderBy<TSource, TKey>(
        this IEnumerable<TSource> source,
        ComposableExpression<TSource, TKey> keySelector)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

        return source.OrderBy(keySelector.Invoke);
    }

    /// <summary>Sorts sequence elements in descending order using a composable key selector.</summary>
    public static IOrderedEnumerable<TSource> OrderByDescending<TSource, TKey>(
        this IEnumerable<TSource> source,
        ComposableExpression<TSource, TKey> keySelector)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

        return source.OrderByDescending(keySelector.Invoke);
    }

    /// <summary>Performs a subsequent ascending ordering using a composable key selector.</summary>
    public static IOrderedEnumerable<TSource> ThenBy<TSource, TKey>(
        this IOrderedEnumerable<TSource> source,
        ComposableExpression<TSource, TKey> keySelector)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

        return source.ThenBy(keySelector.Invoke);
    }

    /// <summary>Performs a subsequent descending ordering using a composable key selector.</summary>
    public static IOrderedEnumerable<TSource> ThenByDescending<TSource, TKey>(
        this IOrderedEnumerable<TSource> source,
        ComposableExpression<TSource, TKey> keySelector)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

        return source.ThenByDescending(keySelector.Invoke);
    }

    /// <summary>Groups sequence elements using a composable key selector.</summary>
    public static IEnumerable<IGrouping<TKey, TSource>> GroupBy<TSource, TKey>(
        this IEnumerable<TSource> source,
        ComposableExpression<TSource, TKey> keySelector)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

        return source.GroupBy(keySelector.Invoke);
    }

    /// <summary>Groups projected sequence elements using composable key and element selectors.</summary>
    public static IEnumerable<IGrouping<TKey, TElement>> GroupBy<TSource, TKey, TElement>(
        this IEnumerable<TSource> source,
        ComposableExpression<TSource, TKey> keySelector,
        ComposableExpression<TSource, TElement> elementSelector)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        if (elementSelector == null) throw new ArgumentNullException(nameof(elementSelector));

        return source.GroupBy(keySelector.Invoke, elementSelector.Invoke);
    }
}
