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
    }

    [Fact]
    public async Task DiagnosticsAndGetterExceptionsDoNotExposeCapturedValues()
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
        var rendering = Assert.Throws<NotSupportedException>(() => query.ToQueryString());
        Assert.DoesNotContain(marker, rendering.ToString());
        fixture.Commands.Clear();
        var holder = new ThrowingHolder(marker);
        condition = Condition<OrderRow>.Create(x => x.Name == holder.Value);
        var getter = await Assert.ThrowsAsync<InvalidOperationException>(() => query.ToArrayAsync());
        Assert.DoesNotContain(marker, getter.ToString());
        Assert.Null(getter.InnerException);
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
        var __raffinert_runtime = 1000;
        var id = 2;
        var condition = Condition<OrderRow>.Create(x => x.Id == id);
        Assert.Equal(new[] { 2 }, await fixture.Db.Orders.Where(x => condition.Invoke(x) && x.TotalCents > __raffinert_runtime)
            .Select(x => x.Id).ToArrayAsync());
        var command = Assert.Single(fixture.Commands.Executed);
        Assert.Equal(2, command.Values.Length);
        Assert.Contains(id, command.Values);
        Assert.Contains(__raffinert_runtime, command.Values);
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
    public async Task UnsupportedReferenceCapturesFailWithoutSensitiveDiagnostics()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var secret = new Uri("https://synthetic-secret.example.invalid/path");
        var projection = Projection<OrderRow>.Create(x => secret);
        var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
            fixture.Db.Orders.Select(x => projection.Invoke(x)).ToArrayAsync());
        Assert.DoesNotContain(secret.ToString(), error.ToString());
        Assert.Empty(fixture.Commands.Executed);
    }

    [Fact]
    public async Task DestructiveEarlierInterceptorsFailSafelyAndLeaveOrdinaryQueriesFunctional()
    {
        await using var fixture = await SqliteFixture.CreateAsync(false, b =>
            b.EnableServiceProviderCaching(false).AddInterceptors(new RebuildWhereInterceptor()).UseRaffinertExpressions());
        var marker = "synthetic-rewrite@example.invalid";
        var condition = Condition<OrderRow>.Create(x => x.Name == marker);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Db.Orders.Where(x => condition.Invoke(x)).ToArrayAsync());
        Assert.Contains("Another query interceptor", error.Message);
        Assert.DoesNotContain(marker, error.ToString());
        Assert.Empty(fixture.Commands.Executed);
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
        public string Value => throw new InvalidOperationException(marker);
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

    // Isolated public-contract prototype. It handles ONLY this test's single Where,
    // using the wrapper's existing core expansion; it is not a second Invoke expander.
    [Theory]
    [InlineData("string")]
    [InlineData("int")]
    [InlineData("nullable")]
    public async Task NativeLateParameterPrototypeBindsFreshValuesOnCacheHits(string kind)
    {
        var text = kind == "string";
        var name = "Desk";
        var id = 2;
        int? customer = 1;
        var condition = text ? Condition<OrderRow>.Create(x => x.Name == name)
            : kind == "int" ? Condition<OrderRow>.Create(x => x.Id == id)
            : Condition<OrderRow>.Create(x => x.CustomerId == customer);
        var prototype = new Prototype(condition);
        await using var fixture = await SqliteFixture.CreateAsync(false, b =>
        {
            b.EnableServiceProviderCaching(false);
            ((IDbContextOptionsBuilderInfrastructure)b).AddOrUpdateExtension(prototype);
            b.AddInterceptors(prototype);
        });
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
        Assert.Equal(3, prototype.Preparations);
        Assert.Equal(1, prototype.Compilations);
        Assert.Contains(text ? "Desk" : kind == "int" ? 2 : 1, fixture.Commands.Executed[0].Values);
        Assert.Contains(text ? "Hidden" : kind == "int" ? 4 : 2, fixture.Commands.Executed[1].Values);
        Assert.DoesNotContain("Desk", fixture.Commands.Executed[0].Sql);
    }

    private sealed class Prototype(Condition<OrderRow> condition) : IDbContextOptionsExtension, IQueryExpressionInterceptor
    {
        private QueryContext? _context;
        private Expression? _original;
        private Expression? _prepared;
        public int Preparations { get; private set; }
        public int Compilations { get; private set; }
        public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);
        public void Validate(IDbContextOptions options) { }
        public void ApplyServices(IServiceCollection services)
        {
            Wrap<IQueryContextFactory>(services, original => new Factory(original, this));
            Wrap<ICompiledQueryCacheKeyGenerator>(services, original => new Keys(original, this));
        }

        private static void Wrap<T>(IServiceCollection services, Func<T, T> wrap) where T : class
        {
            var descriptor = services.Last(x => x.ServiceType == typeof(T));
            services.Remove(descriptor);
            services.Add(new ServiceDescriptor(typeof(T), provider => wrap((T)(descriptor.ImplementationInstance
                ?? descriptor.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!))), descriptor.Lifetime));
        }

        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        {
            Assert.Same(_original, queryExpression);
            Assert.Same(_context!.Context, eventData.Context);
            Compilations++;
            return _prepared!;
        }

        private Expression Prepare(Expression query)
        {
            Preparations++;
            var lambda = condition.GetExpandedExpression();
            var binary = Assert.IsAssignableFrom<BinaryExpression>(lambda.Body);
            var capture = Assert.IsAssignableFrom<MemberExpression>(binary.Right);
            var closure = Assert.IsAssignableFrom<ConstantExpression>(capture.Expression);
            var value = ((FieldInfo)capture.Member).GetValue(closure.Value);
            const string parameterName = "__raffinert_prototype_0";
            var nodeType = typeof(QueryContext).Assembly.GetType("Microsoft.EntityFrameworkCore.Query.QueryParameterExpression");
            var parameter = nodeType == null ? Expression.Parameter(capture.Type, parameterName)
                : (Expression)nodeType.GetConstructor(new[] { typeof(string), typeof(Type) })!.Invoke(new object[] { parameterName, capture.Type });
            var parameters = typeof(QueryContext).GetProperty("Parameters");
            if (parameters != null) ((IDictionary<string, object?>)parameters.GetValue(_context)!).Add(parameterName, value);
            else typeof(QueryContext).GetMethod("AddParameter")!.Invoke(_context, new[] { parameterName, value });
            var predicate = Expression.Lambda<Func<OrderRow, bool>>(binary.Update(binary.Left, binary.Conversion, parameter), lambda.Parameters);
            var where = Assert.IsType<MethodCallExpression>(query, exactMatch: false);
            _original = query;
            return _prepared = Expression.Call(where.Method, where.Arguments[0], Expression.Quote(predicate));
        }

        private sealed class Factory(IQueryContextFactory original, Prototype prototype) : IQueryContextFactory
        {
            public QueryContext Create() => prototype._context = original.Create();
        }
        private sealed class Keys(ICompiledQueryCacheKeyGenerator original, Prototype prototype) : ICompiledQueryCacheKeyGenerator
        {
            public object GenerateCacheKey(Expression query, bool async) => original.GenerateCacheKey(prototype.Prepare(query), async);
        }
        private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
        {
            public override bool IsDatabaseProvider => false;
            public override string LogFragment => "LateParameterPrototype ";
            public override int GetServiceProviderHashCode() => 0;
            public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => false;
            public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) => debugInfo["Tests:LatePrototype"] = "1";
        }
    }
}
