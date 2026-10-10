using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;
using Microsoft.Extensions.DependencyInjection;
#if SQLSERVER_TESTS
using Fixture = Raffinert.Expressions.EntityFrameworkCore.SqlServerTests.LocalDbFixture;
using Row = Raffinert.Expressions.EntityFrameworkCore.SqlServerTests.SqlOrderRow;
using Context = Raffinert.Expressions.EntityFrameworkCore.SqlServerTests.LocalDbOrdersContext;
namespace Raffinert.Expressions.EntityFrameworkCore.SqlServerTests;
#else
using Fixture = Raffinert.Expressions.EntityFrameworkCore.IntegrationTests.SqliteFixture;
using Row = Raffinert.Expressions.EntityFrameworkCore.IntegrationTests.OrderRow;
using Context = Raffinert.Expressions.EntityFrameworkCore.IntegrationTests.OrdersContext;
namespace Raffinert.Expressions.EntityFrameworkCore.IntegrationTests;
#endif

// Experimental acceptance suite: native and direct controls execute before the embedded form.
public class NativeExtractionAcceptanceTests(ITestOutputHelper output)
{
    private static Task<Fixture> CreateAsync()
    {
#if SQLSERVER_TESTS
        return Fixture.CreateAsync();
#else
        return Fixture.CreateAsync(configure: b => b.EnableServiceProviderCaching(false));
#endif
    }

    [Theory]
    [InlineData("computed-parameter")]
    [InlineData("computed-constant")]
    [InlineData("decimal")]
    [InlineData("nullable")]
    [InlineData("method")]
    [InlineData("literal-parameter")]
    [InlineData("literal-constant")]
    [InlineData("automatic")]
    public async Task ScalarNativeDirectAndEmbeddedControls(string mode)
    {
        await using var f = await CreateAsync();
        var threshold = 1000;
        int? customer = 1;
        var holder = new ThresholdHolder();
        Expression<Func<Row, bool>> predicate = mode switch
        {
            "computed-parameter" => x => x.TotalCents > EF.Parameter(threshold + 100),
            "computed-constant" => x => x.TotalCents > EF.Constant(threshold * 2),
            "decimal" => x => x.TotalCents > EF.Parameter((decimal)threshold),
            "nullable" => x => x.CustomerId == EF.Parameter(customer + 0),
            "method" => x => x.TotalCents > EF.Parameter(holder.GetThreshold()),
            "literal-parameter" => x => x.TotalCents > EF.Parameter(1000),
            "literal-constant" => x => x.TotalCents > EF.Constant(1000),
            _ => x => x.TotalCents > threshold
        };
        var condition = Condition<Row>.Create(predicate);
        var native = f.Db.Orders.Where(predicate).OrderBy(x => x.Id).Select(x => x.Id);
        var direct = f.Db.Orders.Where(condition).OrderBy(x => x.Id).Select(x => x.Id);
        var embedded = f.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        foreach (var value in new[] { 1000, 10000, 1000 })
        {
            threshold = holder.Threshold = value;
            customer = value == 10000 ? null : 1;
            var expected = await native.ToArrayAsync();
            var command = f.Commands.Executed.Last();
            Assert.Equal(expected, await direct.ToArrayAsync());
            output.WriteLine($"{mode}: native/direct control succeeded; values omitted.");
            Assert.Equal(expected, await embedded.ToArrayAsync());
            Assert.Equal(command.Values, f.Commands.Executed.Last().Values);
            Assert.Equal(command.Sql, f.Commands.Executed.Last().Sql);
        }
        if (mode == "method") Assert.Equal(9, holder.Reads);
    }

    [Theory]
    [InlineData("parameter", false)]
    [InlineData("constant", false)]
    [InlineData("multiple", false)]
    [InlineData("parameter", true)]
    [InlineData("constant", true)]
    [InlineData("multiple", true)]
    public async Task CollectionsMatchNativeAcrossReplacementMutationAndCardinality(string mode, bool list)
    {
        await using var f = await CreateAsync();
        int[] ids = [1, 3];
        List<int> items = [1, 3];
        Expression<Func<Row, bool>> predicate = (mode, list) switch
        {
            ("constant", false) => x => Enumerable.Contains(EF.Constant(ids), x.Id),
            ("parameter", false) => x => Enumerable.Contains(EF.Parameter(ids), x.Id),
            ("multiple", false) => x => Enumerable.Contains(EF.MultipleParameters(ids), x.Id),
            ("constant", true) => x => Enumerable.Contains(EF.Constant(items), x.Id),
            ("parameter", true) => x => Enumerable.Contains(EF.Parameter(items), x.Id),
            _ => x => Enumerable.Contains(EF.MultipleParameters(items), x.Id)
        };
        var condition = Condition<Row>.Create(predicate);
        var native = f.Db.Orders.Where(predicate).OrderBy(x => x.Id).Select(x => x.Id);
        var direct = f.Db.Orders.Where(condition).OrderBy(x => x.Id).Select(x => x.Id);
        var embedded = f.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        foreach (var values in new int[][] { [1, 3], [2, 4], [], [1, 1, 3], [1], Enumerable.Range(1, 12).ToArray(), [1, 3] })
        {
            ids = values;
            items.Clear();
            items.AddRange(values);
            var expected = await native.ToArrayAsync();
            var command = f.Commands.Executed.Last();
            Assert.Equal(values.Distinct().Where(x => x <= 4).Order(), expected);
            Assert.Equal(expected, await direct.ToArrayAsync());
            output.WriteLine($"{mode}/{list}: native/direct control succeeded; bindings omitted.");
            Assert.Equal(expected, await embedded.ToArrayAsync());
            Assert.Equal(command.Values, f.Commands.Executed.Last().Values);
            Assert.Equal(command.Sql, f.Commands.Executed.Last().Sql);
        }
    }

    [Theory]
    [InlineData("like-literal")]
    [InlineData("like-captured")]
    [InlineData("like-computed")]
    [InlineData("property-literal")]
    [InlineData("property-captured")]
    [InlineData("collate-literal")]
    [InlineData("collate-captured")]
    public async Task ProviderFunctionsAndMetadataMatchNative(string mode)
    {
        await using var f = await CreateAsync();
        var pattern = "D%";
        var prefix = "D";
        var property = "TotalCents";
#if SQLSERVER_TESTS
        var collation = "Latin1_General_BIN2";
        const string literalCollation = "Latin1_General_BIN2";
#else
        var collation = "BINARY";
        const string literalCollation = "BINARY";
#endif
        Expression<Func<Row, bool>> predicate = mode switch
        {
            "like-literal" => x => EF.Functions.Like(x.Name, "D%"),
            "like-captured" => x => EF.Functions.Like(x.Name, pattern),
            "like-computed" => x => EF.Functions.Like(x.Name, prefix + "%"),
            "property-literal" => x => EF.Property<int>(x, "TotalCents") > 1000,
            "property-captured" => x => EF.Property<int>(x, property) > 1000,
            "collate-literal" => x => EF.Functions.Collate(x.Name, literalCollation) == "Desk",
            _ => x => EF.Functions.Collate(x.Name, collation) == "Desk"
        };
        var condition = Condition<Row>.Create(predicate);
        var expected = await f.Db.Orders.Where(predicate).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        var command = f.Commands.Executed.Last();
        Assert.Equal(expected, await f.Db.Orders.Where(condition).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());
        output.WriteLine($"{mode}: native/direct control succeeded; values omitted.");
        Assert.Equal(expected, await f.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());
        Assert.Equal(command.Values, f.Commands.Executed.Last().Values);
        Assert.Equal(command.Sql, f.Commands.Executed.Last().Sql);
    }

    [Fact]
    public async Task NativeRowDependentAndNestedDirectivesFailBeforeSql()
    {
        await using var f = await CreateAsync();
        var threshold = 1000;
        foreach (var predicate in new Expression<Func<Row, bool>>[]
        {
            x => x.Id == EF.Parameter(x.TotalCents),
            x => x.Id == EF.Constant(x.TotalCents),
            x => x.TotalCents > EF.Parameter(EF.Constant(threshold))
        })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.Orders.Where(predicate).ToArrayAsync());
            var condition = Condition<Row>.Create(predicate);
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.Orders.Where(x => condition.Invoke(x)).ToArrayAsync());
            Assert.Empty(f.Commands.Executed);
        }
    }

    [Fact]
    public async Task MixedNestedProjectionAndWrapperReassignmentMatchNative()
    {
        await using var f = await CreateAsync();
        var threshold = 1000;
        var upper = 30000;
        var inner = Condition<Row>.Create(x => x.TotalCents > EF.Parameter(threshold + 100));
        var outer = Condition<Row>.Create(x => inner.Invoke(x) && x.TotalCents < EF.Constant(upper * 2));
        var projection = Projection<Row, int>.Create(x => EF.Parameter(threshold + 100) + x.Id);
        var query = f.Db.Orders.Where(x => outer.Invoke(x)).OrderBy(x => x.Id).Select(x => projection.Invoke(x));
        foreach (var value in new[] { 1000, 10000, 1000 })
        {
            threshold = value;
            var expected = await f.Db.Orders.Where(x => x.TotalCents > EF.Parameter(threshold + 100) && x.TotalCents < EF.Constant(upper * 2))
                .OrderBy(x => x.Id).Select(x => EF.Parameter(threshold + 100) + x.Id).ToArrayAsync();
            Assert.Equal(expected, await query.ToArrayAsync());
        }
        outer = Condition<Row>.Create(x => x.Id == 1);
        Assert.Equal(new[] { 1101 }, await query.ToArrayAsync());
    }

    [Fact]
    public async Task DiagnosticPrivacyKeepsNativeAndConstantOnlyRenderingAvailable()
    {
        await using var f = await CreateAsync();
        var secret = "synthetic-native-extraction-private@example.invalid";
        var condition = Condition<Row>.Create(x => x.Name == EF.Parameter(secret));
        var query = f.Db.Orders.Where(x => condition.Invoke(x));
        for (var i = 0; i < 2; i++)
        {
            await query.ToArrayAsync();
            var command = f.Commands.Executed.Last();
            Assert.True(command.Values.Contains(secret), "Private value must be bound; values omitted.");
            Assert.False(command.Sql.Contains(secret, StringComparison.Ordinal), "Executed SQL must omit private value.");
            Assert.True(query.ToQueryString().Contains(secret, StringComparison.Ordinal), "Native diagnostic rendering includes parameter values; values omitted.");
        }
        Assert.True(f.Db.Orders.Where(x => x.Name == secret).ToQueryString().Length > 0);
        var constant = Condition<Row>.Create(x => x.Id == EF.Constant(1));
        Assert.True(f.Db.Orders.Where(x => constant.Invoke(x)).ToQueryString().Length > 0);
    }

    private sealed class ThresholdHolder
    {
        public int Threshold = 1000;
        public int Reads;
        public int GetThreshold() { Reads++; return Threshold; }
    }

    [Theory]
    [InlineData("constant")]
    [InlineData("parameter")]
    [InlineData("multiple")]
    public async Task NullableCollectionAndNullCollectionMatchNative(string mode)
    {
        await using var f = await CreateAsync();
        int?[]? ids = [1, null];
        Expression<Func<Row, bool>> predicate = mode switch
        {
            "constant" => x => Enumerable.Contains(EF.Constant(ids)!, x.CustomerId),
            "parameter" => x => Enumerable.Contains(EF.Parameter(ids)!, x.CustomerId),
            _ => x => Enumerable.Contains(EF.MultipleParameters(ids)!, x.CustomerId)
        };
        var condition = Condition<Row>.Create(predicate);
        foreach (var values in new int?[]?[] { [1, null], [2], [], null, [1, null] })
        {
            ids = values;
            var expected = await f.Db.Orders.Where(predicate).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
            var command = f.Commands.Executed.Last();
            Assert.Equal(expected, await f.Db.Orders.Where(condition).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());
            Assert.Equal(expected, await f.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());
            Assert.Equal(command.Sql, f.Commands.Executed.Last().Sql);
            Assert.Equal(command.Values, f.Commands.Executed.Last().Values);
        }
    }

    [Fact]
    public async Task ProviderSpecificFunctionMatchesNative()
    {
        await using var f = await CreateAsync();
#if SQLSERVER_TESTS
        Expression<Func<Row, bool>> predicate = x => EF.Functions.DateDiffDay(DateTime.Today.AddDays(-x.Id), DateTime.Today) > 1;
#else
        Expression<Func<Row, bool>> predicate = x => EF.Functions.Glob(x.Name, "D*");
#endif
        var condition = Condition<Row>.Create(predicate);
        var expected = await f.Db.Orders.Where(predicate).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        var command = f.Commands.Executed.Last();
        Assert.NotEmpty(expected);
        Assert.Equal(expected, await f.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());
        Assert.Equal(command.Sql, f.Commands.Executed.Last().Sql);
        Assert.Equal(command.Values, f.Commands.Executed.Last().Values);
    }

    [Fact]
    public async Task CurrentBindingsAndNativeCacheShapeAcrossTwentyFiveValuesAndRepeat()
    {
        await using var f = await CreateAsync();
        var threshold = 100;
        var condition = Condition<Row>.Create(x => x.TotalCents > threshold);
        var embedded = f.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        var before = f.QueryCompilations;
        foreach (var value in Enumerable.Range(100, 25).Append(124))
        {
            threshold = value;
            Assert.Equal(new[] { 1, 2, 3, 4 }, await embedded.ToArrayAsync());
            Assert.Equal(value, Assert.Single(f.Commands.Executed.Last().Values));
        }
        Assert.Equal(26, f.Commands.Executed.Count);
        Assert.Single(f.Commands.Executed.Select(x => x.Sql).Distinct());
        Assert.Equal(1, f.QueryCompilations - before);
    }

    [Fact]
    public async Task NativeGetterFailureRemainsNativeAndRecoverable()
    {
        await using var f = await CreateAsync();
        var holder = new FailingHolder();
        var condition = Condition<Row>.Create(x => x.TotalCents > EF.Parameter(holder.GetThreshold()));
        var query = f.Db.Orders.Where(x => condition.Invoke(x));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => query.ToArrayAsync());
        Assert.NotNull(error.InnerException); // EF preserves the user exception; no adapter exception policy after expansion.
        Assert.Empty(f.Commands.Executed);
        holder.Fail = false;
        Assert.Equal(3, await query.CountAsync());
    }

    private sealed class FailingHolder
    {
        public const string Secret = "synthetic-native-getter-private@example.invalid";
        public bool Fail = true;
        public int GetThreshold() => Fail ? throw new InvalidOperationException(Secret) : 1000;
    }

    [Theory]
    [InlineData("automatic")]
    [InlineData("parameter")]
    [InlineData("computed")]
    [InlineData("mixed-projection")]
    public async Task NativeDiagnosticRenderingAndBoundSensitiveValuesMatchControls(string mode)
    {
        await using var f = await CreateAsync();
        var secret = "synthetic-native-diagnostics@example.invalid";
        var suffix = "-computed";
        var ordinary = Condition<Row>.Create(x => x.Name == secret);
        var parameter = Condition<Row>.Create(x => x.Name == EF.Parameter(secret));
        var computed = Condition<Row>.Create(x => x.Name == EF.Parameter(secret + suffix));
        var projection = Projection<Row, bool>.Create(x => x.Name == EF.Parameter(secret) || x.Id == EF.Constant(1));
        for (var i = 0; i < 2; i++)
        {
            if (mode == "mixed-projection")
            {
                var native = await f.Db.Orders.Select(x => x.Name == EF.Parameter(secret) || x.Id == EF.Constant(1)).ToArrayAsync();
                var query = f.Db.Orders.Select(x => projection.Invoke(x));
                Assert.Equal(native, await query.ToArrayAsync());
                Assert.True(query.ToQueryString().Contains(secret, StringComparison.Ordinal));
            }
            else
            {
                var condition = mode == "automatic" ? ordinary : mode == "parameter" ? parameter : computed;
                var query = f.Db.Orders.Where(x => condition.Invoke(x));
                await query.ToArrayAsync();
                Assert.True(query.ToQueryString().Contains(secret, StringComparison.Ordinal));
            }
            var command = f.Commands.Executed.Last();
            Assert.False(command.Sql.Contains(secret, StringComparison.Ordinal));
            Assert.Contains(command.Values.OfType<string>(), value => value.StartsWith(secret, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PooledContextAndFactoryLeasesKeepNativeBindingsAfterFailures(bool factory)
    {
        await using var f = await CreateAsync();
        var services = new ServiceCollection();
        void Configure(DbContextOptionsBuilder builder)
        {
#if SQLSERVER_TESTS
            builder.UseSqlServer(f.Db.Database.GetConnectionString());
#else
            builder.UseSqlite(f.Db.Database.GetDbConnection());
#endif
            builder.UseRaffinertExpressions().AddInterceptors(f.Commands);
        }
        if (factory) services.AddPooledDbContextFactory<Context>(Configure, poolSize: 1);
        else services.AddDbContextPool<Context>(Configure, poolSize: 1);
        await using var provider = services.BuildServiceProvider();
        Context? first = null;
        for (var lease = 0; lease < 6; lease++)
        {
            using var scope = provider.CreateScope();
            await using var db = factory
                ? scope.ServiceProvider.GetRequiredService<IDbContextFactory<Context>>().CreateDbContext()
                : scope.ServiceProvider.GetRequiredService<Context>();
            if (first == null) first = db;
            else Assert.Same(first, db);
            var threshold = lease % 2 == 0 ? 1000 : 10000;
            var condition = Condition<Row>.Create(x => x.TotalCents > EF.Parameter(threshold + 100));
            var query = db.Orders.Where(x => condition.Invoke(x));
            Assert.Equal(lease % 2 == 0 ? 3 : 1, await query.CountAsync());
            Assert.Equal(threshold + 100, Assert.Single(f.Commands.Executed.Last().Values));
            var invalid = Condition<Row>.Create(x => UnmappedRowMethod(x.Id));
            var before = f.Commands.Executed.Count;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.Orders.Where(x => invalid.Invoke(x)).ToArrayAsync());
            Assert.Equal(before, f.Commands.Executed.Count);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.ToArrayAsync(cancelled.Token));
            Assert.Equal(lease % 2 == 0 ? 3 : 1, await query.CountAsync());
            Assert.Equal(threshold + 100, Assert.Single(f.Commands.Executed.Last().Values));
        }
    }

    private static bool UnmappedRowMethod(int id) => id > 0;
}
