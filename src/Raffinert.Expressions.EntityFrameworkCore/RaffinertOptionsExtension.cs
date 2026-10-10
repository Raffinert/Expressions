using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffinert.Expressions;

internal sealed class RaffinertOptionsExtension : IDbContextOptionsExtension
{
    public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        services.TryAddScoped<QueryExecutionState>();
        Decorate<IQueryContextFactory>(services, (provider, state) => new RecordingQueryContextFactory(provider, state));
        Decorate<ICompiledQueryCacheKeyGenerator>(services, (provider, state) => new ExpansionCacheKeyGenerator(provider, state));
    }

    private static void Decorate<TService>(IServiceCollection services, Func<TService, QueryExecutionState, TService> decorate)
        where TService : class
    {
        // Retain the provider's implementation and its model/relational/options semantics.
        var provider = services.LastOrDefault(x => x.ServiceType == typeof(TService))
            ?? throw new InvalidOperationException("Configure the EF Core database provider before UseRaffinertExpressions().");
        services.Remove(provider);
        services.Add(new ServiceDescriptor(typeof(TService), serviceProvider =>
        {
            var original = (TService)(provider.ImplementationInstance
                ?? provider.ImplementationFactory?.Invoke(serviceProvider)
                ?? ActivatorUtilities.CreateInstance(serviceProvider, provider.ImplementationType!));
            return decorate(original, serviceProvider.GetRequiredService<QueryExecutionState>());
        }, provider.Lifetime));
    }

    public void Validate(IDbContextOptions options) { }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;
        public override string LogFragment => "RaffinertExpressions ";
        public override int GetServiceProviderHashCode() => 0;
        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;
        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) => debugInfo["Raffinert:Expansion"] = "1";
    }

    private sealed class RecordingQueryContextFactory(IQueryContextFactory provider, QueryExecutionState state) : IQueryContextFactory, IDisposable
    {
        public QueryContext Create()
        {
            var context = provider.Create();
            state.SetContext(context);
            return context;
        }

        public void Dispose()
        {
            if (provider is IDisposable disposable) disposable.Dispose();
        }
    }

    private sealed class ExpansionCacheKeyGenerator(ICompiledQueryCacheKeyGenerator provider, QueryExecutionState state) : ICompiledQueryCacheKeyGenerator, IDisposable
    {
        public object GenerateCacheKey(Expression query, bool async) =>
            provider.GenerateCacheKey(EfQueryExpansion.Expand(state.ResolveWrappers(query)), async);

        public void Dispose()
        {
            if (provider is IDisposable disposable) disposable.Dispose();
        }
    }
}
