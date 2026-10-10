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

    // Isolated public-contract prototype. It handles ONLY this test's single Where,
    // using the wrapper's existing core expansion; it is not a second Invoke expander.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeLateParameterPrototypeBindsFreshValuesOnCacheHits(bool text)
    {
        var name = "Desk";
        int? customer = 1;
        var condition = text ? Condition<OrderRow>.Create(x => x.Name == name)
            : Condition<OrderRow>.Create(x => x.CustomerId == customer);
        var prototype = new Prototype(condition);
        await using var fixture = await SqliteFixture.CreateAsync(false, b =>
        {
            b.EnableServiceProviderCaching(false);
            ((IDbContextOptionsBuilderInfrastructure)b).AddOrUpdateExtension(prototype);
            b.AddInterceptors(prototype);
        });
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x));
        Assert.Equal(text ? new[] { 2 } : new[] { 1, 2 }, (await query.ToArrayAsync()).Select(x => x.Id));
        name = "Hidden";
        customer = 2;
        Assert.Equal(new[] { 4 }, (await query.ToArrayAsync()).Select(x => x.Id));
        name = "Desk";
        customer = null;
        Assert.Equal(text ? new[] { 2 } : new[] { 3 }, (await query.ToArrayAsync()).Select(x => x.Id));
        Assert.Equal(1, fixture.QueryCompilations);
        Assert.Equal(3, prototype.Preparations);
        Assert.Equal(1, prototype.Compilations);
        Assert.Contains(text ? "Desk" : 1, fixture.Commands.Executed[0].Values);
        Assert.Contains(text ? "Hidden" : 2, fixture.Commands.Executed[1].Values);
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
