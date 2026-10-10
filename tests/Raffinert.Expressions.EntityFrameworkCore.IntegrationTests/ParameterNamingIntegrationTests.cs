using Microsoft.EntityFrameworkCore;

namespace Raffinert.Expressions.EntityFrameworkCore.IntegrationTests;

public class ParameterNamingIntegrationTests
{
    [Fact]
    public async Task SensitiveDescriptiveNameStaysBoundAndDiagnosticsRemainSafe()
    {
        var messages = new List<string>();
        await using var fixture = await SqliteFixture.CreateAsync(configure: b => b.EnableSensitiveDataLogging(false).LogTo(messages.Add));
        messages.Clear();
        var customerEmail = "synthetic-naming-private@example.invalid";
        var condition = Condition<OrderRow>.Create(x => x.Name == customerEmail);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x));
        Assert.Empty(await query.ToArrayAsync());
        var command = Assert.Single(fixture.Commands.Executed);
        Assert.Equal("__raffinert_customerEmail_0", Assert.Single(command.Names));
        Assert.Equal(customerEmail, Assert.Single(command.Values));
        Assert.DoesNotContain(customerEmail, command.Sql);
        Assert.DoesNotContain(messages, message => message.Contains(customerEmail, StringComparison.Ordinal));
        Assert.DoesNotContain(customerEmail, Assert.Throws<NotSupportedException>(() => query.ToQueryString()).ToString());
        Assert.Contains("WHERE", fixture.Db.Orders.Where(x => x.Id > 1).ToQueryString());
        Assert.Contains("WHERE", fixture.Db.Orders.Where(condition).ToQueryString());
    }

    [Fact]
    public async Task NestedGetterNamingAddsNoReadsOnMissOrHit()
    {
        await using var fixture = await SqliteFixture.CreateAsync(configure: b => b.EnableServiceProviderCaching(false));
        var settings = new Settings();
        var condition = Condition<OrderRow>.Create(x => x.TotalCents > settings.MinPrice);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x)).Select(x => x.Id);
        foreach (var price in new[] { 1000, 10000, 1000 })
        {
            settings.Price = price;
            Assert.Equal(price == 1000 ? new[] { 2, 3, 4 } : new[] { 2 }, await query.OrderBy(x => x).ToArrayAsync());
            var command = fixture.Commands.Executed.Last();
            Assert.Equal("__raffinert_settings_MinPrice_0", Assert.Single(command.Names));
            Assert.Equal(price, Assert.Single(command.Values));
        }
        Assert.Equal(3, settings.Reads);
        Assert.Equal(1, fixture.QueryCompilations);
    }

    [Fact]
    public async Task RepeatedOccurrencesAreDistinctAndIndependentlyBound()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var threshold = 3;
        var condition = Condition<OrderRow>.Create(x => x.TotalCents > threshold && x.Id < threshold);
        Assert.Equal(new[] { 1, 2 }, await fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());
        var command = Assert.Single(fixture.Commands.Executed);
        Assert.Equal(new[] { "__raffinert_threshold_0", "__raffinert_threshold_1" }, command.Names);
        Assert.Equal(new object?[] { threshold, threshold }, command.Values);
    }

    [Fact]
    public async Task NamedNullableCaptureBindsCurrentValues()
    {
        await using var fixture = await SqliteFixture.CreateAsync(configure: b => b.EnableServiceProviderCaching(false));
        int? customerId = 1;
        var condition = Condition<OrderRow>.Create(x => x.CustomerId == customerId);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        Assert.Equal(new[] { 1, 2 }, await query.ToArrayAsync());
        Assert.Equal("__raffinert_customerId_0", Assert.Single(fixture.Commands.Executed.Last().Names));
        customerId = null;
        Assert.Equal(new[] { 3 }, await query.ToArrayAsync());
        customerId = 2;
        Assert.Equal(new[] { 4 }, await query.ToArrayAsync());
        Assert.Equal(2, Assert.Single(fixture.Commands.Executed.Last().Values));
        Assert.Equal(1, fixture.QueryCompilations);
    }

    private sealed class Settings
    {
        public int Price { get; set; }
        public int Reads { get; private set; }
        public int MinPrice { get { Reads++; return Price; } }
        public override string ToString() => throw new InvalidOperationException("Naming must not serialize captures.");
    }
}
