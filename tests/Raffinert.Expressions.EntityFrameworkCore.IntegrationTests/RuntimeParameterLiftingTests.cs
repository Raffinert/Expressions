using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;

namespace Raffinert.Expressions.EntityFrameworkCore.IntegrationTests;

public class RuntimeParameterLiftingTests
{
    [Fact]
    public async Task EmbeddedSensitiveStringIsBoundAsDbParameterNotSqlLiteral()
    {
        await using var fixture = await SqliteFixture.CreateAsync(configure: b => b.EnableSensitiveDataLogging(false));
        var marker = "synthetic-confidential-8c7a@example.invalid";
        var condition = Condition<OrderRow>.Create(x => x.Name == marker);
        Assert.Empty(await fixture.Db.Orders.Where(x => condition.Invoke(x)).ToArrayAsync());
        var command = Assert.Single(fixture.Commands.Executed);
        Assert.DoesNotContain(marker, command.Sql);
        Assert.Contains(marker, command.Values);
        Assert.Contains("WHERE", command.Sql);
    }

    [Fact]
    public async Task TwentyFiveCapturedValuesShareOneCompilationAndSqlShape()
    {
        await using var fixture = await SqliteFixture.CreateAsync(configure: b => b.EnableServiceProviderCaching(false));
        var threshold = 100;
        var condition = Condition<OrderRow>.Create(x => x.TotalCents > threshold);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x));
        for (var i = 0; i < 25; i++)
        {
            threshold = 100 + i;
            Assert.Equal(4, await query.CountAsync());
            Assert.Equal(threshold, Assert.Single(fixture.Commands.Executed.Last().Values));
        }
        threshold = 100;
        Assert.Equal(4, await query.CountAsync());
        Assert.Equal(100, Assert.Single(fixture.Commands.Executed.Last().Values));
        Assert.Equal(1, fixture.QueryCompilations);
        Assert.Single(fixture.Commands.Executed.Select(x => x.Sql).Distinct());
        Assert.Single(fixture.Commands.Executed.Select(command => Assert.Single(command.Names)).Distinct());
    }

    [Fact]
    public async Task DiagnosticsAndGetterFailuresFollowNativeEfAndRecover()
    {
        var messages = new List<string>();
        await using var fixture = await SqliteFixture.CreateAsync(configure: b =>
            b.EnableSensitiveDataLogging(false).LogTo(messages.Add));
        messages.Clear();
        var marker = "synthetic-confidential-diagnostic@example.invalid";
        var condition = Condition<OrderRow>.Create(x => x.Name == marker);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x));
        await query.ToArrayAsync();
        Assert.DoesNotContain(messages, message => message.Contains(marker, StringComparison.Ordinal));
        Assert.True(query.ToQueryString().Contains(marker, StringComparison.Ordinal));
        fixture.Commands.Clear();
        var holder = new ThrowingHolder(marker);
        condition = Condition<OrderRow>.Create(x => x.Name == holder.Value);
        var getter = await Assert.ThrowsAsync<InvalidOperationException>(() => query.ToArrayAsync());
        Assert.NotNull(getter.InnerException);
        Assert.Empty(fixture.Commands.Executed);
        condition = Condition<OrderRow>.Create(x => x.Id == 4);
        Assert.Equal(new[] { 4 }, (await query.ToArrayAsync()).Select(x => x.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PooledLeasesReuseServicesWithoutStaleBindings(bool factory)
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var commands = new CommandRecorder();
        var services = new ServiceCollection();
        void Configure(DbContextOptionsBuilder builder) => builder.UseSqlite(fixture.Db.Database.GetDbConnection())
            .UseRaffinertExpressions().AddInterceptors(commands);
        if (factory) services.AddPooledDbContextFactory<OrdersContext>(Configure, poolSize: 1);
        else services.AddDbContextPool<OrdersContext>(Configure, poolSize: 1);
        await using var provider = services.BuildServiceProvider();
        IQueryContextFactory? firstFactory = null;
        for (var i = 0; i < 6; i++)
        {
            using var scope = provider.CreateScope();
            await using var db = factory ? scope.ServiceProvider.GetRequiredService<IDbContextFactory<OrdersContext>>().CreateDbContext()
                : scope.ServiceProvider.GetRequiredService<OrdersContext>();
            if (firstFactory == null) firstFactory = db.GetService<IQueryContextFactory>();
            else Assert.Same(firstFactory, db.GetService<IQueryContextFactory>());
            var threshold = i % 2 == 0 ? 1000 : 10000;
            var condition = Condition<OrderRow>.Create(x => x.TotalCents > threshold);
            Assert.Equal(i % 2 == 0 ? new[] { 2, 3, 4 } : new[] { 2 },
                await db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());
            Assert.Equal(threshold, Assert.Single(commands.Executed.Last().Values));
            condition = Condition<OrderRow>.Create(x => x.Id == 4);
            Assert.Equal(new[] { 4 }, await db.Orders.Where(x => condition.Invoke(x)).Select(x => x.Id).ToArrayAsync());
        }
        Assert.Equal(12, commands.Executed.Count);
        Assert.Single(commands.Executed.Where((_, i) => i % 2 == 0).Select(x => x.Sql).Distinct());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OtherQueryInterceptorsKeepTheirAddedFiltersInEitherOrder(bool first)
    {
        var interceptor = new TakeOneInterceptor();
        await using var fixture = await SqliteFixture.CreateAsync(false, b =>
        {
            b.EnableServiceProviderCaching(false);
            if (first) b.AddInterceptors(interceptor);
            b.UseRaffinertExpressions();
            if (!first) b.AddInterceptors(interceptor);
        });
        var id = 0;
        var condition = Condition<OrderRow>.Create(x => x.Id > id);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x));
        Assert.Single(await query.ToArrayAsync());
        id = 2;
        var result = Assert.Single(await query.ToArrayAsync());
        Assert.True(result.Id > 2);
        Assert.All(fixture.Commands.Executed, command => Assert.Contains("LIMIT", command.Sql));
        Assert.Contains(2, fixture.Commands.Executed[1].Values);
        Assert.Equal(1, interceptor.Calls);
    }

    [Fact]
    public async Task CompiledRuntimeCapturesFailSafelyButScalarDelegateParametersWork()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var marker = "synthetic-compiled@example.invalid";
        var condition = Condition<OrderRow>.Create(x => x.Name == marker);
        var compiled = EF.CompileQuery((OrdersContext db) => db.Orders.Count(x => condition.Invoke(x)));
        var error = Assert.Throws<NotSupportedException>(() => compiled(fixture.Db));
        Assert.DoesNotContain(marker, error.ToString());
        Assert.Empty(fixture.Commands.Executed);
        var stable = Condition<OrderRow>.Create(x => x.Active);
        var supported = EF.CompileQuery((OrdersContext db, string name) => db.Orders.Count(x => stable.Invoke(x) && x.Name == name));
        Assert.Equal(1, supported(fixture.Db, "Desk"));
        Assert.Equal(0, supported(fixture.Db, marker));
        Assert.Contains(marker, fixture.Commands.Executed[1].Values);
        Assert.DoesNotContain(marker, fixture.Commands.Executed[1].Sql);
    }

    [Fact]
    public async Task IdenticalWrapperStructuresShareCacheAndDifferentStructuresDoNot()
    {
        await using var fixture = await SqliteFixture.CreateAsync(configure: b => b.EnableServiceProviderCaching(false));
        var id = 2;
        var condition = Condition<OrderRow>.Create(x => x.Id == id);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        Assert.Equal(new[] { 2 }, await query.ToArrayAsync());
        id = 4;
        condition = Condition<OrderRow>.Create(x => x.Id == id);
        Assert.Equal(new[] { 4 }, await query.ToArrayAsync());
        Assert.Equal(1, fixture.QueryCompilations);
        condition = Condition<OrderRow>.Create(x => x.Id != id);
        Assert.Equal(new[] { 1, 2, 3 }, await query.ToArrayAsync());
        Assert.Equal(2, fixture.QueryCompilations);
    }

    [Fact]
    public async Task ParameterNamesDoNotCollideWithOuterCaptures()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var __raffinert_id_0 = 1000;
        var id = 2;
        var condition = Condition<OrderRow>.Create(x => x.Id == id);
        Assert.Equal(new[] { 2 }, await fixture.Db.Orders.Where(x => condition.Invoke(x) && x.TotalCents > __raffinert_id_0)
            .Select(x => x.Id).ToArrayAsync());
        var command = Assert.Single(fixture.Commands.Executed);
        Assert.Equal(2, command.Values.Length);
        Assert.Contains(id, command.Values);
        Assert.Contains(__raffinert_id_0, command.Values);
        Assert.Equal(2, command.Names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var outerName = command.Names[Array.IndexOf(command.Values, __raffinert_id_0)];
        Assert.NotEqual(outerName, command.Names[Array.IndexOf(command.Values, id)]);
    }

    [Fact]
    public async Task ServerMemberChainsKeepProviderTranslation()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var condition = Condition<OrderRow>.Create(x => DateTime.UtcNow.Year > 2000 && x.Active);
        Assert.Equal(2, await fixture.Db.Orders.CountAsync(x => condition.Invoke(x)));
        Assert.Empty(Assert.Single(fixture.Commands.Executed).Values);
        Assert.Contains("strftime", fixture.Commands.Executed[0].Sql);
    }

    [Fact]
    public async Task CacheKeyAndCompilationUseTheSamePreparedExpressionWithoutCaptureConstants()
    {
        var observer = new ShapeObserver();
        await using var fixture = await SqliteFixture.CreateAsync(false, b =>
        {
            b.EnableServiceProviderCaching(false);
            ((IDbContextOptionsBuilderInfrastructure)b).AddOrUpdateExtension(observer);
            b.UseRaffinertExpressions().AddInterceptors(observer);
        });
        var marker = "synthetic-shape@example.invalid";
        var condition = Condition<OrderRow>.Create(x => x.Name == marker);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x));
        await query.ToArrayAsync();
        Assert.Same(Assert.Single(observer.Keys), Assert.Single(observer.Compiled));
        marker = "synthetic-shape-next@example.invalid";
        await query.ToArrayAsync();
        Assert.Equal(2, observer.Keys.Count);
        Assert.Single(observer.Compiled);
        Assert.All(observer.Keys, expression =>
        {
            var parameters = new ParameterObserver();
            parameters.Visit(expression);
            Assert.Single(parameters.Names);
            Assert.DoesNotContain(marker, expression.ToString());
            var constants = new ConstantObserver();
            constants.Visit(expression);
            Assert.DoesNotContain(constants.Values, value => value is string text && text.Contains("synthetic-shape", StringComparison.Ordinal));
            Assert.DoesNotContain(constants.Values, value => value is Condition<OrderRow>);
        });
    }

    [Theory]
    [InlineData("bool")]
    [InlineData("DateTime")]
    [InlineData("TimeSpan")]
    [InlineData("double")]
    public async Task AdditionalScalarTypesBindAsNativeParameters(string kind)
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        bool flag = true;
        var date = new DateTime(2020, 1, 2);
        var duration = TimeSpan.FromHours(2);
        double number = 2.5;
        switch (kind)
        {
            case "bool":
                var condition = Condition<OrderRow>.Create(x => x.Active == flag);
                Assert.Equal(2, await fixture.Db.Orders.CountAsync(x => condition.Invoke(x)));
                Assert.NotNull(Assert.Single(fixture.Commands.Executed[0].Values));
                break;
            case "DateTime":
                var dateProjection = Projection<OrderRow>.Create(x => date);
                Assert.All(await fixture.Db.Orders.Select(x => dateProjection.Invoke(x)).ToArrayAsync(), value => Assert.Equal(date, value));
                Assert.NotNull(Assert.Single(fixture.Commands.Executed[0].Values));
                break;
            case "TimeSpan":
                var timeProjection = Projection<OrderRow>.Create(x => duration);
                Assert.All(await fixture.Db.Orders.Select(x => timeProjection.Invoke(x)).ToArrayAsync(), value => Assert.Equal(duration, value));
                Assert.NotNull(Assert.Single(fixture.Commands.Executed[0].Values));
                break;
            default:
                var numberProjection = Projection<OrderRow>.Create(x => number);
                Assert.All(await fixture.Db.Orders.Select(x => numberProjection.Invoke(x)).ToArrayAsync(), value => Assert.Equal(number, value));
                Assert.Equal(number, Assert.Single(fixture.Commands.Executed[0].Values));
                break;
        }
    }

    [Fact]
    public async Task ReferenceCaptureProjectionUsesNativeClientProjection()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var secret = new Uri("https://synthetic-secret.example.invalid/path");
        var projection = Projection<OrderRow>.Create(x => secret);
        var expected = await fixture.Db.Orders.Select(x => secret).ToArrayAsync();
        Assert.Equal(expected, await fixture.Db.Orders.Select(x => projection.Invoke(x)).ToArrayAsync());
    }

    [Fact]
    public async Task RebuildingInterceptorsPreserveExpandedNativeQueries()
    {
        await using var fixture = await SqliteFixture.CreateAsync(false, b =>
            b.EnableServiceProviderCaching(false).AddInterceptors(new RebuildWhereInterceptor()).UseRaffinertExpressions());
        var marker = "synthetic-rewrite@example.invalid";
        var condition = Condition<OrderRow>.Create(x => x.Name == marker);
        Assert.Empty(await fixture.Db.Orders.Where(x => condition.Invoke(x)).ToArrayAsync());
        Assert.Equal(marker, Assert.Single(fixture.Commands.Executed.Last().Values));
        Assert.Equal(2, (await fixture.Db.Orders.Where(x => x.Active).ToArrayAsync()).Length);
    }

    [Fact]
    public async Task CompiledAsyncRuntimeCapturesFailBeforeSqlWithSanitizedError()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var marker = "synthetic-compiled-async@example.invalid";
        var condition = Condition<OrderRow>.Create(x => x.Name == marker);
        var compiled = EF.CompileAsyncQuery((OrdersContext db) => db.Orders.Where(x => condition.Invoke(x)));
        var error = await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await foreach (var _ in compiled(fixture.Db)) { }
        });
        Assert.DoesNotContain(marker, error.ToString());
        Assert.Empty(fixture.Commands.Executed);
    }

    [Fact]
    public async Task UnsupportedCompiledCaptureGettersAreRejectedWithoutReadingThem()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var holder = new ThrowingHolder("synthetic-unread@example.invalid");
        var condition = Condition<OrderRow>.Create(x => x.Name == holder.Value);
        var compiled = EF.CompileQuery((OrdersContext db) => db.Orders.Count(x => condition.Invoke(x)));
        Assert.Throws<NotSupportedException>(() => compiled(fixture.Db));
        Assert.Equal(0, holder.Reads);
        Assert.Empty(fixture.Commands.Executed);
    }

    private sealed class RebuildWhereInterceptor : IQueryExpressionInterceptor
    {
        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData) =>
            queryExpression is MethodCallExpression { Method.Name: nameof(Queryable.Where) } where
                ? Expression.Call(where.Method, where.Arguments)
                : queryExpression;
    }

    private sealed class ShapeObserver : IDbContextOptionsExtension, IQueryExpressionInterceptor
    {
        public List<Expression> Keys { get; } = [];
        public List<Expression> Compiled { get; } = [];
        public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);
        public void Validate(IDbContextOptions options) { }
        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        {
            Compiled.Add(queryExpression);
            return queryExpression;
        }
        public void ApplyServices(IServiceCollection services)
        {
            var descriptor = services.Last(x => x.ServiceType == typeof(ICompiledQueryCacheKeyGenerator));
            services.Remove(descriptor);
            services.Add(new ServiceDescriptor(typeof(ICompiledQueryCacheKeyGenerator), provider =>
                new KeysObserver((ICompiledQueryCacheKeyGenerator)(descriptor.ImplementationInstance
                    ?? descriptor.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!)), Keys), descriptor.Lifetime));
        }
        private sealed class KeysObserver(ICompiledQueryCacheKeyGenerator original, List<Expression> keys) : ICompiledQueryCacheKeyGenerator
        {
            public object GenerateCacheKey(Expression query, bool async)
            {
                keys.Add(query);
                return original.GenerateCacheKey(query, async);
            }
        }
        private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
        {
            public override bool IsDatabaseProvider => false;
            public override string LogFragment => "ShapeObserver ";
            public override int GetServiceProviderHashCode() => 0;
            public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => false;
            public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) => debugInfo["Tests:ShapeObserver"] = "1";
        }
    }

    private sealed class ParameterObserver : ExpressionVisitor
    {
        public List<string> Names { get; } = [];
        public override Expression? Visit(Expression? node)
        {
            if (node is QueryParameterExpression parameter) Names.Add(parameter.Name);
            return base.Visit(node);
        }
    }

    private sealed class ConstantObserver : ExpressionVisitor
    {
        public List<object?> Values { get; } = [];
        protected override Expression VisitConstant(ConstantExpression node)
        {
            Values.Add(node.Value);
            return node;
        }
    }

    private sealed class ThrowingHolder(string marker)
    {
        public int Reads { get; private set; }
        public string Value
        {
            get
            {
                Reads++;
                throw new InvalidOperationException(marker);
            }
        }
    }

    private sealed class TakeOneInterceptor : IQueryExpressionInterceptor
    {
        public int Calls { get; private set; }
        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        {
            Calls++;
            return Expression.Call(typeof(Queryable), nameof(Queryable.Take), new[] { typeof(OrderRow) }, queryExpression, Expression.Constant(1));
        }
    }

    // Native string/int/nullable bindings remain current on cache hits.
    [Theory]
    [InlineData("string")]
    [InlineData("int")]
    [InlineData("nullable")]
    public async Task NativeParametersBindFreshValuesOnCacheHits(string kind)
    {
        var text = kind == "string";
        var name = "Desk";
        var id = 2;
        int? customer = 1;
        var condition = text ? Condition<OrderRow>.Create(x => x.Name == name)
            : kind == "int" ? Condition<OrderRow>.Create(x => x.Id == id)
            : Condition<OrderRow>.Create(x => x.CustomerId == customer);
        await using var fixture = await SqliteFixture.CreateAsync(configure: b => b.EnableServiceProviderCaching(false));
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x));
        Assert.Equal(text || kind == "int" ? new[] { 2 } : new[] { 1, 2 }, (await query.ToArrayAsync()).Select(x => x.Id));
        name = "Hidden";
        id = 4;
        customer = 2;
        Assert.Equal(new[] { 4 }, (await query.ToArrayAsync()).Select(x => x.Id));
        name = "Desk";
        id = 2;
        customer = null;
        Assert.Equal(text || kind == "int" ? new[] { 2 } : new[] { 3 }, (await query.ToArrayAsync()).Select(x => x.Id));
        Assert.Equal(1, fixture.QueryCompilations);
        Assert.Equal(3, fixture.Commands.Executed.Count);
        Assert.Contains(text ? "Desk" : kind == "int" ? 2 : 1, fixture.Commands.Executed[0].Values);
        Assert.Contains(text ? "Hidden" : kind == "int" ? 4 : 2, fixture.Commands.Executed[1].Values);
        Assert.DoesNotContain("Desk", fixture.Commands.Executed[0].Sql);
    }

}
