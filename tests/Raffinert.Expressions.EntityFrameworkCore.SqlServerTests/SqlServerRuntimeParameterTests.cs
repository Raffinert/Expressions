using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Raffinert.Expressions.EntityFrameworkCore.SqlServerTests;

public class SqlServerRuntimeParameterTests(ITestOutputHelper output)
{
    [Fact]
    public async Task SimpleSelectExecutesAgainstRealLocalDb()
    {
        await using var fixture = await LocalDbFixture.CreateAsync();
        Assert.Equal(new[] { 1, 2, 3, 4 }, await fixture.Db.Orders.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());
        Assert.Single(fixture.Commands.Executed);
        output.WriteLine(fixture.EngineVersion);
        Assert.True(await fixture.DatabaseExistsAsync());
        await fixture.DisposeAsync();
        Assert.False(await fixture.DatabaseExistsAsync());
    }

    [Fact]
    public async Task TwentyFiveChangingThresholdsReuseOneCompilationAndSqlShape()
    {
        await using var fixture = await LocalDbFixture.CreateAsync();
        var threshold = 100;
        var condition = Condition<SqlOrderRow>.Create(x => x.TotalCents > threshold);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x));
        foreach (var value in Enumerable.Range(100, 25).Append(100))
        {
            threshold = value;
            Assert.Equal(4, await query.CountAsync());
            var command = fixture.Commands.Executed.Last();
            Assert.Single(command.Names);
            Assert.Equal(value, Assert.Single(command.Values));
        }
        Assert.Equal(26, fixture.Commands.Executed.Count);
        Assert.Equal(1, fixture.QueryCompilations);
        Assert.Single(fixture.Commands.Executed.Select(x => x.Sql).Distinct());
        output.WriteLine(fixture.Commands.Executed[0].Sql);
        output.WriteLine("25 changing values plus repeat: one compilation, one SQL shape; current bindings verified without logging values.");
    }

    [Fact]
    public async Task CacheHitBindsCurrentValueWithoutStaleData()
    {
        await using var fixture = await LocalDbFixture.CreateAsync();
        var id = 2;
        var condition = Condition<SqlOrderRow>.Create(x => x.Id == id);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x)).Select(x => x.Id);
        foreach (var value in new[] { 2, 4, 2 })
        {
            id = value;
            Assert.Equal(new[] { value }, await query.ToArrayAsync());
            Assert.Equal(value, Assert.Single(fixture.Commands.Executed.Last().Values));
            Assert.Single(fixture.Commands.Executed.Last().Names);
        }
        Assert.Equal(1, fixture.QueryCompilations);
        Assert.Single(fixture.Commands.Executed.Select(x => x.Sql).Distinct());
    }

    [Fact]
    public async Task NestedMemberUsesReadableNameAndOneGetterReadPerExecution()
    {
        await using var fixture = await LocalDbFixture.CreateAsync();
        var settings = new Settings();
        var condition = Condition<SqlOrderRow>.Create(x => x.TotalCents > settings.MinPrice);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        foreach (var value in new[] { 1000, 10000, 1000 })
        {
            settings.Price = value;
            Assert.Equal(value == 1000 ? new[] { 2, 3, 4 } : new[] { 2 }, await query.ToArrayAsync());
            Assert.Equal(value, Assert.Single(fixture.Commands.Executed.Last().Values));
            Assert.Single(fixture.Commands.Executed.Last().Names);
        }
        Assert.Equal(3, settings.Reads);
        Assert.Equal(1, fixture.QueryCompilations);
    }

    [Fact]
    public async Task RepeatedCaptureOccurrencesUseNativeDeduplicationAndBindings()
    {
        await using var fixture = await LocalDbFixture.CreateAsync();
        var threshold = 3;
        var condition = Condition<SqlOrderRow>.Create(x => x.TotalCents > threshold && x.Id < threshold);
        Assert.Equal(new[] { 1, 2 }, await fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());
        var command = Assert.Single(fixture.Commands.Executed);
        Assert.Single(command.Names);
        Assert.Equal(threshold, Assert.Single(command.Values));
    }

    [Fact]
    public async Task OuterEfCaptureAndLiftedCaptureStayIndependent()
    {
        await using var fixture = await LocalDbFixture.CreateAsync();
        var __raffinert_id_0 = 1000;
        var id = 2;
        var condition = Condition<SqlOrderRow>.Create(x => x.Id == id);
        Assert.Equal(new[] { 2 }, await fixture.Db.Orders.Where(x => condition.Invoke(x) && x.TotalCents > __raffinert_id_0)
            .Select(x => x.Id).ToArrayAsync());
        var command = Assert.Single(fixture.Commands.Executed);
        Assert.Equal(2, command.Names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var inner = Array.IndexOf(command.Values, id);
        var outer = Array.IndexOf(command.Values, __raffinert_id_0);
        Assert.True(inner >= 0 && outer >= 0 && inner != outer);
        Assert.NotEqual(command.Names[outer], command.Names[inner]);
        Assert.Contains("[Id] = " + command.CommandNames[inner], command.Sql);
        Assert.Contains("[TotalCents] > " + command.CommandNames[outer], command.Sql);
    }

    [Fact]
    public async Task NullableCaptureCorrectForValueNullValue()
    {
        await using var fixture = await LocalDbFixture.CreateAsync();
        int? customerId = 1;
        var condition = Condition<SqlOrderRow>.Create(x => x.CustomerId == customerId);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        foreach (var value in new int?[] { 1, null, 2, null })
        {
            customerId = value;
            Assert.Equal(value == 1 ? new[] { 1, 2 } : value == 2 ? new[] { 4 } : new[] { 3 }, await query.ToArrayAsync());
            var command = fixture.Commands.Executed.Last();
            if (command.Values.Length > 0)
            {
                Assert.Equal(value, Assert.Single(command.Values));
                Assert.Single(command.Names);
            }
            else Assert.Null(value); // Provider may optimize a null parameter to IS NULL.
        }
        Assert.Equal(1, fixture.QueryCompilations);
    }

    [Fact]
    public async Task NestedConditionsExpandEntirelyOnServer()
    {
        await using var fixture = await LocalDbFixture.CreateAsync();
        var threshold = 1000;
        var active = Condition<SqlOrderRow>.Create(x => x.Active);
        var expensive = Condition<SqlOrderRow>.Create(x => x.TotalCents > threshold);
        var condition = Condition<SqlOrderRow>.Create(x => active.Invoke(x) && expensive.Invoke(x));
        Assert.Equal(new[] { 2 }, await fixture.Db.Orders.Where(x => condition.Invoke(x)).Select(x => x.Id).ToArrayAsync());
        var command = Assert.Single(fixture.Commands.Executed);
        Assert.Single(command.Names);
        Assert.Equal(threshold, Assert.Single(command.Values));
        Assert.Contains("[Active]", command.Sql);
        Assert.DoesNotContain("Invoke", command.Sql);
    }

    [Fact]
    public async Task ReassignedWrapperChangesResultsCorrectly()
    {
        await using var fixture = await LocalDbFixture.CreateAsync();
        var id = 2;
        var condition = Condition<SqlOrderRow>.Create(x => x.Id == id);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        Assert.Equal(new[] { 2 }, await query.ToArrayAsync());
        id = 4;
        condition = Condition<SqlOrderRow>.Create(x => x.Id == id);
        Assert.Equal(new[] { 4 }, await query.ToArrayAsync());
        Assert.Equal(1, fixture.QueryCompilations);
        condition = Condition<SqlOrderRow>.Create(x => x.Id != id);
        Assert.Equal(new[] { 1, 2, 3 }, await query.ToArrayAsync());
        Assert.Equal(2, fixture.QueryCompilations);
    }

    [Fact]
    public async Task CapturedValueToQueryStringUsesNativeRendering()
    {
        await using var fixture = await LocalDbFixture.CreateAsync();
        var customerEmail = "synthetic-localdb-diagnostic@example.invalid";
        var condition = Condition<SqlOrderRow>.Create(x => x.Name == customerEmail);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x));
        Assert.True(query.ToQueryString().Contains(customerEmail, StringComparison.Ordinal));
        Assert.Empty(fixture.Commands.Executed);
        Assert.Contains("SELECT", fixture.Db.Orders.Where(x => x.Id > 1).ToQueryString());
        Assert.Contains("SELECT", fixture.Db.Orders.Where(condition).ToQueryString());
        Assert.Empty(fixture.Commands.Executed);
        Assert.Empty(await query.ToArrayAsync());
        Assert.DoesNotContain(customerEmail, Assert.Single(fixture.Commands.Executed).Sql);
    }

    [Fact]
    public async Task ReferenceCaptureProjectionMatchesNativeControl()
    {
        await using var fixture = await LocalDbFixture.CreateAsync();
        var unsupported = new Uri("https://synthetic-localdb-private.example.invalid/path");
        var projection = Projection<SqlOrderRow>.Create(x => unsupported);
        var expected = await fixture.Db.Orders.Select(x => unsupported).ToArrayAsync();
        Assert.Equal(expected, await fixture.Db.Orders.Select(x => projection.Invoke(x)).ToArrayAsync());
    }

    [Fact]
    public async Task ExplicitCompiledRuntimeCaptureFailsSafely()
    {
        await using var fixture = await LocalDbFixture.CreateAsync();
        var customerEmail = "synthetic-localdb-compiled@example.invalid";
        var condition = Condition<SqlOrderRow>.Create(x => x.Name == customerEmail);
        var compiled = EF.CompileQuery((LocalDbOrdersContext db) => db.Orders.Count(x => condition.Invoke(x)));
        var error = Assert.Throws<NotSupportedException>(() => compiled(fixture.Db));
        Assert.DoesNotContain(customerEmail, error.ToString());
        Assert.Empty(fixture.Commands.Executed);
        var stable = Condition<SqlOrderRow>.Create(x => x.Active);
        var control = EF.CompileQuery((LocalDbOrdersContext db, string name) => db.Orders.Count(x => stable.Invoke(x) && x.Name == name));
        Assert.Equal(1, control(fixture.Db, "Desk"));
        Assert.Equal(0, control(fixture.Db, customerEmail));
        var command = fixture.Commands.Executed.Last();
        Assert.Equal(customerEmail, Assert.Single(command.Values));
        Assert.DoesNotContain(customerEmail, command.Sql);
        Assert.Equal(2, fixture.Commands.Executed.Count);
    }

    [Fact]
    public async Task IndependentContextsDoNotLeakCapturedValues()
    {
        await using var fixture = await LocalDbFixture.CreateAsync();
        var secondCommands = new LocalDbCommandRecorder();
        await using var second = fixture.CreateContext(secondCommands);
        var id = 2;
        var otherId = 4;
        var firstCondition = Condition<SqlOrderRow>.Create(x => x.Id == id);
        var secondCondition = Condition<SqlOrderRow>.Create(x => x.Id == otherId);
        var firstQuery = fixture.Db.Orders.Where(x => firstCondition.Invoke(x)).Select(x => x.Id);
        var secondQuery = second.Orders.Where(x => secondCondition.Invoke(x)).Select(x => x.Id);
        Assert.Equal(new[] { 2 }, await firstQuery.ToArrayAsync());
        Assert.Equal(new[] { 4 }, await secondQuery.ToArrayAsync());
        id = 1;
        Assert.Equal(new[] { 1 }, await firstQuery.ToArrayAsync());
        Assert.Equal(new[] { 4 }, await secondQuery.ToArrayAsync());
        Assert.Equal(new object?[] { 2, 1 }, fixture.Commands.Executed.Select(x => Assert.Single(x.Values)).ToArray());
        Assert.All(secondCommands.Executed, command => Assert.Equal(4, Assert.Single(command.Values)));
    }

    [Fact]
    public async Task FailureOrCancellationDoesNotPoisonNextQuery()
    {
        await using var fixture = await LocalDbFixture.CreateAsync();
        var holder = new ThrowingHolder();
        var failing = Condition<SqlOrderRow>.Create(x => x.Name == holder.Value);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Db.Orders.Where(x => failing.Invoke(x)).ToArrayAsync());
        Assert.NotNull(error.InnerException);
        Assert.Empty(fixture.Commands.Executed);
        var id = 2;
        var condition = Condition<SqlOrderRow>.Create(x => x.Id == id);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x)).Select(x => x.Id);
        Assert.Equal(new[] { 2 }, await query.ToArrayAsync());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.ToArrayAsync(cancellation.Token));
        fixture.Commands.Executed.Clear();
        id = 4;
        using var validToken = new CancellationTokenSource();
        Assert.Equal(new[] { 4 }, await query.ToArrayAsync(validToken.Token));
        var command = Assert.Single(fixture.Commands.Executed);
        Assert.Equal(4, Assert.Single(command.Values));
        Assert.Equal(validToken.Token, command.CancellationToken);
    }

    private sealed class ThrowingHolder
    {
        public const string Marker = "synthetic-localdb-getter@example.invalid";
        public string Value => throw new InvalidOperationException(Marker);
    }

    private sealed class Settings
    {
        public int Price { get; set; }
        public int Reads { get; private set; }
        public int MinPrice { get { Reads++; return Price; } }
    }

    [Fact]
    public async Task EmbeddedSensitiveStringUsesSqlServerDbParameterNotSqlLiteral()
    {
        await using var fixture = await LocalDbFixture.CreateAsync();
        var customerEmail = "synthetic-localdb-private@example.invalid";
        var condition = Condition<SqlOrderRow>.Create(x => x.Name == customerEmail);
        Assert.Empty(await fixture.Db.Orders.Where(x => condition.Invoke(x)).ToArrayAsync());
        var command = Assert.Single(fixture.Commands.Executed);
        Assert.True(command.SqlClientParameters);
        Assert.Single(command.Names);
        Assert.Equal(customerEmail, Assert.Single(command.Values));
        Assert.DoesNotContain(customerEmail, command.Sql);
        Assert.Contains(command.CommandNames.Single(), command.Sql);
        Assert.DoesNotContain(fixture.Messages, message => message.Contains(customerEmail, StringComparison.Ordinal));
        output.WriteLine(command.Sql);
        output.WriteLine("SQL Client parameter binding verified; values omitted.");
    }
}
