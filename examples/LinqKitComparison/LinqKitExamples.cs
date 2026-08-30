using System.Linq.Expressions;
using LinqKit;

namespace LinqKitComparison;

public static class LinqKitExamples
{
    public static IQueryable<string> CustomersWithQualifyingNavigationPurchase(
        ExampleDbContext db,
        decimal minimumPrice)
    {
        Expression<Func<Purchase, bool>> purchaseCriteria = purchase => purchase.Price > minimumPrice;

        return db.Customers
            .AsExpandable()
            .Where(customer => customer.Purchases.Any(purchaseCriteria.Compile()))
            .OrderBy(customer => customer.Name)
            .Select(customer => customer.Name);
    }

    public static IQueryable<string> CustomersWithQualifyingAdHocPurchase(
        ExampleDbContext db,
        decimal minimumPrice)
    {
        Expression<Func<Purchase, bool>> purchaseCriteria = purchase => purchase.Price > minimumPrice;

        return
            from customer in db.Customers.AsExpandable()
            where db.Purchases
                .Where(purchase => purchase.CustomerId == customer.Id)
                .Any(purchaseCriteria)
            orderby customer.Name
            select customer.Name;
    }

    public static IQueryable<string> CombinedPurchaseCriteria(ExampleDbContext db, decimal minimumPrice)
    {
        Expression<Func<Purchase, bool>> expensive = purchase => purchase.Price > minimumPrice;
        Expression<Func<Purchase, bool>> combined = purchase =>
            expensive.Invoke(purchase) || purchase.Description.Contains("service");

        return db.Purchases
            .Where(combined.Expand())
            .OrderBy(purchase => purchase.Description)
            .Select(purchase => purchase.Description);
    }

    public static IQueryable<string> ProductsMatchingAllKeywords(
        ExampleDbContext db,
        params string[] keywords)
    {
        var predicate = PredicateBuilder.New<Product>(true);

        foreach (var keyword in keywords)
            predicate = predicate.And(product => product.Description.Contains(keyword));

        return db.Products
            .AsExpandable()
            .Where(predicate)
            .OrderBy(product => product.Description)
            .Select(product => product.Description);
    }

    public static IQueryable<string> ProductsMatchingAnyKeyword(
        ExampleDbContext db,
        params string[] keywords)
    {
        var predicate = PredicateBuilder.New<Product>();

        foreach (var keyword in keywords)
            predicate = predicate.Or(product => product.Description.Contains(keyword));

        return db.Products
            .AsExpandable()
            .Where(predicate)
            .OrderBy(product => product.Description)
            .Select(product => product.Description);
    }

    public static IQueryable<string> NestedProductCriteria(ExampleDbContext db)
    {
        var descriptions = PredicateBuilder.New<Product>()
            .Start(product => product.Description.Contains("foo"))
            .Or(product => product.Description.Contains("far"));

        var predicate = PredicateBuilder.New<Product>()
            .Start(product => product.Price > 100m)
            .And(product => product.Price < 1_000m)
            .And(descriptions);

        return db.Products
            .AsExpandable()
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
            .AsExpandable()
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
            .AsExpandable()
            .Where(predicate)
            .OrderBy(priceList => priceList.Name)
            .Select(priceList => priceList.Name);
    }

    public static IQueryable<DailyAverage> DailyOrderAverages(ExampleDbContext db)
    {
        Expression<Func<IQueryable<Order>, double?>> average = orders =>
            orders.Average(order => (double?)order.Amount);

        return
            from order in db.Orders.AsExpandable()
            group order by order.OrderDate into orders
            orderby orders.Key
            select new DailyAverage(
                orders.Key,
                average.Invoke(orders.AsQueryable()));
    }

    private static Expression<Func<TEntity, bool>> IsCurrent<TEntity>(DateTime asOf)
        where TEntity : IValidFromTo =>
        entity =>
            (entity.ValidFrom == null || entity.ValidFrom <= asOf) &&
            (entity.ValidTo == null || entity.ValidTo >= asOf);

    private static Expression<Func<Product, bool>> ContainsInDescription(params string[] keywords)
    {
        var predicate = PredicateBuilder.New<Product>();

        foreach (var keyword in keywords)
            predicate = predicate.Or(product => product.Description.Contains(keyword));

        return predicate;
    }

    private static Expression<Func<Product, bool>> IsSelling(DateTime recentSaleCutoff) =>
        product => !product.Discontinued && product.LastSale > recentSaleCutoff;
}
