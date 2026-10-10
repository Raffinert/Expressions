using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;

namespace Raffinert.Expressions;

// Isolated experiment only: EF10 internal infrastructure, not a stable extension API.
// Not registered by UseRaffinertExpressions; the existing production path is retained.
internal sealed class RaffinertQueryCompiler(IQueryCompiler original) : IQueryCompiler
{
    public TResult Execute<TResult>(Expression query) => original.Execute<TResult>(ExpressionExpander.Expand(query));

    public TResult ExecuteAsync<TResult>(Expression query, CancellationToken cancellationToken) =>
        original.ExecuteAsync<TResult>(ExpressionExpander.Expand(query), cancellationToken);

    // Compiled entry points are forwarded unchanged for this initial experiment.
    // No compiled/precompiled support claim is made until their separate gate passes.
    public Func<QueryContext, TResult> CreateCompiledQuery<TResult>(Expression query) =>
        original.CreateCompiledQuery<TResult>(query);

    public Func<QueryContext, TResult> CreateCompiledAsyncQuery<TResult>(Expression query) =>
        original.CreateCompiledAsyncQuery<TResult>(query);

    [Experimental("EF9100")]
    public Expression<Func<QueryContext, TResult>> PrecompileQuery<TResult>(Expression query, bool async) =>
        original.PrecompileQuery<TResult>(query, async);
}
