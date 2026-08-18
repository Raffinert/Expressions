namespace Raffinert.Expressions;

/// <summary>Provides condition-based LINQ operations for queryable sequences.</summary>
public static class ConditionQueryableExtensions
{
    /// <summary>Filters a query using an expanded composable condition expression.</summary>
    /// <typeparam name="T">The type of elements in <paramref name="source"/>.</typeparam>
    /// <param name="source">The query to filter.</param>
    /// <param name="condition">The expression used to test each element.</param>
    /// <returns>A query containing the elements accepted by <paramref name="condition"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="condition"/> is null.</exception>
    public static IQueryable<T> Where<T>(this IQueryable<T> source, ComposableExpression<T, bool> condition)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (condition == null) throw new ArgumentNullException(nameof(condition));

        return source.Where(condition.GetExpandedExpression());
    }
}
