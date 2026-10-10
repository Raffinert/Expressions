using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Raffinert.Expressions;

/// <summary>Registers optional Raffinert query expansion with EF Core.</summary>
public static class RaffinertDbContextOptionsBuilderExtensions
{
    /// <summary>Enables invocation expansion and cache-safe wrapper resolution for this context.</summary>
    /// <remarks>Call after configuring the database provider. Repeated calls are harmless.</remarks>
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
