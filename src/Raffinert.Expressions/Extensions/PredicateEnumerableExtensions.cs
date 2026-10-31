namespace Raffinert.Expressions;

/// <summary>Provides predicate-based LINQ operations for in-memory sequences.</summary>
public static class PredicateEnumerableExtensions
{
    /// <summary>Filters a sequence using a composable predicate expression.</summary>
    /// <typeparam name="T">The type of elements in <paramref name="source"/>.</typeparam>
    /// <param name="source">The sequence to filter.</param>
    /// <param name="predicate">The expression used to test each element.</param>
    /// <returns>A sequence containing the elements accepted by <paramref name="predicate"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="predicate"/> is null.</exception>
    public static IEnumerable<T> Where<T>(this IEnumerable<T> source, IExpr<T, bool> predicate)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        return source.Where(predicate.Invoke);
    }

    /// <summary>Determines whether any sequence element satisfies a composable predicate.</summary>
    public static bool Any<T>(this IEnumerable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.Any(predicate.Invoke);
    }

    /// <summary>Determines whether every sequence element satisfies a composable predicate.</summary>
    public static bool All<T>(this IEnumerable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.All(predicate.Invoke);
    }

    /// <summary>Returns the number of sequence elements satisfying a composable predicate.</summary>
    public static int Count<T>(this IEnumerable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.Count(predicate.Invoke);
    }

    /// <summary>Returns an <see cref="long"/> count of sequence elements satisfying a composable predicate.</summary>
    public static long LongCount<T>(this IEnumerable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.LongCount(predicate.Invoke);
    }

    /// <summary>Returns the first sequence element satisfying a composable predicate.</summary>
    public static T First<T>(this IEnumerable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.First(predicate.Invoke);
    }

    /// <summary>Returns the first sequence element satisfying a composable predicate, or its default value.</summary>
    public static T? FirstOrDefault<T>(this IEnumerable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.FirstOrDefault(predicate.Invoke);
    }

    /// <summary>Returns the last sequence element satisfying a composable predicate.</summary>
    public static T Last<T>(this IEnumerable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.Last(predicate.Invoke);
    }

    /// <summary>Returns the last sequence element satisfying a composable predicate, or its default value.</summary>
    public static T? LastOrDefault<T>(this IEnumerable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.LastOrDefault(predicate.Invoke);
    }

    /// <summary>Returns the only sequence element satisfying a composable predicate.</summary>
    public static T Single<T>(this IEnumerable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.Single(predicate.Invoke);
    }

    /// <summary>Returns the only sequence element satisfying a composable predicate, or its default value.</summary>
    public static T? SingleOrDefault<T>(this IEnumerable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.SingleOrDefault(predicate.Invoke);
    }

    /// <summary>Skips sequence elements while a composable predicate is satisfied.</summary>
    public static IEnumerable<T> SkipWhile<T>(this IEnumerable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.SkipWhile(predicate.Invoke);
    }

    /// <summary>Returns sequence elements while a composable predicate is satisfied.</summary>
    public static IEnumerable<T> TakeWhile<T>(this IEnumerable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.TakeWhile(predicate.Invoke);
    }

    private static void ValidateArguments<T>(IEnumerable<T> source, IExpr<T, bool> predicate)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
    }
}
