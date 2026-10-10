using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;
#if SQLSERVER_TESTS
using Fixture = Raffinert.Expressions.EntityFrameworkCore.SqlServerTests.LocalDbFixture;
using Row = Raffinert.Expressions.EntityFrameworkCore.SqlServerTests.SqlOrderRow;
namespace Raffinert.Expressions.EntityFrameworkCore.SqlServerTests;
#else
using Fixture = Raffinert.Expressions.EntityFrameworkCore.IntegrationTests.SqliteFixture;
using Row = Raffinert.Expressions.EntityFrameworkCore.IntegrationTests.OrderRow;
namespace Raffinert.Expressions.EntityFrameworkCore.IntegrationTests;
#endif

// Linked into the LocalDB project: identical behavioral assertions, real provider-specific fixtures.
public class ExplicitEfParameterizationTests(ITestOutputHelper output)
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
    [InlineData("automatic")]
    [InlineData("constant")]
    [InlineData("parameter")]
    public async Task CapturedScalarMatchesDirectEfAcrossChanges(string mode)
    {
        await using var fixture = await CreateAsync();
        var threshold = 1000;
        var condition = mode switch
        {
            "constant" => Condition<Row>.Create(x => x.TotalCents > EF.Constant(threshold)),
            "parameter" => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(threshold)),
            _ => Condition<Row>.Create(x => x.TotalCents > threshold)
        };
        var control = mode switch
        {
            "constant" => fixture.Db.Orders.Where(x => x.TotalCents > EF.Constant(threshold)),
            "parameter" => fixture.Db.Orders.Where(x => x.TotalCents > EF.Parameter(threshold)),
            _ => fixture.Db.Orders.Where(x => x.TotalCents > threshold)
        };
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        var before = fixture.QueryCompilations;
        foreach (var value in new[] { 1000, 10000, 1000 })
        {
            threshold = value;
            var expected = await control.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
            var native = fixture.Commands.Executed.Last();
            Assert.Equal(value == 1000 ? new[] { 2, 3, 4 } : new[] { 2 }, expected);
            Assert.Equal(expected, await query.ToArrayAsync());
            var embedded = fixture.Commands.Executed.Last();
            if (mode == "constant")
            {
                Assert.Empty(native.Values);
                Assert.Empty(embedded.Values);
                Assert.Equal(native.Sql, embedded.Sql);
            }
            else
            {
                Assert.Equal(value, Assert.Single(native.Values));
                Assert.Equal(value, Assert.Single(embedded.Values));
                Assert.Equal("__raffinert_threshold_0", Assert.Single(embedded.Names));
                Assert.DoesNotContain(value.ToString(), embedded.Sql);
            }
        }
        output.WriteLine($"{mode}: {fixture.QueryCompilations - before} combined native/embedded compilations; values omitted.");
        output.WriteLine(fixture.Commands.Executed.Last().Sql);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiteralDirectiveMatchesDirectEf(bool constant)
    {
        await using var fixture = await CreateAsync();
        var condition = constant ? Condition<Row>.Create(x => x.TotalCents > EF.Constant(1000))
            : Condition<Row>.Create(x => x.TotalCents > EF.Parameter(1000));
        var control = constant ? fixture.Db.Orders.Where(x => x.TotalCents > EF.Constant(1000))
            : fixture.Db.Orders.Where(x => x.TotalCents > EF.Parameter(1000));
        var expected = await control.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        var native = fixture.Commands.Executed.Last();
        Assert.Equal(new[] { 2, 3, 4 }, expected);
        Assert.Equal(expected, await fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());
        var embedded = fixture.Commands.Executed.Last();
        Assert.Equal(native.Values, embedded.Values);
        if (constant) Assert.Equal(native.Sql, embedded.Sql);
        else
        {
            Assert.Equal(1000, Assert.Single(embedded.Values));
            Assert.Equal("__raffinert_p_0", Assert.Single(embedded.Names));
            Assert.DoesNotContain("1000", embedded.Sql);
        }
    }

    [Fact]
    public async Task MixedAndNestedWrappersMatchFlatEfAndReassignment()
    {
        await using var fixture = await CreateAsync();
        var minimum = 1000;
        var maximum = 4;
        var inner = Condition<Row>.Create(x => x.TotalCents > EF.Constant(minimum) && x.Id < EF.Parameter(maximum));
        var outer = Condition<Row>.Create(x => inner.Invoke(x));
        var query = fixture.Db.Orders.Where(x => outer.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        foreach (var value in new[] { 1000, 10000, 1000 })
        {
            minimum = value;
            var expected = await fixture.Db.Orders.Where(x => x.TotalCents > EF.Constant(minimum) && x.Id < EF.Parameter(maximum))
                .OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
            Assert.Equal(expected, await query.ToArrayAsync());
            var command = fixture.Commands.Executed.Last();
            Assert.Equal(maximum, Assert.Single(command.Values));
            Assert.Equal("__raffinert_maximum_0", Assert.Single(command.Names));
        }
        outer = Condition<Row>.Create(x => x.TotalCents < EF.Constant(minimum) && x.Id < EF.Parameter(maximum));
        Assert.Equal(new[] { 1 }, await query.ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullableDirectivesMatchNativeValueNullValue(bool constant)
    {
        await using var fixture = await CreateAsync();
        int? customerId = 1;
        var condition = constant ? Condition<Row>.Create(x => x.CustomerId == EF.Constant(customerId))
            : Condition<Row>.Create(x => x.CustomerId == EF.Parameter(customerId));
        var control = constant ? fixture.Db.Orders.Where(x => x.CustomerId == EF.Constant(customerId))
            : fixture.Db.Orders.Where(x => x.CustomerId == EF.Parameter(customerId));
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        foreach (var value in new int?[] { 1, null, 2 })
        {
            customerId = value;
            var expected = await control.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
            var native = fixture.Commands.Executed.Last();
            Assert.Equal(value == 1 ? new[] { 1, 2 } : value == 2 ? new[] { 4 } : new[] { 3 }, expected);
            Assert.Equal(expected, await query.ToArrayAsync());
            var embedded = fixture.Commands.Executed.Last();
            Assert.Equal(native.Values, embedded.Values);
            if (constant) Assert.Equal(native.Sql, embedded.Sql);
            else if (embedded.Names.Length > 0) Assert.Equal("__raffinert_customerId_0", Assert.Single(embedded.Names));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectWrapperOverloadRetainsNativeDirectiveBehavior(bool constant)
    {
        await using var fixture = await CreateAsync();
        var threshold = 1000;
        var condition = constant ? Condition<Row>.Create(x => x.TotalCents > EF.Constant(threshold))
            : Condition<Row>.Create(x => x.TotalCents > EF.Parameter(threshold));
        foreach (var value in new[] { 1000, 10000, 1000 })
        {
            threshold = value;
            Assert.Equal(value == 1000 ? 3 : 1, await fixture.Db.Orders.Where(condition).CountAsync());
            var command = fixture.Commands.Executed.Last();
            if (constant) Assert.Empty(command.Values);
            else Assert.Equal(value, Assert.Single(command.Values));
        }
    }

    [Fact]
    public async Task ExplicitParameterKeepsSyntheticStringPrivateAndConstantOnlyDiagnosticsWork()
    {
        await using var fixture = await CreateAsync();
        var customerEmail = "synthetic-ef10-directive-private@example.invalid";
        var condition = Condition<Row>.Create(x => x.Name == EF.Parameter(customerEmail));
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x));
        foreach (var value in new[] { customerEmail, "Desk", customerEmail })
        {
            customerEmail = value;
            var native = await fixture.Db.Orders.Where(x => x.Name == EF.Parameter(customerEmail)).Select(x => x.Id).ToArrayAsync();
            Assert.Equal(native, await query.Select(x => x.Id).ToArrayAsync());
            var command = fixture.Commands.Executed.Last();
            Assert.Equal(customerEmail, Assert.Single(command.Values));
            Assert.DoesNotContain(customerEmail, command.Sql);
        }
        Assert.DoesNotContain(customerEmail, Assert.Throws<NotSupportedException>(() => query.ToQueryString()).ToString());
        var threshold = 1000;
        var constant = Condition<Row>.Create(x => x.TotalCents > EF.Constant(threshold));
        Assert.Contains("1000", fixture.Db.Orders.Where(x => constant.Invoke(x)).ToQueryString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RowDependentDirectivesFailBeforeSql(bool constant)
    {
        await using var fixture = await CreateAsync();
        var control = constant ? fixture.Db.Orders.Where(x => x.Id == EF.Constant(x.TotalCents))
            : fixture.Db.Orders.Where(x => x.Id == EF.Parameter(x.TotalCents));
        await Assert.ThrowsAsync<InvalidOperationException>(() => control.ToArrayAsync());
        var condition = constant ? Condition<Row>.Create(x => x.Id == EF.Constant(x.TotalCents))
            : Condition<Row>.Create(x => x.Id == EF.Parameter(x.TotalCents));
        var error = await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Db.Orders.Where(x => condition.Invoke(x)).ToArrayAsync());
        Assert.Contains("operand", error.Message);
        Assert.Null(error.InnerException);
        Assert.Empty(fixture.Commands.Executed);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NestedDirectiveOperandsAreRejectedLikeNativeEf(bool outerConstant, bool innerConstant)
    {
        await using var fixture = await CreateAsync();
        var threshold = 1000;
        var condition = (outerConstant, innerConstant) switch
        {
            (true, true) => Condition<Row>.Create(x => x.TotalCents > EF.Constant(EF.Constant(threshold))),
            (true, false) => Condition<Row>.Create(x => x.TotalCents > EF.Constant(EF.Parameter(threshold))),
            (false, true) => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(EF.Constant(threshold))),
            _ => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(EF.Parameter(threshold)))
        };
        var control = (outerConstant, innerConstant) switch
        {
            (true, true) => fixture.Db.Orders.Where(x => x.TotalCents > EF.Constant(EF.Constant(threshold))),
            (true, false) => fixture.Db.Orders.Where(x => x.TotalCents > EF.Constant(EF.Parameter(threshold))),
            (false, true) => fixture.Db.Orders.Where(x => x.TotalCents > EF.Parameter(EF.Constant(threshold))),
            _ => fixture.Db.Orders.Where(x => x.TotalCents > EF.Parameter(EF.Parameter(threshold)))
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => control.ToArrayAsync());
        var error = await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Db.Orders.Where(x => condition.Invoke(x)).ToArrayAsync());
        Assert.Contains("operand", error.Message);
        Assert.Null(error.InnerException);
        Assert.Empty(fixture.Commands.Executed);
    }

    [Fact]
    public async Task ComputedOperandIsRejectedWithoutReadingGetters()
    {
        await using var fixture = await CreateAsync();
        var holder = new Holder();
        var condition = Condition<Row>.Create(x => x.TotalCents > EF.Parameter(holder.Value + 1));
        var error = await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Db.Orders.Where(x => condition.Invoke(x)).ToArrayAsync());
        Assert.Contains("operand", error.Message);
        Assert.Equal(0, holder.Reads);
        Assert.Null(error.InnerException);
        Assert.Empty(fixture.Commands.Executed);
    }

    [Fact]
    public async Task DirectiveGetterReadsOnceAndFailuresStaySanitized()
    {
        await using var fixture = await CreateAsync();
        var holder = new Holder();
        var condition = Condition<Row>.Create(x => x.TotalCents > EF.Parameter(holder.Value));
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x));
        Assert.Equal(3, await query.CountAsync());
        Assert.Equal(3, await query.CountAsync());
        Assert.Equal(2, holder.Reads);
        holder.Throw = true;
        fixture.Commands.Executed.Clear();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => query.ToArrayAsync());
        Assert.DoesNotContain(Holder.Marker, error.ToString());
        Assert.Null(error.InnerException);
        Assert.Empty(fixture.Commands.Executed);
    }

    [Fact]
    public Task ParameterProjectionMatchesNativeAcrossChanges() => ProjectionMatchesNativeAcrossChanges(false);

    [Fact]
    public Task ConstantProjectionMatchesNativeAcrossChanges() => ProjectionMatchesNativeAcrossChanges(true);

    private async Task ProjectionMatchesNativeAcrossChanges(bool constant)
    {
        await using var fixture = await CreateAsync();
        var threshold = 1000;
        var projection = constant
            ? Projection<Row>.Create(x => new { x.Id, IsExpensive = x.TotalCents > EF.Constant(threshold) })
            : Projection<Row>.Create(x => new { x.Id, IsExpensive = x.TotalCents > EF.Parameter(threshold) });
        var native = constant
            ? fixture.Db.Orders.Select(x => new { x.Id, IsExpensive = x.TotalCents > EF.Constant(threshold) }).OrderBy(x => x.Id)
            : fixture.Db.Orders.Select(x => new { x.Id, IsExpensive = x.TotalCents > EF.Parameter(threshold) }).OrderBy(x => x.Id);
        var embedded = fixture.Db.Orders.Select(x => projection.Invoke(x)).OrderBy(x => x.Id);
        var nativeCompilations = 0;
        var embeddedCompilations = 0;
        foreach (var value in new[] { 1000, 10000, 1000 })
        {
            threshold = value;
            var before = fixture.QueryCompilations;
            var expected = await native.ToArrayAsync();
            nativeCompilations += fixture.QueryCompilations - before;
            var nativeCommand = fixture.Commands.Executed.Last();
            Assert.Equal(new[] { 1, 2, 3, 4 }, expected.Select(x => x.Id));
            Assert.Equal(value == 1000 ? new[] { false, true, true, true } : new[] { false, true, false, false },
                expected.Select(x => x.IsExpensive));
            before = fixture.QueryCompilations;
            Assert.Equal(expected, await embedded.ToArrayAsync());
            embeddedCompilations += fixture.QueryCompilations - before;
            var embeddedCommand = fixture.Commands.Executed.Last();
            if (constant)
            {
                Assert.Empty(nativeCommand.Values);
                Assert.Empty(embeddedCommand.Values);
                Assert.Matches(@">\s*" + value + @"\b", nativeCommand.Sql);
                Assert.Matches(@">\s*" + value + @"\b", embeddedCommand.Sql);
            }
            else
            {
                Assert.Equal(value, Assert.Single(nativeCommand.Values));
                Assert.Equal(value, Assert.Single(embeddedCommand.Values));
                Assert.Equal("__raffinert_threshold_0", Assert.Single(embeddedCommand.Names));
                Assert.DoesNotContain(value.ToString(), embeddedCommand.Sql);
            }
        }
        output.WriteLine($"Projection {(constant ? "constant" : "parameter")}: native compilations {nativeCompilations}; embedded compilations {embeddedCompilations}; bindings omitted.");
        if (constant)
            Assert.Matches(@">\s*1000\b", embedded.ToQueryString());
        else
        {
            var error = Assert.Throws<NotSupportedException>(() => embedded.ToQueryString());
            Assert.DoesNotContain(threshold.ToString(), error.ToString());
            Assert.Null(error.InnerException);
        }
    }

    private sealed class Holder
    {
        public const string Marker = "synthetic-ef10-getter@example.invalid";
        public int Reads { get; private set; }
        public bool Throw { get; set; }
        public int Value { get { Reads++; if (Throw) throw new InvalidOperationException(Marker); return 1000; } }
    }
}
