using System.Collections;
using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>
/// Represents an opt-in query-syntax expansion boundary backed by an existing LINQ provider.
/// </summary>
/// <typeparam name="T">The type of query element.</typeparam>
/// <remarks>
/// This type delegates its expression, provider, and enumeration to <see cref="UnderlyingQuery"/>.
/// It does not implement or replace the underlying <see cref="IQueryProvider"/>.
/// </remarks>
public class RaffinertQueryable<T> : IQueryable<T>, IAsyncEnumerable<T>
{
    internal RaffinertQueryable(IQueryable<T> underlyingQuery)
    {
        UnderlyingQuery = underlyingQuery ?? throw new ArgumentNullException(nameof(underlyingQuery));
    }

    /// <summary>Gets the provider query represented by this facade.</summary>
    public IQueryable<T> UnderlyingQuery { get; }

    /// <inheritdoc />
    public Type ElementType => UnderlyingQuery.ElementType;

    /// <inheritdoc />
    public Expression Expression => UnderlyingQuery.Expression;

    /// <inheritdoc />
    public IQueryProvider Provider => UnderlyingQuery.Provider;

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => UnderlyingQuery.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        if (UnderlyingQuery is IAsyncEnumerable<T> asyncQuery)
            return asyncQuery.GetAsyncEnumerator(cancellationToken);

        throw new InvalidOperationException(
            $"The underlying query provider '{Provider.GetType().FullName}' does not support asynchronous enumeration.");
    }

    /// <summary>Asynchronously materializes the query as a list.</summary>
    /// <param name="cancellationToken">A token used to cancel asynchronous enumeration.</param>
    /// <returns>A task whose result contains the materialized query elements.</returns>
    public async Task<List<T>> ToListAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<T>();
        var enumerator = GetAsyncEnumerator(cancellationToken);

        try
        {
            while (await enumerator.MoveNextAsync().ConfigureAwait(false))
                results.Add(enumerator.Current);
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        return results;
    }

    /// <summary>Asynchronously materializes the query as an array.</summary>
    /// <param name="cancellationToken">A token used to cancel asynchronous enumeration.</param>
    /// <returns>A task whose result contains the materialized query elements.</returns>
    public async Task<T[]> ToArrayAsync(CancellationToken cancellationToken = default) =>
        (await ToListAsync(cancellationToken).ConfigureAwait(false)).ToArray();

    /// <summary>Filters query elements after expanding invocation markers in the predicate.</summary>
    public RaffinertQueryable<T> Where(Expression<Func<T, bool>> predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        return Wrap(Queryable.Where(UnderlyingQuery, ExpressionExpander.Expand(predicate)));
    }

    /// <summary>Projects query elements after expanding invocation markers in the selector.</summary>
    public RaffinertQueryable<TResult> Select<TResult>(Expression<Func<T, TResult>> selector)
    {
        if (selector == null) throw new ArgumentNullException(nameof(selector));

        return Wrap(Queryable.Select(UnderlyingQuery, ExpressionExpander.Expand(selector)));
    }

    /// <summary>Projects query elements to sequences and flattens them after expanding invocation markers.</summary>
    public RaffinertQueryable<TResult> SelectMany<TResult>(
        Expression<Func<T, IEnumerable<TResult>>> selector)
    {
        if (selector == null) throw new ArgumentNullException(nameof(selector));

        return Wrap(Queryable.SelectMany(UnderlyingQuery, ExpressionExpander.Expand(selector)));
    }

    /// <summary>
    /// Projects query elements to sequences and combines their elements after expanding invocation markers
    /// in both selectors.
    /// </summary>
    public RaffinertQueryable<TResult> SelectMany<TCollection, TResult>(
        Expression<Func<T, IEnumerable<TCollection>>> collectionSelector,
        Expression<Func<T, TCollection, TResult>> resultSelector)
    {
        if (collectionSelector == null) throw new ArgumentNullException(nameof(collectionSelector));
        if (resultSelector == null) throw new ArgumentNullException(nameof(resultSelector));

        return Wrap(Queryable.SelectMany(
            UnderlyingQuery,
            ExpressionExpander.Expand(collectionSelector),
            ExpressionExpander.Expand(resultSelector)));
    }

    /// <summary>Joins two sequences after expanding invocation markers in all selectors.</summary>
    public RaffinertQueryable<TResult> Join<TInner, TKey, TResult>(
        IEnumerable<TInner> inner,
        Expression<Func<T, TKey>> outerKeySelector,
        Expression<Func<TInner, TKey>> innerKeySelector,
        Expression<Func<T, TInner, TResult>> resultSelector)
    {
        if (inner == null) throw new ArgumentNullException(nameof(inner));
        if (outerKeySelector == null) throw new ArgumentNullException(nameof(outerKeySelector));
        if (innerKeySelector == null) throw new ArgumentNullException(nameof(innerKeySelector));
        if (resultSelector == null) throw new ArgumentNullException(nameof(resultSelector));

        return Wrap(Queryable.Join(
            UnderlyingQuery,
            inner,
            ExpressionExpander.Expand(outerKeySelector),
            ExpressionExpander.Expand(innerKeySelector),
            ExpressionExpander.Expand(resultSelector)));
    }

    /// <summary>Performs a grouped join after expanding invocation markers in all selectors.</summary>
    public RaffinertQueryable<TResult> GroupJoin<TInner, TKey, TResult>(
        IEnumerable<TInner> inner,
        Expression<Func<T, TKey>> outerKeySelector,
        Expression<Func<TInner, TKey>> innerKeySelector,
        Expression<Func<T, IEnumerable<TInner>, TResult>> resultSelector)
    {
        if (inner == null) throw new ArgumentNullException(nameof(inner));
        if (outerKeySelector == null) throw new ArgumentNullException(nameof(outerKeySelector));
        if (innerKeySelector == null) throw new ArgumentNullException(nameof(innerKeySelector));
        if (resultSelector == null) throw new ArgumentNullException(nameof(resultSelector));

        return Wrap(Queryable.GroupJoin(
            UnderlyingQuery,
            inner,
            ExpressionExpander.Expand(outerKeySelector),
            ExpressionExpander.Expand(innerKeySelector),
            ExpressionExpander.Expand(resultSelector)));
    }

    /// <summary>Orders query elements in ascending order after expanding invocation markers.</summary>
    public RaffinertOrderedQueryable<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

        return new RaffinertOrderedQueryable<T>(
            Queryable.OrderBy(UnderlyingQuery, ExpressionExpander.Expand(keySelector)));
    }

    /// <summary>Orders query elements in descending order after expanding invocation markers.</summary>
    public RaffinertOrderedQueryable<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

        return new RaffinertOrderedQueryable<T>(
            Queryable.OrderByDescending(UnderlyingQuery, ExpressionExpander.Expand(keySelector)));
    }

    /// <summary>Groups query elements after expanding invocation markers in the key selector.</summary>
    public RaffinertQueryable<IGrouping<TKey, T>> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

        return Wrap(Queryable.GroupBy(UnderlyingQuery, ExpressionExpander.Expand(keySelector)));
    }

    /// <summary>Groups projected query elements after expanding invocation markers in both selectors.</summary>
    public RaffinertQueryable<IGrouping<TKey, TElement>> GroupBy<TKey, TElement>(
        Expression<Func<T, TKey>> keySelector,
        Expression<Func<T, TElement>> elementSelector)
    {
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        if (elementSelector == null) throw new ArgumentNullException(nameof(elementSelector));

        return Wrap(Queryable.GroupBy(
            UnderlyingQuery,
            ExpressionExpander.Expand(keySelector),
            ExpressionExpander.Expand(elementSelector)));
    }

    /// <summary>Converts query elements to the specified type.</summary>
    public RaffinertQueryable<TResult> Cast<TResult>() => Wrap(Queryable.Cast<TResult>(UnderlyingQuery));

    private static RaffinertQueryable<TResult> Wrap<TResult>(IQueryable<TResult> query) => new(query);
}

/// <summary>
/// Represents an ordered query-syntax expansion boundary backed by an existing LINQ provider.
/// </summary>
/// <typeparam name="T">The type of query element.</typeparam>
public sealed class RaffinertOrderedQueryable<T> : RaffinertQueryable<T>, IOrderedQueryable<T>
{
    internal RaffinertOrderedQueryable(IOrderedQueryable<T> underlyingQuery)
        : base(underlyingQuery)
    {
    }

    /// <summary>Performs a subsequent ascending ordering after expanding invocation markers.</summary>
    public RaffinertOrderedQueryable<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

        return new RaffinertOrderedQueryable<T>(Queryable.ThenBy(
            (IOrderedQueryable<T>)UnderlyingQuery,
            ExpressionExpander.Expand(keySelector)));
    }

    /// <summary>Performs a subsequent descending ordering after expanding invocation markers.</summary>
    public RaffinertOrderedQueryable<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

        return new RaffinertOrderedQueryable<T>(Queryable.ThenByDescending(
            (IOrderedQueryable<T>)UnderlyingQuery,
            ExpressionExpander.Expand(keySelector)));
    }
}
