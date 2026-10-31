using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Raffinert.Expressions;

/// <summary>Provides EF Core async terminal operators for composable conditions.</summary>
public static class PredicateEntityFrameworkQueryableExtensions
{
    /// <summary>Determines whether any element satisfies an expanded predicate.</summary>
    /// <typeparam name="T">The query element type.</typeparam>
    /// <param name="source">The EF Core query to execute.</param>
    /// <param name="predicate">The composable predicate to expand before execution.</param>
    /// <param name="cancellationToken">A token passed unchanged to EF Core.</param>
    /// <returns>The asynchronous result using EF Core's terminal operator semantics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="predicate"/> is null.</exception>
    public static Task<bool> AnyAsync<T>(this IQueryable<T> source,
        IExpr<T, bool> predicate, CancellationToken cancellationToken = default) =>
        EntityFrameworkQueryableExtensions.AnyAsync(source, GetPredicate(source, predicate), cancellationToken);

    /// <summary>Determines whether every element satisfies an expanded predicate.</summary>
    /// <typeparam name="T">The query element type.</typeparam>
    /// <param name="source">The EF Core query to execute.</param>
    /// <param name="predicate">The composable predicate to expand before execution.</param>
    /// <param name="cancellationToken">A token passed unchanged to EF Core.</param>
    /// <returns>The asynchronous result using EF Core's terminal operator semantics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="predicate"/> is null.</exception>
    public static Task<bool> AllAsync<T>(this IQueryable<T> source,
        IExpr<T, bool> predicate, CancellationToken cancellationToken = default) =>
        EntityFrameworkQueryableExtensions.AllAsync(source, GetPredicate(source, predicate), cancellationToken);

    /// <summary>Counts elements satisfying an expanded predicate.</summary>
    /// <typeparam name="T">The query element type.</typeparam>
    /// <param name="source">The EF Core query to execute.</param>
    /// <param name="predicate">The composable predicate to expand before execution.</param>
    /// <param name="cancellationToken">A token passed unchanged to EF Core.</param>
    /// <returns>The asynchronous result using EF Core's terminal operator semantics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="predicate"/> is null.</exception>
    public static Task<int> CountAsync<T>(this IQueryable<T> source,
        IExpr<T, bool> predicate, CancellationToken cancellationToken = default) =>
        EntityFrameworkQueryableExtensions.CountAsync(source, GetPredicate(source, predicate), cancellationToken);

    /// <summary>Counts elements satisfying an expanded predicate using a 64-bit result.</summary>
    /// <typeparam name="T">The query element type.</typeparam>
    /// <param name="source">The EF Core query to execute.</param>
    /// <param name="predicate">The composable predicate to expand before execution.</param>
    /// <param name="cancellationToken">A token passed unchanged to EF Core.</param>
    /// <returns>The asynchronous result using EF Core's terminal operator semantics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="predicate"/> is null.</exception>
    public static Task<long> LongCountAsync<T>(this IQueryable<T> source,
        IExpr<T, bool> predicate, CancellationToken cancellationToken = default) =>
        EntityFrameworkQueryableExtensions.LongCountAsync(source, GetPredicate(source, predicate), cancellationToken);

    /// <summary>Returns the first element satisfying an expanded predicate.</summary>
    /// <typeparam name="T">The query element type.</typeparam>
    /// <param name="source">The EF Core query to execute.</param>
    /// <param name="predicate">The composable predicate to expand before execution.</param>
    /// <param name="cancellationToken">A token passed unchanged to EF Core.</param>
    /// <returns>The asynchronous result using EF Core's terminal operator semantics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="predicate"/> is null.</exception>
    public static Task<T> FirstAsync<T>(this IQueryable<T> source,
        IExpr<T, bool> predicate, CancellationToken cancellationToken = default) =>
        EntityFrameworkQueryableExtensions.FirstAsync(source, GetPredicate(source, predicate), cancellationToken);

    /// <summary>Returns the first matching element, or its default value.</summary>
    /// <typeparam name="T">The query element type.</typeparam>
    /// <param name="source">The EF Core query to execute.</param>
    /// <param name="predicate">The composable predicate to expand before execution.</param>
    /// <param name="cancellationToken">A token passed unchanged to EF Core.</param>
    /// <returns>The asynchronous result using EF Core's terminal operator semantics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="predicate"/> is null.</exception>
    public static Task<T?> FirstOrDefaultAsync<T>(this IQueryable<T> source,
        IExpr<T, bool> predicate, CancellationToken cancellationToken = default) =>
        EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(source, GetPredicate(source, predicate), cancellationToken);

    /// <summary>Returns the only element satisfying an expanded predicate.</summary>
    /// <typeparam name="T">The query element type.</typeparam>
    /// <param name="source">The EF Core query to execute.</param>
    /// <param name="predicate">The composable predicate to expand before execution.</param>
    /// <param name="cancellationToken">A token passed unchanged to EF Core.</param>
    /// <returns>The asynchronous result using EF Core's terminal operator semantics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="predicate"/> is null.</exception>
    public static Task<T> SingleAsync<T>(this IQueryable<T> source,
        IExpr<T, bool> predicate, CancellationToken cancellationToken = default) =>
        EntityFrameworkQueryableExtensions.SingleAsync(source, GetPredicate(source, predicate), cancellationToken);

    /// <summary>Returns the only matching element, or its default value.</summary>
    /// <typeparam name="T">The query element type.</typeparam>
    /// <param name="source">The EF Core query to execute.</param>
    /// <param name="predicate">The composable predicate to expand before execution.</param>
    /// <param name="cancellationToken">A token passed unchanged to EF Core.</param>
    /// <returns>The asynchronous result using EF Core's terminal operator semantics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="predicate"/> is null.</exception>
    public static Task<T?> SingleOrDefaultAsync<T>(this IQueryable<T> source,
        IExpr<T, bool> predicate, CancellationToken cancellationToken = default) =>
        EntityFrameworkQueryableExtensions.SingleOrDefaultAsync(source, GetPredicate(source, predicate), cancellationToken);

    /// <summary>Returns the last element satisfying an expanded predicate.</summary>
    /// <typeparam name="T">The query element type.</typeparam>
    /// <param name="source">The EF Core query to execute.</param>
    /// <param name="predicate">The composable predicate to expand before execution.</param>
    /// <param name="cancellationToken">A token passed unchanged to EF Core.</param>
    /// <returns>The asynchronous result using EF Core's terminal operator semantics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="predicate"/> is null.</exception>
    public static Task<T> LastAsync<T>(this IQueryable<T> source,
        IExpr<T, bool> predicate, CancellationToken cancellationToken = default) =>
        EntityFrameworkQueryableExtensions.LastAsync(source, GetPredicate(source, predicate), cancellationToken);

    /// <summary>Returns the last matching element, or its default value.</summary>
    /// <typeparam name="T">The query element type.</typeparam>
    /// <param name="source">The EF Core query to execute.</param>
    /// <param name="predicate">The composable predicate to expand before execution.</param>
    /// <param name="cancellationToken">A token passed unchanged to EF Core.</param>
    /// <returns>The asynchronous result using EF Core's terminal operator semantics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="predicate"/> is null.</exception>
    public static Task<T?> LastOrDefaultAsync<T>(this IQueryable<T> source,
        IExpr<T, bool> predicate, CancellationToken cancellationToken = default) =>
        EntityFrameworkQueryableExtensions.LastOrDefaultAsync(source, GetPredicate(source, predicate), cancellationToken);

    private static Expression<Func<T, bool>> GetPredicate<T>(IQueryable<T> source, IExpr<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);
        return ExprAdapter.GetExpandedExpression(predicate);
    }
}

