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
    public static IEnumerable<T> Where<T>(this IEnumerable<T> source, IComposableExpression<T, bool> condition)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (condition == null) throw new ArgumentNullException(nameof(condition));

        return source.Where(condition.Invoke);
    }

    /// <summary>Determines whether any sequence element satisfies a composable condition.</summary>
    public static bool Any<T>(this IEnumerable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.Any(condition.Invoke);
    }

    /// <summary>Determines whether every sequence element satisfies a composable condition.</summary>
    public static bool All<T>(this IEnumerable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.All(condition.Invoke);
    }

    /// <summary>Returns the number of sequence elements satisfying a composable condition.</summary>
    public static int Count<T>(this IEnumerable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.Count(condition.Invoke);
    }

    /// <summary>Returns an <see cref="long"/> count of sequence elements satisfying a composable condition.</summary>
    public static long LongCount<T>(this IEnumerable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.LongCount(condition.Invoke);
    }

    /// <summary>Returns the first sequence element satisfying a composable condition.</summary>
    public static T First<T>(this IEnumerable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.First(condition.Invoke);
    }

    /// <summary>Returns the first sequence element satisfying a composable condition, or its default value.</summary>
    public static T? FirstOrDefault<T>(this IEnumerable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.FirstOrDefault(condition.Invoke);
    }

    /// <summary>Returns the last sequence element satisfying a composable condition.</summary>
    public static T Last<T>(this IEnumerable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.Last(condition.Invoke);
    }

    /// <summary>Returns the last sequence element satisfying a composable condition, or its default value.</summary>
    public static T? LastOrDefault<T>(this IEnumerable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.LastOrDefault(condition.Invoke);
    }

    /// <summary>Returns the only sequence element satisfying a composable condition.</summary>
    public static T Single<T>(this IEnumerable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.Single(condition.Invoke);
    }

    /// <summary>Returns the only sequence element satisfying a composable condition, or its default value.</summary>
    public static T? SingleOrDefault<T>(this IEnumerable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.SingleOrDefault(condition.Invoke);
    }

    /// <summary>Skips sequence elements while a composable condition is satisfied.</summary>
    public static IEnumerable<T> SkipWhile<T>(this IEnumerable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.SkipWhile(condition.Invoke);
    }

    /// <summary>Returns sequence elements while a composable condition is satisfied.</summary>
    public static IEnumerable<T> TakeWhile<T>(this IEnumerable<T> source, IComposableExpression<T, bool> condition)
    {
        ValidateArguments(source, condition);

        return source.TakeWhile(condition.Invoke);
    }

    private static void ValidateArguments<T>(IEnumerable<T> source, IComposableExpression<T, bool> condition)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (condition == null) throw new ArgumentNullException(nameof(condition));
    }
}
