namespace Raffinert.Expressions;

/// <summary>Provides an opt-in expansion boundary for C# LINQ query expressions.</summary>
public static class RaffinertQueryableExtensions
{
    /// <summary>
    /// Wraps a query so invocation markers in subsequent query-syntax clauses are expanded before
    /// being submitted to the underlying query provider.
    /// </summary>
    /// <typeparam name="T">The type of query element.</typeparam>
    /// <param name="source">The provider query to wrap.</param>
    /// <returns>A query-syntax facade backed by the same expression and provider.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    public static RaffinertQueryable<T> AsRaffinertQuery<T>(this IQueryable<T> source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        return source as RaffinertQueryable<T> ?? new RaffinertQueryable<T>(source);
    }
}
