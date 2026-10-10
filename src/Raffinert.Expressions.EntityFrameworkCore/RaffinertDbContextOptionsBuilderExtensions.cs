using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Raffinert.Expressions;

/// <summary>Registers optional Raffinert query expansion with EF Core.</summary>
public static class RaffinertDbContextOptionsBuilderExtensions
{
    /// <summary>Enables invocation expansion and cache-safe wrapper resolution for this context.</summary>
    /// <remarks>
    /// Requires .NET 10 and EF Core 10.x. Call after configuring the database provider.
    /// Repeated calls are harmless. Raffinert expands wrappers before EF's native extraction;
    /// EF owns parameter evaluation, naming, binding, caching and SQL translation, including directives and collections.
    /// ToQueryString follows native EF behavior and may include parameter values even with sensitive-data logging disabled.
    /// Compiled wrappers with runtime captures and Raffinert precompiled queries are unsupported.
    /// Uses a narrowly scoped EF10 internal IQueryCompiler contract; revalidate EF patch upgrades.
    /// </remarks>
    /// <param name="optionsBuilder">The context options to configure.</param>
    /// <returns>The same options builder.</returns>
    public static DbContextOptionsBuilder UseRaffinertExpressions(this DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        if (optionsBuilder.Options.FindExtension<RaffinertOptionsExtension>() == null)
        {
            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(new RaffinertOptionsExtension());
            optionsBuilder.AddInterceptors(RaffinertExpressionInterceptor.Instance);
        }
        return optionsBuilder;
    }

    /// <summary>Enables invocation expansion while retaining the typed options builder.</summary>
    /// <param name="optionsBuilder">The context options to configure.</param>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <returns>The same typed options builder.</returns>
    public static DbContextOptionsBuilder<TContext> UseRaffinertExpressions<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder) where TContext : DbContext
    {
        UseRaffinertExpressions((DbContextOptionsBuilder)optionsBuilder);
        return optionsBuilder;
    }
}
