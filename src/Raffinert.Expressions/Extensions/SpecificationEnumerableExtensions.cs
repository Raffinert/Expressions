namespace Raffinert.Expressions;

/// <summary>Provides specification-based LINQ operations for in-memory sequences.</summary>
public static class SpecificationEnumerableExtensions
{
    /// <summary>Filters a sequence using a composable predicate expression.</summary>
    /// <typeparam name="T">The type of elements in <paramref name="source"/>.</typeparam>
    /// <param name="source">The sequence to filter.</param>
    /// <param name="specification">The expression used to test each element.</param>
    /// <returns>A sequence containing the elements accepted by <paramref name="specification"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="specification"/> is null.</exception>
    public static IEnumerable<T> Where<T>(this IEnumerable<T> source, ComposableExpression<T, bool> specification)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (specification == null) throw new ArgumentNullException(nameof(specification));
        return source.Where(specification.Invoke);
    }
}
