using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Raffinert.Expressions;

// EF10 compiler boundary: Raffinert expands; the original compiler owns all extraction and execution.
#pragma warning disable EF1001 // Implement and forward the provider's scoped IQueryCompiler.
internal sealed class NativeExtractionQueryCompiler(IQueryCompiler inner) : IQueryCompiler
{
    internal static void Decorate(IServiceCollection services)
    {
        var descriptors = services.Where(x => x.ServiceType == typeof(IQueryCompiler) && !x.IsKeyedService).ToArray();
        if (descriptors.Length != 1 || descriptors[0].Lifetime != ServiceLifetime.Scoped)
            throw new InvalidOperationException("Configure one scoped EF Core query compiler before UseRaffinertExpressions().");
        var original = descriptors[0];
        var key = new object();
        // The container retains ownership of the original scoped compiler, including disposal.
        // Resolve it by a private key, never through the decorated unkeyed registration.
        services.Remove(original);
        services.Add(original.ImplementationFactory is { } factory
            ? ServiceDescriptor.DescribeKeyed(typeof(IQueryCompiler), key, (provider, _) => factory(provider), original.Lifetime)
            : ServiceDescriptor.DescribeKeyed(typeof(IQueryCompiler), key, original.ImplementationType!, original.Lifetime));
        services.AddScoped<IQueryCompiler>(provider => new NativeExtractionQueryCompiler(provider.GetRequiredKeyedService<IQueryCompiler>(key)));
    }

    public TResult Execute<TResult>(Expression query) => inner.Execute<TResult>(Expand(query));

    public TResult ExecuteAsync<TResult>(Expression query, CancellationToken cancellationToken) =>
        inner.ExecuteAsync<TResult>(Expand(query), cancellationToken);

    public Func<QueryContext, TResult> CreateCompiledQuery<TResult>(Expression query) =>
        inner.CreateCompiledQuery<TResult>(query);

    public Func<QueryContext, TResult> CreateCompiledAsyncQuery<TResult>(Expression query) =>
        inner.CreateCompiledAsyncQuery<TResult>(query);

#pragma warning disable EF9100 // Owner-authorized: forward EF10's experimental precompiled-query API only.
    public Expression<Func<QueryContext, TResult>> PrecompileQuery<TResult>(Expression query, bool async) =>
        !ReferenceEquals(query, Expand(query))
            ? throw new NotSupportedException("Precompiled queries containing Raffinert wrappers are unsupported. Use ordinary LINQ or supported EF compiled queries.")
            : inner.PrecompileQuery<TResult>(query, async);
#pragma warning restore EF9100

    private static Expression Expand(Expression query)
    {
        try
        {
            return ExpressionExpander.Expand(query);
        }
        catch (InvalidOperationException error) when (error.InnerException != null)
        {
            throw new InvalidOperationException("Unable to expand a Raffinert query. Getter and constructor failures are not included in diagnostics.");
        }
    }

}
#pragma warning restore EF1001
