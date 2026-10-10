using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;

namespace Raffinert.Expressions;

// Experimental no-op adapter. EF10 internal contract only; no extraction APIs called.
#pragma warning disable EF1001 // Implement and forward the provider's scoped IQueryCompiler.
internal sealed class NativeExtractionQueryCompiler(IQueryCompiler inner) : IQueryCompiler
{
    public TResult Execute<TResult>(Expression query) => inner.Execute<TResult>(query);

    public TResult ExecuteAsync<TResult>(Expression query, CancellationToken cancellationToken) =>
        inner.ExecuteAsync<TResult>(query, cancellationToken);

    public Func<QueryContext, TResult> CreateCompiledQuery<TResult>(Expression query) =>
        inner.CreateCompiledQuery<TResult>(query);

    public Func<QueryContext, TResult> CreateCompiledAsyncQuery<TResult>(Expression query) =>
        inner.CreateCompiledAsyncQuery<TResult>(query);

#pragma warning disable EF9100 // Owner-authorized: forward EF10's experimental precompiled-query API only.
    public Expression<Func<QueryContext, TResult>> PrecompileQuery<TResult>(Expression query, bool async) =>
        inner.PrecompileQuery<TResult>(query, async);
#pragma warning restore EF9100
}
#pragma warning restore EF1001
