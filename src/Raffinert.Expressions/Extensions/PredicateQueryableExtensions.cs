namespace Raffinert.Expressions;

/// <summary>Provides predicate-based LINQ operations for queryable sequences.</summary>
public static class PredicateQueryableExtensions
{
    /// <summary>Filters a query using an expanded composable predicate expression.</summary>
    /// <typeparam name="T">The type of elements in <paramref name="source"/>.</typeparam>
    /// <param name="source">The query to filter.</param>
    /// <param name="predicate">The expression used to test each element.</param>
    /// <returns>A query containing the elements accepted by <paramref name="predicate"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="predicate"/> is null.</exception>
    public static IQueryable<T> Where<T>(this IQueryable<T> source, IExpr<T, bool> predicate)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        return source.Where(ExprAdapter.GetExpandedExpression(predicate));
    }

    /// <summary>Determines whether any query element satisfies an expanded composable predicate.</summary>
    public static bool Any<T>(this IQueryable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.Any(ExprAdapter.GetExpandedExpression(predicate));
    }

    /// <summary>Determines whether every query element satisfies an expanded composable predicate.</summary>
    public static bool All<T>(this IQueryable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.All(ExprAdapter.GetExpandedExpression(predicate));
    }

    /// <summary>Returns the number of query elements satisfying an expanded composable predicate.</summary>
    public static int Count<T>(this IQueryable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.Count(ExprAdapter.GetExpandedExpression(predicate));
    }

    /// <summary>Returns an <see cref="long"/> count of query elements satisfying an expanded composable predicate.</summary>
    public static long LongCount<T>(this IQueryable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.LongCount(ExprAdapter.GetExpandedExpression(predicate));
    }

    /// <summary>Returns the first query element satisfying an expanded composable predicate.</summary>
    public static T First<T>(this IQueryable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.First(ExprAdapter.GetExpandedExpression(predicate));
    }

    /// <summary>Returns the first query element satisfying an expanded composable predicate, or its default value.</summary>
    public static T? FirstOrDefault<T>(this IQueryable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.FirstOrDefault(ExprAdapter.GetExpandedExpression(predicate));
    }

    /// <summary>Returns the last query element satisfying an expanded composable predicate.</summary>
    public static T Last<T>(this IQueryable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.Last(ExprAdapter.GetExpandedExpression(predicate));
    }

    /// <summary>Returns the last query element satisfying an expanded composable predicate, or its default value.</summary>
    public static T? LastOrDefault<T>(this IQueryable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.LastOrDefault(ExprAdapter.GetExpandedExpression(predicate));
    }

    /// <summary>Returns the only query element satisfying an expanded composable predicate.</summary>
    public static T Single<T>(this IQueryable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.Single(ExprAdapter.GetExpandedExpression(predicate));
    }

    /// <summary>Returns the only query element satisfying an expanded composable predicate, or its default value.</summary>
    public static T? SingleOrDefault<T>(this IQueryable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.SingleOrDefault(ExprAdapter.GetExpandedExpression(predicate));
    }

    /// <summary>Skips query elements while an expanded composable predicate is satisfied.</summary>
    public static IQueryable<T> SkipWhile<T>(this IQueryable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.SkipWhile(ExprAdapter.GetExpandedExpression(predicate));
    }

    /// <summary>Returns query elements while an expanded composable predicate is satisfied.</summary>
    public static IQueryable<T> TakeWhile<T>(this IQueryable<T> source, IExpr<T, bool> predicate)
    {
        ValidateArguments(source, predicate);

        return source.TakeWhile(ExprAdapter.GetExpandedExpression(predicate));
    }

    private static void ValidateArguments<T>(IQueryable<T> source, IExpr<T, bool> predicate)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
    }
}
