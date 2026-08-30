namespace LinqKitComparison;

/// <summary>
/// The no-library baseline. Reusable expression plumbing is deliberately avoided: logic is written inline,
/// and duplicated where an ordinary EF Core lambda has no composition mechanism.
/// </summary>
public static class PureDotNetExamples
{
    public static IQueryable<string> CustomersWithQualifyingNavigationPurchase(
        ExampleDbContext db,
        decimal minimumPrice) =>
        db.Customers
            // Limitation: the purchase rule must live inside this query and cannot be supplied as a reusable
            // expression. LINQKit expands a supplied expression; Raffinert.Expressions expands a nested Condition.
            .Where(customer => customer.Purchases.Any(purchase => purchase.Price > minimumPrice))
            .OrderBy(customer => customer.Name)
            .Select(customer => customer.Name);

    public static IQueryable<string> CustomersWithQualifyingAdHocPurchase(
        ExampleDbContext db,
        decimal minimumPrice) =>
        from customer in db.Customers
            // Limitation: the price rule is duplicated inside the correlated subquery. LINQKit and Raffinert.Expressions
            // are both designed to inject a separately declared predicate at this point.
        where db.Purchases.Any(purchase =>
            purchase.CustomerId == customer.Id && purchase.Price > minimumPrice)
        orderby customer.Name
        select customer.Name;

    public static IQueryable<string> CombinedPurchaseCriteria(ExampleDbContext db, decimal minimumPrice) =>
        db.Purchases
            // Limitation: both branches are hardcoded into one lambda. There is no separately reusable
            // "expensive purchase" predicate to combine, which Invoke/Expand and Condition.Invoke provide.
            .Where(purchase =>
                purchase.Price > minimumPrice ||
                purchase.Description.Contains("service"))
            .OrderBy(purchase => purchase.Description)
            .Select(purchase => purchase.Description);

    public static IQueryable<string> ProductsMatchingAllKeywords(
        ExampleDbContext db,
        params string[] keywords)
    {
        IQueryable<Product> query = db.Products;

        // Standard chained Where calls naturally mean "all keywords", but they do not produce one reusable
        // predicate that can be nested elsewhere. PredicateBuilder and Condition.And do produce such a value.
        foreach (var keyword in keywords)
            query = query.Where(product => product.Description.Contains(keyword));

        return query.OrderBy(product => product.Description).Select(product => product.Description);
    }

    public static IQueryable<string> ProductsMatchingAnyKeyword(
        ExampleDbContext db,
        params string[] keywords)
    {
        // Limitation: with no expression composition, the number of OR branches is hardcoded. This sample
        // accepts exactly two keywords; LINQKit PredicateBuilder and Raffinert.Expressions Condition.Or accept any count.
        if (keywords.Length != 2)
            throw new ArgumentException("The pure .NET example intentionally spells out exactly two OR terms.", nameof(keywords));

        var first = keywords[0];
        var second = keywords[1];

        return db.Products
            .Where(product =>
                product.Description.Contains(first) ||
                product.Description.Contains(second))
            .OrderBy(product => product.Description)
            .Select(product => product.Description);
    }

    public static IQueryable<string> NestedProductCriteria(ExampleDbContext db) =>
        db.Products
            // Limitation: the parenthesized description rule is hardcoded inside the outer price rule.
            // LINQKit and Raffinert.Expressions can build the inner rule independently and compose it into the outer rule.
            .Where(product =>
                product.Price > 100m &&
                product.Price < 1_000m &&
                (product.Description.Contains("foo") || product.Description.Contains("far")))
            .OrderBy(product => product.Description)
            .Select(product => product.Description);

    public static IQueryable<string> ProductsFromReusableRuleScenario(
        ExampleDbContext db,
        DateTime recentSaleCutoff) =>
        db.Products
            // Limitation: the keyword groups and IsSelling rule are duplicated into one provider lambda.
            // LINQKit expressions and Raffinert.Expressions Conditions keep those rules named, reusable, and composable.
            .Where(product =>
                product.Description.Contains("BlackBerry") ||
                product.Description.Contains("iPhone") ||
                ((product.Description.Contains("Nokia") || product.Description.Contains("Ericsson")) &&
                 !product.Discontinued &&
                 product.LastSale > recentSaleCutoff))
            .OrderBy(product => product.Description)
            .Select(product => product.Description);

    public static IQueryable<string> CurrentPriceListsStartingWith(
        ExampleDbContext db,
        DateTime asOf,
        string prefix) =>
        db.PriceLists
            // Limitation: the ValidFrom/ValidTo rule is hardcoded for PriceList. LINQKit can compose a generic
            // expression and Raffinert.Expressions a generic Condition<TEntity> with the entity-specific name rule.
            .Where(priceList =>
                (priceList.ValidFrom == null || priceList.ValidFrom <= asOf) &&
                (priceList.ValidTo == null || priceList.ValidTo >= asOf) &&
                priceList.Name.StartsWith(prefix))
            .OrderBy(priceList => priceList.Name)
            .Select(priceList => priceList.Name);

    public static IQueryable<DailyAverage> DailyOrderAverages(ExampleDbContext db) =>
        from order in db.Orders
        group order by order.OrderDate into orders
        orderby orders.Key
        select new DailyAverage(
            orders.Key,
            // Limitation: Average is hardcoded in this projection. LINQKit can invoke a reusable aggregate
            // expression here; Raffinert.Expressions can invoke a reusable Projection<IQueryable<Order>, double?>.
            orders.Average(order => (double?)order.Amount));
}
