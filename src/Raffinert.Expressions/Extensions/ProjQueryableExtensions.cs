namespace Raffinert.Expressions;

/// <summary>Queryable integration for projections.</summary>
public static class ProjQueryableExtensions
{
    /// <summary>Projects a query with the fully expanded projection expression.</summary>
    public static IQueryable<TResult> Select<TSource, TResult>(
        this IQueryable<TSource> source,
        Expr<TSource, TResult> projection)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (projection == null) throw new ArgumentNullException(nameof(projection));
        return Queryable.Select(source, projection.GetExpandedExpression());
    }
}
