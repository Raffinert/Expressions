using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Raffinert.Expressions;

/// <summary>Provides EF Core async terminal operators for composable conditions.</summary>
public static class ConditionEntityFrameworkQueryableExtensions
{
    /// <summary>Determines whether any element satisfies an expanded condition.</summary>
    /// <typeparam name="T">The query element type.</typeparam>
    /// <param name="source">The EF Core query to execute.</param>
    /// <param name="condition">The composable predicate to expand before execution.</param>
    /// <param name="cancellationToken">A token passed unchanged to EF Core.</param>
    /// <returns>The asynchronous result using EF Core's terminal operator semantics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="condition"/> is null.</exception>
    public static Task<bool> AnyAsync<T>(this IQueryable<T> source,
        IComposableExpression<T, bool> condition, CancellationToken cancellationToken = default) =>
        EntityFrameworkQueryableExtensions.AnyAsync(source, GetPredicate(source, condition), cancellationToken);

    /// <summary>Determines whether every element satisfies an expanded condition.</summary>
    /// <typeparam name="T">The query element type.</typeparam>
    /// <param name="source">The EF Core query to execute.</param>
    /// <param name="condition">The composable predicate to expand before execution.</param>
    /// <param name="cancellationToken">A token passed unchanged to EF Core.</param>
    /// <returns>The asynchronous result using EF Core's terminal operator semantics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="condition"/> is null.</exception>
    public static Task<bool> AllAsync<T>(this IQueryable<T> source,
        IComposableExpression<T, bool> condition, CancellationToken cancellationToken = default) =>
        EntityFrameworkQueryableExtensions.AllAsync(source, GetPredicate(source, condition), cancellationToken);

    /// <summary>Counts elements satisfying an expanded condition.</summary>
    /// <typeparam name="T">The query element type.</typeparam>
    /// <param name="source">The EF Core query to execute.</param>
    /// <param name="condition">The composable predicate to expand before execution.</param>
    /// <param name="cancellationToken">A token passed unchanged to EF Core.</param>
    /// <returns>The asynchronous result using EF Core's terminal operator semantics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="condition"/> is null.</exception>
    public static Task<int> CountAsync<T>(this IQueryable<T> source,
        IComposableExpression<T, bool> condition, CancellationToken cancellationToken = default) =>
        EntityFrameworkQueryableExtensions.CountAsync(source, GetPredicate(source, condition), cancellationToken);

    /// <summary>Counts elements satisfying an expanded condition using a 64-bit result.</summary>
    /// <typeparam name="T">The query element type.</typeparam>
    /// <param name="source">The EF Core query to execute.</param>
    /// <param name="condition">The composable predicate to expand before execution.</param>
    /// <param name="cancellationToken">A token passed unchanged to EF Core.</param>
    /// <returns>The asynchronous result using EF Core's terminal operator semantics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="condition"/> is null.</exception>
    public static Task<long> LongCountAsync<T>(this IQueryable<T> source,
        IComposableExpression<T, bool> condition, CancellationToken cancellationToken = default) =>
        EntityFrameworkQueryableExtensions.LongCountAsync(source, GetPredicate(source, condition), cancellationToken);

    /// <summary>Returns the first element satisfying an expanded condition.</summary>
    /// <typeparam name="T">The query element type.</typeparam>
    /// <param name="source">The EF Core query to execute.</param>
    /// <param name="condition">The composable predicate to expand before execution.</param>
    /// <param name="cancellationToken">A token passed unchanged to EF Core.</param>
    /// <returns>The asynchronous result using EF Core's terminal operator semantics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="condition"/> is null.</exception>
    public static Task<T> FirstAsync<T>(this IQueryable<T> source,
        IComposableExpression<T, bool> condition, CancellationToken cancellationToken = default) =>
        EntityFrameworkQueryableExtensions.FirstAsync(source, GetPredicate(source, condition), cancellationToken);

    /// <summary>Returns the first matching element, or its default value.</summary>
    /// <typeparam name="T">The query element type.</typeparam>
    /// <param name="source">The EF Core query to execute.</param>
    /// <param name="condition">The composable predicate to expand before execution.</param>
    /// <param name="cancellationToken">A token passed unchanged to EF Core.</param>
    /// <returns>The asynchronous result using EF Core's terminal operator semantics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="condition"/> is null.</exception>
    public static Task<T?> FirstOrDefaultAsync<T>(this IQueryable<T> source,
        IComposableExpression<T, bool> condition, CancellationToken cancellationToken = default) =>
        EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(source, GetPredicate(source, condition), cancellationToken);

    /// <summary>Returns the only element satisfying an expanded condition.</summary>
    /// <typeparam name="T">The query element type.</typeparam>
    /// <param name="source">The EF Core query to execute.</param>
    /// <param name="condition">The composable predicate to expand before execution.</param>
    /// <param name="cancellationToken">A token passed unchanged to EF Core.</param>
    /// <returns>The asynchronous result using EF Core's terminal operator semantics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="condition"/> is null.</exception>
    public static Task<T> SingleAsync<T>(this IQueryable<T> source,
        IComposableExpression<T, bool> condition, CancellationToken cancellationToken = default) =>
        EntityFrameworkQueryableExtensions.SingleAsync(source, GetPredicate(source, condition), cancellationToken);

    /// <summary>Returns the only matching element, or its default value.</summary>
    /// <typeparam name="T">The query element type.</typeparam>
    /// <param name="source">The EF Core query to execute.</param>
    /// <param name="condition">The composable predicate to expand before execution.</param>
    /// <param name="cancellationToken">A token passed unchanged to EF Core.</param>
    /// <returns>The asynchronous result using EF Core's terminal operator semantics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="condition"/> is null.</exception>
    public static Task<T?> SingleOrDefaultAsync<T>(this IQueryable<T> source,
        IComposableExpression<T, bool> condition, CancellationToken cancellationToken = default) =>
        EntityFrameworkQueryableExtensions.SingleOrDefaultAsync(source, GetPredicate(source, condition), cancellationToken);

    /// <summary>Returns the last element satisfying an expanded condition.</summary>
    /// <typeparam name="T">The query element type.</typeparam>
    /// <param name="source">The EF Core query to execute.</param>
    /// <param name="condition">The composable predicate to expand before execution.</param>
    /// <param name="cancellationToken">A token passed unchanged to EF Core.</param>
    /// <returns>The asynchronous result using EF Core's terminal operator semantics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="condition"/> is null.</exception>
    public static Task<T> LastAsync<T>(this IQueryable<T> source,
        IComposableExpression<T, bool> condition, CancellationToken cancellationToken = default) =>
        EntityFrameworkQueryableExtensions.LastAsync(source, GetPredicate(source, condition), cancellationToken);

    /// <summary>Returns the last matching element, or its default value.</summary>
    /// <typeparam name="T">The query element type.</typeparam>
    /// <param name="source">The EF Core query to execute.</param>
    /// <param name="condition">The composable predicate to expand before execution.</param>
    /// <param name="cancellationToken">A token passed unchanged to EF Core.</param>
    /// <returns>The asynchronous result using EF Core's terminal operator semantics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="condition"/> is null.</exception>
    public static Task<T?> LastOrDefaultAsync<T>(this IQueryable<T> source,
        IComposableExpression<T, bool> condition, CancellationToken cancellationToken = default) =>
        EntityFrameworkQueryableExtensions.LastOrDefaultAsync(source, GetPredicate(source, condition), cancellationToken);

    private static Expression<Func<T, bool>> GetPredicate<T>(IQueryable<T> source, IComposableExpression<T, bool> condition)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(condition);
        return ComposableExpressionAdapter.GetExpandedExpression(condition);
    }
}

