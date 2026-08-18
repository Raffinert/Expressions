namespace Raffinert.Expressions;

/// <summary>Provides condition-based LINQ operations for in-memory sequences.</summary>
public static class ConditionEnumerableExtensions
{
    /// <summary>Filters a sequence using a composable condition expression.</summary>
    /// <typeparam name="T">The type of elements in <paramref name="source"/>.</typeparam>
    /// <param name="source">The sequence to filter.</param>
    /// <param name="condition">The expression used to test each element.</param>
    /// <returns>A sequence containing the elements accepted by <paramref name="condition"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="condition"/> is null.</exception>
    public static IEnumerable<T> Where<T>(this IEnumerable<T> source, ComposableExpression<T, bool> condition)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (condition == null) throw new ArgumentNullException(nameof(condition));

        return source.Where(condition.Invoke);
    }
}
