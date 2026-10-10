using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;

namespace Raffinert.Expressions.EntityFrameworkCore.IntegrationTests;

public class ServiceCompositionTests
{
    [Fact]
    public async Task SharedOptionsKeepInterleavedContextsAndClosuresIndependent()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        await using var second = new OrdersContext(fixture.Options);
        var firstThreshold = 1000;
        var secondThreshold = 10000;
        IComposableExpression<OrderRow, bool> first = Condition<OrderRow>.Create(x => x.TotalCents > firstThreshold);
        IComposableExpression<OrderRow, bool> other = Condition<OrderRow>.Create(x => x.TotalCents > secondThreshold);
        var firstQuery = fixture.Db.Orders.Where(x => first.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        var secondQuery = second.Orders.Where(x => other.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        Assert.Equal(new[] { 2, 3, 4 }, await firstQuery.ToArrayAsync());
        Assert.Equal(new[] { 2 }, await secondQuery.ToArrayAsync());
        firstThreshold = 10000;
        secondThreshold = 1000;
        Assert.Equal(new[] { 2 }, await firstQuery.ToArrayAsync());
        Assert.Equal(new[] { 2, 3, 4 }, await secondQuery.ToArrayAsync());
        Assert.Equal(4, fixture.Commands.Executed.Count);
        Assert.NotSame(fixture.Db.GetService<IQueryContextFactory>(), second.GetService<IQueryContextFactory>());
        Assert.NotSame(fixture.Db.GetService<ICompiledQueryCacheKeyGenerator>(), second.GetService<ICompiledQueryCacheKeyGenerator>());
        Assert.All(fixture.Commands.Executed, command => Assert.Contains("WHERE", command.Sql));
    }

    [Fact]
    public async Task FailedTranslationDoesNotPoisonTheNextQuery()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var invalid = Condition<OrderRow>.Create(x => Unsupported(x.Name));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Db.Orders.Where(x => invalid.Invoke(x)).ToArrayAsync());
        Assert.Empty(fixture.Commands.Executed);
        var valid = Condition<OrderRow>.Create(x => x.Id == 4);
        Assert.Equal(new[] { 4 }, await fixture.Db.Orders.Where(x => valid.Invoke(x)).Select(x => x.Id).ToArrayAsync());
        Assert.Contains("WHERE", Assert.Single(fixture.Commands.Executed).Sql);
    }

    [Fact]
    public async Task ManualInterceptorCannotRecoverExtractedCapturedWrappers()
    {
        await using var fixture = await SqliteFixture.CreateAsync(false,
            b => b.AddInterceptors(RaffinertExpressionInterceptor.Instance));
        var condition = Condition<OrderRow>.Create(x => x.Active);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Db.Orders.Where(x => condition.Invoke(x)).ToArrayAsync());
        Assert.Contains("Unable to resolve expression instance", error.Message);
        Assert.Empty(fixture.Commands.Executed);
        Assert.Equal(2, await fixture.Db.Orders.CountAsync(condition));
        Assert.Contains("WHERE", Assert.Single(fixture.Commands.Executed).Sql);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicServiceDecoratorsComposeInEitherRegistrationOrder(bool raffinertFirst)
    {
        var counters = new ServiceCounters();
        await using var fixture = await SqliteFixture.CreateAsync(false, builder =>
        {
            if (raffinertFirst) builder.UseRaffinertExpressions();
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new ObservingExtension(counters));
            if (!raffinertFirst) builder.UseRaffinertExpressions();
            // Untyped and typed repeated calls must retain one registration.
            ((DbContextOptionsBuilder)builder).UseRaffinertExpressions();
            builder.UseRaffinertExpressions();
        });
        counters.Contexts = counters.Keys = 0;
        var condition = Condition<OrderRow>.Create(x => x.Active);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        Assert.Equal(new[] { 1, 2 }, await query.ToArrayAsync());
        condition = Condition<OrderRow>.Create(x => !x.Active);
        Assert.Equal(new[] { 3, 4 }, await query.ToArrayAsync());
        Assert.Equal(2, counters.Contexts);
        Assert.Equal(2, counters.Keys);
        Assert.Equal(2, fixture.Commands.Executed.Count);
        Assert.All(fixture.Commands.Executed, command => Assert.Contains("WHERE", command.Sql));
    }

    private static bool Unsupported(string value) => value.Length > 0;

    private sealed class ServiceCounters
    {
        public int Contexts;
        public int Keys;
    }

    private sealed class ObservingExtension(ServiceCounters counters) : IDbContextOptionsExtension
    {
        public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);
        public void Validate(IDbContextOptions options) { }
        public void ApplyServices(IServiceCollection services)
        {
            Wrap<IQueryContextFactory>(services, original => new ObservingFactory(original, counters));
            Wrap<ICompiledQueryCacheKeyGenerator>(services, original => new ObservingKeys(original, counters));
            Assert.DoesNotContain(services, x => x.ServiceType.Name == "QueryExecutionState");
        }

        private static void Wrap<T>(IServiceCollection services, Func<T, T> wrap) where T : class
        {
            var original = services.Last(x => x.ServiceType == typeof(T));
            Assert.Equal(ServiceLifetime.Scoped, original.Lifetime);
            services.Remove(original);
            services.Add(ServiceDescriptor.Scoped(typeof(T), provider => wrap((T)(original.ImplementationInstance
                ?? original.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, original.ImplementationType!)))));
        }

        private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
        {
            public override bool IsDatabaseProvider => false;
            public override string LogFragment => "Observing ";
            public override int GetServiceProviderHashCode() => 0;
            public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => false;
            public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) => debugInfo["Tests:Observing"] = "1";
        }
    }

    private sealed class ObservingFactory(IQueryContextFactory original, ServiceCounters counters) : IQueryContextFactory
    {
        public QueryContext Create()
        {
            counters.Contexts++;
            return original.Create();
        }
    }

    private sealed class ObservingKeys(ICompiledQueryCacheKeyGenerator original, ServiceCounters counters) : ICompiledQueryCacheKeyGenerator
    {
        public object GenerateCacheKey(Expression query, bool async)
        {
            counters.Keys++;
            return original.GenerateCacheKey(query, async);
        }
    }
}
