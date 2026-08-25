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
    public static IQueryable<T> Where<T>(this IQueryable<T> source, IComposableExpression<T, bool> condition)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (condition == null) throw new ArgumentNullException(nameof(condition));

        return source.Where(ComposableExpressionAdapter.GetExpandedExpression(condition));
    }

    /// <summary>Determines whether any query element satisfies an expanded composable condition.</summary>
    public static bool Any<T>(this IQueryable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.Any(ComposableExpressionAdapter.GetExpandedExpression(condition));
    }

    /// <summary>Determines whether every query element satisfies an expanded composable condition.</summary>
    public static bool All<T>(this IQueryable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.All(ComposableExpressionAdapter.GetExpandedExpression(condition));
    }

    /// <summary>Returns the number of query elements satisfying an expanded composable condition.</summary>
    public static int Count<T>(this IQueryable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.Count(ComposableExpressionAdapter.GetExpandedExpression(condition));
    }

    /// <summary>Returns an <see cref="long"/> count of query elements satisfying an expanded composable condition.</summary>
    public static long LongCount<T>(this IQueryable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.LongCount(ComposableExpressionAdapter.GetExpandedExpression(condition));
    }

    /// <summary>Returns the first query element satisfying an expanded composable condition.</summary>
    public static T First<T>(this IQueryable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.First(ComposableExpressionAdapter.GetExpandedExpression(condition));
    }

    /// <summary>Returns the first query element satisfying an expanded composable condition, or its default value.</summary>
    public static T? FirstOrDefault<T>(this IQueryable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.FirstOrDefault(ComposableExpressionAdapter.GetExpandedExpression(condition));
    }

    /// <summary>Returns the last query element satisfying an expanded composable condition.</summary>
    public static T Last<T>(this IQueryable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.Last(ComposableExpressionAdapter.GetExpandedExpression(condition));
    }

    /// <summary>Returns the last query element satisfying an expanded composable condition, or its default value.</summary>
    public static T? LastOrDefault<T>(this IQueryable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.LastOrDefault(ComposableExpressionAdapter.GetExpandedExpression(condition));
    }

    /// <summary>Returns the only query element satisfying an expanded composable condition.</summary>
    public static T Single<T>(this IQueryable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.Single(ComposableExpressionAdapter.GetExpandedExpression(condition));
    }

    /// <summary>Returns the only query element satisfying an expanded composable condition, or its default value.</summary>
    public static T? SingleOrDefault<T>(this IQueryable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.SingleOrDefault(ComposableExpressionAdapter.GetExpandedExpression(condition));
    }

    /// <summary>Skips query elements while an expanded composable condition is satisfied.</summary>
    public static IQueryable<T> SkipWhile<T>(this IQueryable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.SkipWhile(ComposableExpressionAdapter.GetExpandedExpression(condition));
    }

    /// <summary>Returns query elements while an expanded composable condition is satisfied.</summary>
    public static IQueryable<T> TakeWhile<T>(this IQueryable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.TakeWhile(ComposableExpressionAdapter.GetExpandedExpression(condition));
    }

    private static void ValidateArguments<T>(IQueryable<T> source, IComposableExpression<T, bool> condition)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (condition == null) throw new ArgumentNullException(nameof(condition));
    }
}
