namespace Raffinert.Expressions;

/// <summary>In-memory sequence integration for projections.</summary>
public static class ProjEnumerableExtensions
{
    /// <summary>Projects an in-memory sequence with a cached compiled projection.</summary>
    public static IEnumerable<TResult> Select<TSource, TResult>(
        this IEnumerable<TSource> source,
        Expr<TSource, TResult> projection)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (projection == null) throw new ArgumentNullException(nameof(projection));
        return Enumerable.Select(source, projection.Invoke);
    }
}
