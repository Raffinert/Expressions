# Application usage patterns

Use examples with real business intent. These are illustrative models, not types shipped by Raffinert. Import `Raffinert.Expressions` and the chosen LINQ provider's usual namespaces.

## Dynamic conditions

```csharp
var nameMatches = ComposableExpression<Product>.Start();
foreach (var keyword in keywords)
    nameMatches = nameMatches.Or(p => p.Name.Contains(keyword));

var products = db.Products.Where(nameMatches);
```

The unchanged default starter returns `false`. For an optional AND-filter pipeline in which no filters means accept all, use `ComposableExpression<Product>.Start(defaultWhenEmpty: true)` and `And` instead. The result of each `And`/`Or` is a *new* wrapper.

## Composition across different types

```csharp
var customer = ComposableExpression<Order>.Create(o => o.Customer);
var customerName = ComposableExpression<Customer>.Create(c => c.Name);
var orderCustomerName = customer.Then(customerName);

var names = await db.Orders.Select(orderCustomerName).ToListAsync();
```

`Then` composes by type-safe parameter substitution. It can also compose a projection followed by a `Condition` on the intermediate type.

## Optional export fields without duplicate base mapping

For an `OrderExportRow` with a parameterless constructor and `decimal? Total`, a line's `VatRate` represented as a decimal fraction, and navigable `Order.Lines`:

```csharp
var netAmount = ComposableExpression<OrderLine>.Create(l => l.Quantity * l.UnitPrice);
var grossAmount = ComposableExpression<OrderLine>.Create(l => netAmount.Invoke(l) * (1m + l.VatRate));

var export = ComposableExpression<Order>.Create(o => new OrderExportRow
{
    Number = o.Number,
    Customer = o.Customer.Name
});

if (includeAmounts)
{
    var lineAmount = includeVat ? grossAmount : netAmount;
    var total = ComposableExpression<Order>.Create(o => o.Lines.Sum(l => lineAmount.Invoke(l)));
    export = export.MergeBindings(ComposableExpression<Order>.Create(o => new OrderExportRow
    {
        Total = total.Invoke(o)
    }));
}

var rows = await db.Orders.Select(export).ToListAsync();
```

When `includeAmounts` is false, the final expanded initializer has **no `Total` member assignment**. It is not a SQL ternary, not a `null` assignment, and not a claim that a non-existent `Splice.Select` API exists. `MergeBindings` combines compatible initializer assignments; duplicate destination members use `BindingConflictBehavior.UseLast` by default (`UseFirst`/`Throw` are options).

## Adapting compatible structures

```csharp
ComposableExpression<Product, bool> productRule = ComposableExpression<Product>.Create(p => p.Stock > 0);
ComposableExpression<InventoryItem, bool> inventoryRule = productRule.AdaptSource<InventoryItem>();
```

This requires a compatible public `Stock` member on the adapted source. Projection adaptation offers `AdaptSource`, `AdaptResult`, and `Adapt` for supported shapes.

## Null-safe nested projection

```csharp
var category = ComposableExpression<Category>.Create(c => new CategoryDto { Name = c.Name });
var product = ComposableExpression<Product>.Create(p => new ProductDto
{
    Category = category.InvokeOrDefault(p.Category)
});
```

`InvokeOrDefault` returns `default(TResult)` for null input: for value types that can be `0` or `false`, not necessarily `null`. Ordinary `Invoke` does not implicitly add a null guard.

## Mapping to an existing object

```csharp
var mapping = ComposableExpression<Order>.Create(o => new OrderDto
{
    Status = o.Status,
    Total = o.Total
});

OrderDto? destination = existingDto;
mapping.MapToExisting(order, ref destination);
```

`MapToExisting` is a runtime mapping capability; do not treat it as a SQL UPDATE generator. It preserves the destination instance when present, assigns members represented in the mapping, and handles nested objects/collections according to its documented semantics. Existing mutable collections are cleared and refilled, not matched by item ID.