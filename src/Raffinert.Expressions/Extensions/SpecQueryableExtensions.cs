namespace Raffinert.Expressions;

/// <summary>Queryable integration for specifications.</summary>
public static class SpecQueryableExtensions
{
    /// <summary>Filters a query with the fully expanded specification expression.</summary>
    public static IQueryable<T> Where<T>(this IQueryable<T> source, Expr<T, bool> spec)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (spec == null) throw new ArgumentNullException(nameof(spec));
        return Queryable.Where(source, spec.GetExpandedExpression());
    }
}
