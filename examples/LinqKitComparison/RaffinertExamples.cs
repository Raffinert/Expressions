using Raffinert.Expressions;

namespace LinqKitComparison;

public static class RaffinertExamples
{
    public static IQueryable<string> CustomersWithQualifyingNavigationPurchase(
        ExampleDbContext db,
        decimal minimumPrice)
    {
        var purchaseCriteria = Condition<Purchase>.Create(purchase => purchase.Price > minimumPrice);
        var customerCriteria = Condition<Customer>.Create(customer =>
            customer.Purchases.Any(purchaseCriteria.Invoke));

        return db.Customers
            .Where(customerCriteria)
            .OrderBy(customer => customer.Name)
            .Select(customer => customer.Name);
    }

    public static IQueryable<string> CustomersWithQualifyingAdHocPurchase(
        ExampleDbContext db,
        decimal minimumPrice)
    {
        var purchaseCriteria = Condition<Purchase>.Create(purchase => purchase.Price > minimumPrice);

        return
            from customer in db.Customers.AsRaffinertQuery()
            where db.Purchases.Any(purchase =>
                purchase.CustomerId == customer.Id && purchaseCriteria.Invoke(purchase))
            orderby customer.Name
            select customer.Name;
    }

    public static IQueryable<string> CombinedPurchaseCriteria(ExampleDbContext db, decimal minimumPrice)
    {
        var expensive = Condition<Purchase>.Create(purchase => purchase.Price > minimumPrice);
        var combined = Condition<Purchase>.Create(purchase =>
            expensive.Invoke(purchase) || purchase.Description.Contains("service"));

        return db.Purchases
            .Where(combined)
            .OrderBy(purchase => purchase.Description)
            .Select(purchase => purchase.Description);
    }

    public static IQueryable<string> ProductsMatchingAllKeywords(
        ExampleDbContext db,
        params string[] keywords)
    {
        var predicate = keywords
            .Select(keyword => Condition<Product>.Create(product => product.Description.Contains(keyword)))
            .Aggregate(Condition<Product>.True, (current, next) => current.And(next));

        return db.Products
            .Where(predicate)
            .OrderBy(product => product.Description)
            .Select(product => product.Description);
    }

    public static IQueryable<string> ProductsMatchingAnyKeyword(
        ExampleDbContext db,
        params string[] keywords)
    {
        var predicate = keywords
            .Select(keyword => Condition<Product>.Create(product => product.Description.Contains(keyword)))
            .Aggregate(Condition<Product>.False, (current, next) => current.Or(next));

        return db.Products
            .Where(predicate)
            .OrderBy(product => product.Description)
            .Select(product => product.Description);
    }

    public static IQueryable<string> NestedProductCriteria(ExampleDbContext db)
    {
        var descriptions = Condition<Product>.Create(product => product.Description.Contains("foo"))
            .Or(product => product.Description.Contains("far"));

        var predicate = Condition<Product>.Create(product => product.Price > 100m)
            .And(product => product.Price < 1_000m)
            .And(descriptions);

        return db.Products
            .Where(predicate)
            .OrderBy(product => product.Description)
            .Select(product => product.Description);
    }

    public static IQueryable<string> ProductsFromReusableRuleScenario(
        ExampleDbContext db,
        DateTime recentSaleCutoff)
    {
        var newKids = ContainsInDescription("BlackBerry", "iPhone");
        var classics = ContainsInDescription("Nokia", "Ericsson")
            .And(IsSelling(recentSaleCutoff));

        return db.Products
            .Where(newKids.Or(classics))
            .OrderBy(product => product.Description)
            .Select(product => product.Description);
    }

    public static IQueryable<string> CurrentPriceListsStartingWith(
        ExampleDbContext db,
        DateTime asOf,
        string prefix)
    {
        var predicate = IsCurrent<PriceList>(asOf)
            .And(priceList => priceList.Name.StartsWith(prefix));

        return db.PriceLists
            .Where(predicate)
            .OrderBy(priceList => priceList.Name)
            .Select(priceList => priceList.Name);
    }

    public static IQueryable<DailyAverage> DailyOrderAverages(ExampleDbContext db)
    {
        var average = Projection<IQueryable<Order>, double?>.Create(orders =>
            orders.Average(order => (double?)order.Amount));

        return
            from order in db.Orders.AsRaffinertQuery()
            group order by order.OrderDate into orders
            orderby orders.Key
            select new DailyAverage(
                orders.Key,
                average.Invoke(orders.AsQueryable()));
    }

    private static Condition<TEntity> IsCurrent<TEntity>(DateTime asOf)
        where TEntity : IValidFromTo =>
        Condition<TEntity>.Create(entity =>
            (entity.ValidFrom == null || entity.ValidFrom <= asOf) &&
            (entity.ValidTo == null || entity.ValidTo >= asOf));

    private static Condition<Product> ContainsInDescription(params string[] keywords) =>
        keywords
            .Select(keyword => Condition<Product>.Create(product => product.Description.Contains(keyword)))
            .Aggregate(Condition<Product>.False, (current, next) => current.Or(next));

    private static Condition<Product> IsSelling(DateTime recentSaleCutoff) =>
        Condition<Product>.Create(product =>
            !product.Discontinued && product.LastSale > recentSaleCutoff);
}
