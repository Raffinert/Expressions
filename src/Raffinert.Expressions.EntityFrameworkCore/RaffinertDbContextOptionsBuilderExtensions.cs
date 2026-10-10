using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Raffinert.Expressions;

/// <summary>Registers optional Raffinert query expansion with EF Core.</summary>
public static class RaffinertDbContextOptionsBuilderExtensions
{
    /// <summary>Enables invocation expansion and cache-safe wrapper resolution for this context.</summary>
    /// <remarks>
    /// Requires .NET 10 and EF Core 10.x. Call after configuring the database provider.
    /// Repeated calls are harmless. Explicit EF.Constant values may appear in SQL;
    /// embedded EF directives support scalar captures and literals, rejecting computed operands.
    /// Captured scalars introduced by expansion are bound as execution parameters.
    /// Cache keys and compilation share one prepared expression. Hidden captured collections
    /// require direct operators; compiled wrappers with runtime captures are unsupported.
    /// ToQueryString rejects lifted parameters because EF diagnostic rendering includes values.
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
