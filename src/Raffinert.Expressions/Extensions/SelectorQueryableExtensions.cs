namespace Raffinert.Expressions;

/// <summary>Provides selector-based LINQ operations for queryable sequences.</summary>
public static class SelectorQueryableExtensions
{
    /// <summary>Projects each element of a query using an expanded composable expression.</summary>
    /// <typeparam name="TSource">The type of elements in <paramref name="source"/>.</typeparam>
    /// <typeparam name="TResult">The type of value produced for each element.</typeparam>
    /// <param name="source">The query whose elements are projected.</param>
    /// <param name="selector">The expression used to project each element.</param>
    /// <returns>A query whose elements are the results of applying <paramref name="selector"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="selector"/> is null.</exception>
    public static IQueryable<TResult> Select<TSource, TResult>(
        this IQueryable<TSource> source,
        IExpr<TSource, TResult> selector)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (selector == null) throw new ArgumentNullException(nameof(selector));

        return source.Select(ExprAdapter.GetExpandedExpression(selector));
    }

    /// <summary>Projects each query element to a sequence and flattens the resulting sequences.</summary>
    public static IQueryable<TResult> SelectMany<TSource, TResult>(
        this IQueryable<TSource> source,
        IExpr<TSource, IEnumerable<TResult>> selector)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (selector == null) throw new ArgumentNullException(nameof(selector));

        return source.SelectMany(ExprAdapter.GetExpandedExpression(selector));
    }

    /// <summary>Sorts query elements in ascending order using an expanded composable key selector.</summary>
    public static IOrderedQueryable<TSource> OrderBy<TSource, TKey>(
        this IQueryable<TSource> source,
        IExpr<TSource, TKey> keySelector)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

        return source.OrderBy(ExprAdapter.GetExpandedExpression(keySelector));
    }

    /// <summary>Sorts query elements in descending order using an expanded composable key selector.</summary>
    public static IOrderedQueryable<TSource> OrderByDescending<TSource, TKey>(
        this IQueryable<TSource> source,
        IExpr<TSource, TKey> keySelector)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

        return source.OrderByDescending(ExprAdapter.GetExpandedExpression(keySelector));
    }

    /// <summary>Performs a subsequent ascending ordering using an expanded composable key selector.</summary>
    public static IOrderedQueryable<TSource> ThenBy<TSource, TKey>(
        this IOrderedQueryable<TSource> source,
        IExpr<TSource, TKey> keySelector)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

        return source.ThenBy(ExprAdapter.GetExpandedExpression(keySelector));
    }

    /// <summary>Performs a subsequent descending ordering using an expanded composable key selector.</summary>
    public static IOrderedQueryable<TSource> ThenByDescending<TSource, TKey>(
        this IOrderedQueryable<TSource> source,
        IExpr<TSource, TKey> keySelector)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

        return source.ThenByDescending(ExprAdapter.GetExpandedExpression(keySelector));
    }

    /// <summary>Groups query elements using an expanded composable key selector.</summary>
    public static IQueryable<IGrouping<TKey, TSource>> GroupBy<TSource, TKey>(
        this IQueryable<TSource> source,
        IExpr<TSource, TKey> keySelector)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

        return source.GroupBy(ExprAdapter.GetExpandedExpression(keySelector));
    }

    /// <summary>Groups projected query elements using expanded composable key and element selectors.</summary>
    public static IQueryable<IGrouping<TKey, TElement>> GroupBy<TSource, TKey, TElement>(
        this IQueryable<TSource> source,
        IExpr<TSource, TKey> keySelector,
        IExpr<TSource, TElement> elementSelector)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        if (elementSelector == null) throw new ArgumentNullException(nameof(elementSelector));

        return source.GroupBy(
            ExprAdapter.GetExpandedExpression(keySelector),
            ExprAdapter.GetExpandedExpression(elementSelector));
    }
}
