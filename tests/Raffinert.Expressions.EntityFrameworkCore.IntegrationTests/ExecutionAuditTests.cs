using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Raffinert.Expressions.EntityFrameworkCore.IntegrationTests;

public class ExecutionAuditTests(ITestOutputHelper output)
{
    [Fact]
    public async Task EmbeddedStringAppearsInSqlEvenWithSensitiveLoggingDisabled()
    {
        await using var fixture = await SqliteFixture.CreateAsync(configure: b => b.EnableSensitiveDataLogging(false));
        var marker = "synthetic-private-value-pr7";
        var condition = Condition<OrderRow>.Create(x => x.Name == marker);
        Assert.Empty(await fixture.Db.Orders.Where(x => condition.Invoke(x)).ToArrayAsync());
        var embedded = Assert.Single(fixture.Commands.Executed);
        Assert.Contains(marker, embedded.Sql);
        Assert.DoesNotContain(marker, embedded.Values);
        fixture.Commands.Clear();
        Assert.Empty(await fixture.Db.Orders.Where(condition).ToArrayAsync());
        var direct = Assert.Single(fixture.Commands.Executed);
        Assert.DoesNotContain(marker, direct.Sql);
        Assert.Contains(marker, direct.Values);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StableGetterIsReadForKeyAndCompilationButOnlyKeyOnCacheHit(bool async)
    {
        await using var fixture = await SqliteFixture.CreateAsync(configure: b => b.EnableServiceProviderCaching(false));
        var holder = new ScalarHolder { Value = 2 };
        var condition = Condition<OrderRow>.Create(x => x.Id == holder.Current);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x)).Select(x => x.Id);
        Task<int[]> Execute() => async ? query.ToArrayAsync() : Task.FromResult(query.ToArray());
        Assert.Equal(new[] { 2 }, await Execute());
        Assert.Equal(new[] { 2, 2, 2, 2, 2 }, holder.Reads);
        Assert.Equal(1, fixture.QueryCompilations);
        holder.Reads.Clear();
        Assert.Equal(new[] { 2 }, await Execute());
        Assert.Equal(new[] { 2, 2 }, holder.Reads);
        Assert.Equal(1, fixture.QueryCompilations);
        holder.Value = 4;
        Assert.Equal(new[] { 4 }, await Execute());
        holder.Value = 2;
        Assert.Equal(new[] { 2 }, await Execute());
        Assert.Equal(2, fixture.QueryCompilations);
        Assert.Equal(fixture.Commands.Executed[0].Sql, fixture.Commands.Executed[3].Sql);
        Assert.All(fixture.Commands.Executed, command => Assert.Empty(command.Values));
        output.WriteLine("Stable getter: five reads on first miss (including core body caching), two on hit; A -> B -> A results and literal SQL match.");
    }

    [Fact]
    public async Task ChangingGetterBetweenPassesIsOutsideTheStableCaptureContract()
    {
        await using var fixture = await SqliteFixture.CreateAsync(configure: b => b.EnableServiceProviderCaching(false));
        var holder = new ScalarHolder { Value = 2 };
        var condition = Condition<OrderRow>.Create(x => x.Id == holder.Current);
        condition.GetExpandedExpression(); // Isolate the two EF expansion passes from core's initial body cache.
        holder.Reads.Clear();
        holder.Sequence = new Queue<int>(new[] { 2, 2, 4, 4, 2, 2 });
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x)).Select(x => x.Id);
        // Deliberately violates the documented stable, side-effect-free getter contract:
        // cache key sees 2, compilation sees 4, then key 2 hits the SQL compiled for 4.
        Assert.Equal(new[] { 4 }, await query.ToArrayAsync());
        Assert.Equal(new[] { 2, 2, 4, 4 }, holder.Reads);
        Assert.Equal(new[] { 4 }, await query.ToArrayAsync());
        Assert.Equal(new[] { 2, 2, 4, 4, 2, 2 }, holder.Reads);
        Assert.Equal(1, fixture.QueryCompilations);
        Assert.Equal(fixture.Commands.Executed[0].Sql, fixture.Commands.Executed[1].Sql);
        Assert.Contains("= 4", fixture.Commands.Executed[0].Sql);
        output.WriteLine("Unsupported changing getter: key 2 / SQL 4; repeat key 2 returns ID 4 without compilation.");

        // Both parameterized controls read once per execution and bind that value.
        foreach (var direct in new[] { false, true })
        {
            holder.Sequence = new Queue<int>(new[] { 2, 4, 2 });
            holder.Reads.Clear();
            fixture.Commands.Clear();
            var control = direct ? fixture.Db.Orders.Where(condition)
                : fixture.Db.Orders.Where(x => x.Id == holder.Current);
            foreach (var expected in new[] { 2, 4, 2 })
                Assert.Equal(new[] { expected }, await control.Select(x => x.Id).ToArrayAsync());
            Assert.Equal(new[] { 2, 4, 2 }, holder.Reads);
            Assert.Equal(new object?[] { 2, 4, 2 }, fixture.Commands.Executed.Select(x => Assert.Single(x.Values)).ToArray());
        }
    }

    [Fact]
    public async Task DeferredQueriesExecuteInReverseOrderAndEnumerateSequentially()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var active = Condition<OrderRow>.Create(x => x.Active);
        var hidden = Condition<OrderRow>.Create(x => !x.Active);
        var queryA = fixture.Db.Orders.Where(x => active.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        var queryB = fixture.Db.Orders.Where(x => hidden.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        Assert.Empty(fixture.Commands.Executed);
        Assert.Equal(new[] { 3, 4 }, await queryB.ToArrayAsync());
        Assert.Equal(new[] { 1, 2 }, await queryA.ToArrayAsync());
        active = Condition<OrderRow>.Create(x => x.Id == 4);
        var ids = new List<int>();
        await foreach (var id in queryA.AsAsyncEnumerable()) ids.Add(id);
        Assert.Equal(new[] { 4 }, ids);
        ids.Clear();
        await foreach (var id in queryB.AsAsyncEnumerable()) ids.Add(id);
        Assert.Equal(new[] { 3, 4 }, ids);
        Assert.Equal(4, fixture.Commands.Executed.Count);
        Assert.NotEqual(fixture.Commands.Executed[0].Sql, fixture.Commands.Executed[1].Sql);
        Assert.All(fixture.Commands.Executed, command =>
        {
            Assert.Contains("WHERE", command.Sql);
            Assert.DoesNotContain("Invoke", command.Sql);
        });
    }

    [Fact]
    public async Task GetterFailureAndCancellationDoNotPoisonTheNextExecution()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var holder = new ScalarHolder { Throw = true };
        var condition = Condition<OrderRow>.Create(x => x.Id == holder.Current);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Db.Orders.Where(x => condition.Invoke(x)).ToArrayAsync());
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Empty(fixture.Commands.Executed);
        holder.Throw = false;
        holder.Value = 2;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Db.Orders.Where(x => condition.Invoke(x)).ToArrayAsync(cancellation.Token));
        fixture.Commands.Clear();
        condition = Condition<OrderRow>.Create(x => x.Id == 4 && !x.Active);
        Assert.Equal(new[] { 4 }, await fixture.Db.Orders.Where(x => condition.Invoke(x)).Select(x => x.Id).ToArrayAsync());
        Assert.Contains("= 4", Assert.Single(fixture.Commands.Executed).Sql);
    }

    [Fact]
    public async Task StandaloneInterceptorAcceptsConstantTargetsAndOrdinaryQueries()
    {
        await using var fixture = await SqliteFixture.CreateAsync(false,
            b => b.AddInterceptors(RaffinertExpressionInterceptor.Instance));
        var condition = Condition<OrderRow>.Create(x => x.Id == 4);
        var row = Expression.Parameter(typeof(OrderRow), "row");
        var predicate = Expression.Lambda<Func<OrderRow, bool>>(
            Expression.Call(Expression.Constant(condition, typeof(Condition<OrderRow>)),
                typeof(ComposableExpression<OrderRow, bool>).GetMethod(nameof(condition.Invoke))!, row), row);
        Assert.Equal(new[] { 4 }, await fixture.Db.Orders.Where(predicate).Select(x => x.Id).ToArrayAsync());
        Assert.Equal(2, await fixture.Db.Orders.CountAsync(x => x.Active));
        Assert.Equal(2, fixture.Commands.Executed.Count);
    }

    [Fact]
    public async Task ReentrantGetterFailureIsUnsupportedAndNextQueryRecovers()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var holder = new ReentrantHolder(fixture.Db);
        var condition = Condition<OrderRow>.Create(x => x.Id == holder.Current);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Db.Orders.Where(x => condition.Invoke(x)).ToArrayAsync());
        Assert.Contains("Unable to read closure member", error.Message);
        Assert.Equal(1, holder.Reads);
        // The nested ordinary query executes; the controlled getter then fails, without recursion.
        Assert.Equal(4, holder.NestedCount);
        Assert.Single(fixture.Commands.Executed);
        fixture.Commands.Clear();
        var valid = Condition<OrderRow>.Create(x => x.Id == 2);
        Assert.Equal(new[] { 2 }, await fixture.Db.Orders.Where(x => valid.Invoke(x)).Select(x => x.Id).ToArrayAsync());
        Assert.Single(fixture.Commands.Executed);
    }

    [Fact]
    public async Task MissingRecordedStateFailsCapturedMarkersButAllowsOtherModes()
    {
        await using var fixture = await SqliteFixture.CreateAsync(false);
        // Deliberately discard the helper's factory decorator using only public EF services.
        // This unsupported configuration deterministically leaves its state empty; no GC forcing.
        var builder = new DbContextOptionsBuilder<OrdersContext>(fixture.Options).UseRaffinertExpressions();
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
            new MissingStateExtension(fixture.Db.GetService<IQueryContextFactory>()));
        await using var broken = new OrdersContext(builder.Options);
        var condition = Condition<OrderRow>.Create(x => x.Active);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            broken.Orders.Where(x => condition.Invoke(x)).ToArrayAsync());
        Assert.Contains("Unable to resolve expression instance", error.Message);
        Assert.Empty(fixture.Commands.Executed);
        Assert.Equal(4, await broken.Orders.CountAsync());
        Assert.Equal(2, await broken.Orders.CountAsync(condition));
        var compiled = EF.CompileQuery((OrdersContext db) => db.Orders.Count(x => condition.Invoke(x)));
        Assert.Equal(2, compiled(broken));
        Assert.Equal(3, fixture.Commands.Executed.Count);
    }

    private sealed class MissingStateExtension(IQueryContextFactory factory) : IDbContextOptionsExtension
    {
        public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);
        public void Validate(IDbContextOptions options) { }
        public void ApplyServices(IServiceCollection services)
        {
            services.Remove(services.Last(x => x.ServiceType == typeof(IQueryContextFactory)));
            services.AddScoped<IQueryContextFactory>(_ => factory);
        }

        private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
        {
            public override bool IsDatabaseProvider => false;
            public override string LogFragment => "MissingStateTest ";
            public override int GetServiceProviderHashCode() => 0;
            public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => false;
            public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) => debugInfo["Tests:MissingState"] = "1";
        }
    }

    private sealed class ScalarHolder
    {
        public int Value { get; set; }
        public bool Throw { get; set; }
        public Queue<int>? Sequence { get; set; }
        public List<int> Reads { get; } = [];
        public int Current
        {
            get
            {
                if (Throw) throw new InvalidOperationException("Synthetic getter failure.");
                var value = Sequence?.Dequeue() ?? Value;
                Reads.Add(value);
                return value;
            }
        }
    }

    private sealed class ReentrantHolder(OrdersContext db)
    {
        public int Reads { get; private set; }
        public int NestedCount { get; private set; }
        public int Current
        {
            get
            {
                Reads++;
                NestedCount = db.Orders.Count();
                throw new InvalidOperationException("Synthetic failure after unsupported nested query.");
            }
        }
    }
}
