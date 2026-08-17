namespace Raffinert.Expressions;

/// <summary>In-memory sequence integration for specifications.</summary>
public static class SpecEnumerableExtensions
{
    /// <summary>Filters an in-memory sequence with a cached compiled specification.</summary>
    public static IEnumerable<T> Where<T>(this IEnumerable<T> source, Expr<T, bool> spec)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (spec == null) throw new ArgumentNullException(nameof(spec));
        return Enumerable.Where(source, spec.Invoke);
    }
}
