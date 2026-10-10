using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Raffinert.Expressions.EntityFrameworkCore.IntegrationTests;

public class CaptureRegressionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HiddenCollectionsAndDirectOperatorsUseNativeCurrentValues(bool list)
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        int[] ids = [1, 2];
        var items = new List<int> { 1, 2 };
        var condition = list
            ? Condition<OrderRow>.Create(x => items.Contains(x.Id))
            : Condition<OrderRow>.Create(x => Enumerable.Contains(ids, x.Id));
        var embedded = fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        Assert.Equal(new[] { 1, 2 }, await embedded.ToArrayAsync());

        var query = fixture.Db.Orders.Where(condition).OrderBy(x => x.Id).Select(x => x.Id);
        Assert.Equal(new[] { 1, 2 }, await query.ToArrayAsync());
        ids = [4];
        items.Clear();
        items.Add(4);
        Assert.Equal(new[] { 4 }, await query.ToArrayAsync());
        Assert.Equal(new[] { 4 }, await embedded.ToArrayAsync());
        Assert.Equal(4, fixture.Commands.Executed.Count);
        Assert.All(fixture.Commands.Executed, command => Assert.Contains("WHERE", command.Sql));
        output.WriteLine(fixture.Commands.Executed[1].Sql);
        output.WriteLine("Native collection bindings verified; values omitted.");
    }

    [Theory]
    [InlineData("string")]
    [InlineData("Guid")]
    [InlineData("decimal")]
    [InlineData("DateTimeOffset")]
    [InlineData("DateOnly")]
    [InlineData("TimeOnly")]
    [InlineData("enum")]
    public async Task CapturedScalarKindsBindCurrentValuesOnTheServer(string kind)
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var name = "Desk";
        var token = new Guid(2, 0, 0, new byte[8]);
        var price = 3m;
        var recorded = new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var day = new DateOnly(2020, 1, 2);
        var time = new TimeOnly(2, 0);
        var status = OrderStatus.Open;
        var condition = kind switch
        {
            "string" => Condition<OrderRow>.Create(x => x.Name == name),
            "Guid" => Condition<OrderRow>.Create(x => x.Token == token),
            "decimal" => Condition<OrderRow>.Create(x => x.Price == price),
            "DateTimeOffset" => Condition<OrderRow>.Create(x => x.RecordedAt == recorded),
            "DateOnly" => Condition<OrderRow>.Create(x => x.Day == day),
            "TimeOnly" => Condition<OrderRow>.Create(x => x.Time == time),
            _ => Condition<OrderRow>.Create(x => x.Status == status)
        };
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x)).Select(x => x.Id);
        Assert.Equal(new[] { 2 }, await query.ToArrayAsync());
        name = "Hidden";
        token = new Guid(4, 0, 0, new byte[8]);
        price = 6m;
        recorded = new DateTimeOffset(2020, 1, 4, 0, 0, 0, TimeSpan.Zero);
        day = new DateOnly(2020, 1, 4);
        time = new TimeOnly(4, 0);
        status = OrderStatus.Archived;
        Assert.Equal(new[] { 4 }, await query.ToArrayAsync());
        Assert.Equal(2, fixture.Commands.Executed.Count);
        Assert.All(fixture.Commands.Executed, command =>
        {
            Assert.Contains("WHERE", command.Sql);
            Assert.NotNull(Assert.Single(command.Values));
            output.WriteLine($"{kind}: {command.Sql}; one bound value");
        });
        Assert.Equal(fixture.Commands.Executed[0].Sql, fixture.Commands.Executed[1].Sql);
        Assert.NotEqual(fixture.Commands.Executed[0].Values[0], fixture.Commands.Executed[1].Values[0]);
    }

    [Fact]
    public async Task NullableScalarBindingChangesBetweenValueAndNull()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        int? customer = 1;
        var condition = Condition<OrderRow>.Create(x => x.CustomerId == customer);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        Assert.Equal(new[] { 1, 2 }, await query.ToArrayAsync());
        customer = null;
        Assert.Equal(new[] { 3 }, await query.ToArrayAsync());
        Assert.Equal(2, fixture.Commands.Executed.Count);
        Assert.Contains("IS NULL", fixture.Commands.Executed[1].Sql);
        Assert.Equal(1, Assert.Single(fixture.Commands.Executed[0].Values));
        Assert.Empty(fixture.Commands.Executed[1].Values); // SQLite simplifies a null parameter to IS NULL.
        output.WriteLine(fixture.Commands.Executed[0].Sql);
        output.WriteLine(fixture.Commands.Executed[1].Sql);
    }

    [Fact]
    public async Task InterfaceWrapperThroughHolderPropertyUsesCurrentInstance()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var holder = new ConditionHolder { Predicate = Condition<OrderRow>.Create(x => x.Active) };
        var query = fixture.Db.Orders.Where(x => holder.Predicate.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        Assert.Equal(new[] { 1, 2 }, await query.ToArrayAsync());
        holder.Predicate = Condition<OrderRow>.Create(x => !x.Active);
        Assert.Equal(new[] { 3, 4 }, await query.ToArrayAsync());
        Assert.Equal(2, fixture.Commands.Executed.Count);
        Assert.All(fixture.Commands.Executed, command => Assert.Contains("WHERE", command.Sql));
    }

    private sealed class ConditionHolder
    {
        public IComposableExpression<OrderRow, bool> Predicate { get; set; } = null!;
    }
}
