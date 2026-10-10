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
        var condition = Condition<Row>.Create(x => x.TotalCents > EF.Parameter(holder.Value + ArbitraryMethod()));
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

    [Fact]
    public async Task ComputedParameterMatchesNativeAcrossChanges()
    {
        await using var fixture = await CreateAsync();
        var threshold = 1000;
        var condition = Condition<Row>.Create(x => x.TotalCents > EF.Parameter(threshold + 100));
        var native = fixture.Db.Orders.Where(x => x.TotalCents > EF.Parameter(threshold + 100)).OrderBy(x => x.Id).Select(x => x.Id);
        var embedded = fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        var expected = new List<int[]>();
        foreach (var value in new[] { 1000, 10000, 1000 })
        {
            threshold = value;
            expected.Add(await native.ToArrayAsync());
            Assert.Equal(value + 100, Assert.Single(fixture.Commands.Executed.Last().Values));
        }
        var before = fixture.QueryCompilations;
        fixture.Commands.Executed.Clear();
        var index = 0;
        foreach (var value in new[] { 1000, 10000, 1000 })
        {
            threshold = value;
            Assert.Equal(expected[index++], await embedded.ToArrayAsync());
            var command = fixture.Commands.Executed.Last();
            Assert.Equal(value + 100, Assert.Single(command.Values));
            Assert.Equal("__raffinert_computed_0", Assert.Single(command.Names));
            Assert.DoesNotContain((value + 100).ToString(), command.Sql);
        }
        Assert.Equal(1, fixture.QueryCompilations - before);
        Assert.Single(fixture.Commands.Executed.Select(x => x.Sql).Distinct());
        Assert.Throws<NotSupportedException>(() => embedded.ToQueryString());
        output.WriteLine("Computed parameter: native controls passed; embedded one compilation, one SQL shape, current A/B/A bindings.");
    }

    [Fact]
    public async Task ComputedConstantMatchesNativeAcrossChanges()
    {
        await using var fixture = await CreateAsync();
        var settings = new Settings { MinPrice = 500 };
        var condition = Condition<Row>.Create(x => x.TotalCents > EF.Constant(settings.MinPrice * 2));
        var native = fixture.Db.Orders.Where(x => x.TotalCents > EF.Constant(settings.MinPrice * 2)).OrderBy(x => x.Id).Select(x => x.Id);
        var embedded = fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        var controls = new List<(int[] Results, string Sql)>();
        var before = fixture.QueryCompilations;
        foreach (var value in new[] { 500, 5000, 500 })
        {
            settings.MinPrice = value;
            var results = await native.ToArrayAsync();
            var command = fixture.Commands.Executed.Last();
            Assert.Empty(command.Values);
            Assert.Matches(@">\s*" + value * 2 + @"\b", command.Sql);
            controls.Add((results, command.Sql));
        }
        var nativeCompilations = fixture.QueryCompilations - before;
        before = fixture.QueryCompilations;
        var index = 0;
        foreach (var value in new[] { 500, 5000, 500 })
        {
            settings.MinPrice = value;
            var control = controls[index++];
            Assert.Equal(control.Results, await embedded.ToArrayAsync());
            var command = fixture.Commands.Executed.Last();
            Assert.Empty(command.Values);
            Assert.Equal(control.Sql, command.Sql);
        }
        Assert.Matches(@">\s*1000\b", embedded.ToQueryString());
        output.WriteLine($"Computed constant: native compilations {nativeCompilations}; embedded compilations {fixture.QueryCompilations - before}; current literals match native.");
    }

    [Theory]
    [InlineData("add")]
    [InlineData("add-checked")]
    [InlineData("subtract")]
    [InlineData("subtract-checked")]
    [InlineData("multiply")]
    [InlineData("multiply-checked")]
    [InlineData("convert")]
    [InlineData("convert-checked")]
    [InlineData("short-result")]
    [InlineData("long-checked")]
    [InlineData("static-field")]
    public async Task ComputedNumericOperationsMatchNative(string operation)
    {
        await using var fixture = await CreateAsync();
        var threshold = 1000;
        var condition = operation switch
        {
            "add" => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(threshold + 100)),
            "add-checked" => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(checked(threshold + 100))),
            "subtract" => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(threshold - 100)),
            "subtract-checked" => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(checked(threshold - 100))),
            "multiply" => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(threshold * 2)),
            "multiply-checked" => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(checked(threshold * 2))),
            "convert" => Condition<Row>.Create(x => x.TotalCents > EF.Parameter((long)threshold + 100L)),
            "short-result" => Condition<Row>.Create(x => x.TotalCents > EF.Parameter((short)(threshold + 100))),
            "long-checked" => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(checked((long)threshold + 100L))),
            "static-field" => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(StaticThreshold + 100)),
            _ => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(checked((short)threshold) + 100))
        };
        // The direct overload expands before EF extraction: this is the native control.
        var controls = new List<(int[] Rows, object? Value)>();
        foreach (var value in new[] { 1000, 10000, 1000 })
        {
            threshold = value;
            var rows = await fixture.Db.Orders.Where(condition).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
            controls.Add((rows, Assert.Single(fixture.Commands.Executed.Last().Values)));
        }
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        var index = 0;
        foreach (var value in new[] { 1000, 10000, 1000 })
        {
            threshold = value;
            var control = controls[index++];
            Assert.Equal(control.Rows, await query.ToArrayAsync());
            Assert.Equal(control.Value, Assert.Single(fixture.Commands.Executed.Last().Values));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComputedNullableConversionMatchesNative(bool constant)
    {
        await using var fixture = await CreateAsync();
        int? customer = 0;
        var condition = constant ? Condition<Row>.Create(x => (long?)x.CustomerId == EF.Constant((long?)(customer + 1)))
            : Condition<Row>.Create(x => (long?)x.CustomerId == EF.Parameter((long?)(customer + 1)));
        var controls = new List<(int[] Rows, object?[] Values)>();
        foreach (var value in new int?[] { 0, null, 1 })
        {
            customer = value;
            var rows = await fixture.Db.Orders.Where(condition).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
            controls.Add((rows, fixture.Commands.Executed.Last().Values));
        }
        var index = 0;
        foreach (var value in new int?[] { 0, null, 1 })
        {
            customer = value;
            var control = controls[index++];
            Assert.Equal(control.Rows, await fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());
            Assert.Equal(control.Values, fixture.Commands.Executed.Last().Values);
        }
    }

    [Fact]
    public async Task ComputedMixedNestedProjectionAndReassignmentMatchNative()
    {
        await using var fixture = await CreateAsync();
        var threshold = 1000;
        var maximum = 4;
        var inner = Condition<Row>.Create(x => x.TotalCents > EF.Parameter(threshold + 100) && x.Id < EF.Constant(maximum));
        var outer = Condition<Row>.Create(x => inner.Invoke(x));
        var query = fixture.Db.Orders.Where(x => outer.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        var projection = Projection<Row>.Create(x => new { x.Id, Match = inner.Invoke(x) });
        foreach (var value in new[] { 1000, 10000, 1000 })
        {
            threshold = value;
            var expected = await fixture.Db.Orders.Where(inner).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
            Assert.Equal(expected, await query.ToArrayAsync());
            var expectedProjection = await fixture.Db.Orders.Select(projection).OrderBy(x => x.Id).ToArrayAsync();
            Assert.Equal(expectedProjection, await fixture.Db.Orders.Select(x => projection.Invoke(x)).OrderBy(x => x.Id).ToArrayAsync());
        }
        outer = Condition<Row>.Create(x => x.TotalCents < EF.Parameter(threshold + 100) && x.Id < EF.Constant(maximum));
        Assert.Equal(new[] { 1 }, await query.ToArrayAsync());
    }

    [Fact]
    public async Task ComputedPrivacyAndRepeatedGetterReadsSurviveCacheHits()
    {
        await using var fixture = await CreateAsync();
        var holder = new Holder();
        var secret = Holder.Marker;
        var condition = Condition<Row>.Create(x => x.TotalCents > EF.Parameter(holder.Value + 100)
            && x.TotalCents > EF.Parameter(holder.Value + 100) && x.Name != EF.Parameter(secret));
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x));
        for (var i = 1; i <= 3; i++)
        {
            Assert.Equal(3, await query.CountAsync());
            Assert.Equal(i * 2, holder.Reads);
            var command = fixture.Commands.Executed.Last();
            Assert.Contains(secret, command.Values);
            Assert.Equal(2, command.Values.Count(x => Equals(x, 1100)));
            Assert.Contains("__raffinert_computed_0", command.Names);
            Assert.Contains("__raffinert_computed_1", command.Names);
            Assert.DoesNotContain(secret, command.Sql);
        }
        Assert.Equal(1, fixture.QueryCompilations);
        var diagnostic = Assert.Throws<NotSupportedException>(() => query.ToQueryString());
        Assert.DoesNotContain(secret, diagnostic.ToString());
        holder.Throw = true;
        fixture.Commands.Executed.Clear();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => query.CountAsync());
        Assert.DoesNotContain(secret, error.ToString());
        Assert.Null(error.InnerException);
        Assert.Empty(fixture.Commands.Executed);
        holder.Throw = false;
        Assert.Equal(3, await query.CountAsync());
    }

    [Theory]
    [InlineData("add")]
    [InlineData("subtract")]
    [InlineData("multiply")]
    [InlineData("convert")]
    public async Task ComputedCheckedOverflowIsSanitizedAndRecovers(string operation)
    {
        await using var fixture = await CreateAsync();
        var threshold = operation == "subtract" ? int.MinValue : int.MaxValue;
        var condition = operation switch
        {
            "add" => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(checked(threshold + 100))),
            "subtract" => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(checked(threshold - 100))),
            "multiply" => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(checked(threshold * 2))),
            _ => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(checked((short)threshold)))
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Db.Orders.Where(condition).ToArrayAsync());
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => query.ToArrayAsync());
        Assert.Null(error.InnerException);
        Assert.Empty(fixture.Commands.Executed);
        threshold = 1000;
        Assert.Equal(await fixture.Db.Orders.Where(condition).CountAsync(), await query.CountAsync());
    }

    [Fact]
    public async Task ComputedUncheckedOverflowMatchesNative()
    {
        await using var fixture = await CreateAsync();
        var threshold = int.MaxValue;
        var condition = Condition<Row>.Create(x => x.TotalCents > EF.Parameter(unchecked(threshold + 100)));
        var expected = await fixture.Db.Orders.Where(condition).CountAsync();
        var nativeValue = Assert.Single(fixture.Commands.Executed.Last().Values);
        Assert.Equal(expected, await fixture.Db.Orders.CountAsync(x => condition.Invoke(x)));
        Assert.Equal(nativeValue, Assert.Single(fixture.Commands.Executed.Last().Values));
        var narrowing = Condition<Row>.Create(x => x.TotalCents > EF.Parameter(unchecked((short)threshold)));
        expected = await fixture.Db.Orders.Where(narrowing).CountAsync();
        nativeValue = Assert.Single(fixture.Commands.Executed.Last().Values);
        Assert.Equal(expected, await fixture.Db.Orders.CountAsync(x => narrowing.Invoke(x)));
        Assert.Equal(nativeValue, Assert.Single(fixture.Commands.Executed.Last().Values));
    }

    [Fact]
    public async Task ComputedNamesAvoidOuterEfCollision()
    {
        await using var fixture = await CreateAsync();
        var threshold = 1000;
        var __raffinert_computed_0 = 4;
        var condition = Condition<Row>.Create(x => x.TotalCents > EF.Parameter(threshold + 100));
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x) && x.Id < __raffinert_computed_0);
        Assert.Equal(2, await query.CountAsync());
        var command = fixture.Commands.Executed.Last();
        Assert.Contains("__raffinert_computed_0", command.Names);
        Assert.Contains("__raffinert_computed_1", command.Names);
        Assert.Equal(command.Names.Length, command.Names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(1100, command.Values);
        Assert.Contains(4, command.Values);
        threshold = 10000;
        Assert.Equal(1, await query.CountAsync());
        Assert.Contains(10100, fixture.Commands.Executed.Last().Values);
        Assert.Equal(1, fixture.QueryCompilations);
    }

    [Fact]
    public async Task ComputedPreparationRecoversAfterTranslationFailureAndCancellation()
    {
        await using var fixture = await CreateAsync();
        var threshold = 1000;
        var condition = Condition<Row>.Create(x => x.TotalCents > EF.Parameter(threshold + 100));
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x));
        await Assert.ThrowsAsync<InvalidOperationException>(() => query.Where(x => UnsupportedRow(x.Id)).ToArrayAsync());
        Assert.Empty(fixture.Commands.Executed);
        Assert.Equal(3, await query.CountAsync());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.ToArrayAsync(canceled.Token));
        threshold = 10000;
        Assert.Equal(1, await query.CountAsync());
        Assert.Equal(10100, Assert.Single(fixture.Commands.Executed.Last().Values));
    }

    [Theory]
    [InlineData("row")]
    [InlineData("constructor")]
    [InlineData("conversion")]
    [InlineData("operator")]
    [InlineData("conditional")]
    [InlineData("nested")]
    [InlineData("static-property")]
    public async Task UnsupportedComputedShapesRejectBeforeGettersAndRecover(string shape)
    {
        await using var fixture = await CreateAsync();
        var holder = new Holder();
        var custom = new CustomNumber();
        var selectGetter = true;
        var condition = shape switch
        {
            "row" => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(holder.Value + x.Id)),
            "constructor" => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(holder.Value + new Holder().Value)),
            "conversion" => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(holder.Value + (int)custom)),
            "operator" => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(holder.Value + custom)),
            "conditional" => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(selectGetter ? holder.Value : ArbitraryMethod())),
            "nested" => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(holder.Value + EF.Constant(100))),
            _ => Condition<Row>.Create(x => x.TotalCents > EF.Parameter(holder.Value + DateTime.Now.Day))
        };
        var error = await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Db.Orders.Where(x => condition.Invoke(x)).ToArrayAsync());
        Assert.Equal(0, holder.Reads);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(Holder.Marker, error.ToString());
        Assert.Empty(fixture.Commands.Executed);
        var valid = Condition<Row>.Create(x => x.TotalCents > EF.Parameter(holder.Value + 100));
        Assert.Equal(3, await fixture.Db.Orders.CountAsync(x => valid.Invoke(x)));
    }

    private static int ArbitraryMethod() => throw new InvalidOperationException(Holder.Marker);
    private static bool UnsupportedRow(int value) => throw new InvalidOperationException(Holder.Marker);
    private static readonly int StaticThreshold = 1000;
    private sealed class Settings { public int MinPrice { get; set; } }
    private sealed class CustomNumber
    {
        public static explicit operator int(CustomNumber value) => throw new InvalidOperationException(Holder.Marker);
        public static int operator +(int left, CustomNumber right) => throw new InvalidOperationException(Holder.Marker);
    }

    private sealed class Holder
    {
        public const string Marker = "synthetic-ef10-getter@example.invalid";
        public int Reads { get; private set; }
        public bool Throw { get; set; }
        public int Value { get { Reads++; if (Throw) throw new InvalidOperationException(Marker); return 1000; } }
    }
}
