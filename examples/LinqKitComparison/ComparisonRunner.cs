using Microsoft.EntityFrameworkCore;

namespace LinqKitComparison;

public static class ComparisonRunner
{
    public static async Task RunAsync(ExampleDbContext db, bool showSql)
    {
        const decimal minimumPrice = 1_000m;
        var asOf = new DateTime(2026, 8, 30);
        var recentSaleCutoff = asOf.AddDays(-30);

        await CompareAsync(
            "Expression predicate inside a navigation collection",
            PureDotNetExamples.CustomersWithQualifyingNavigationPurchase(db, minimumPrice),
            LinqKitExamples.CustomersWithQualifyingNavigationPurchase(db, minimumPrice),
            RaffinertExamples.CustomersWithQualifyingNavigationPurchase(db, minimumPrice),
            showSql);

        await CompareAsync(
            "Expression predicate inside an ad-hoc correlated subquery",
            PureDotNetExamples.CustomersWithQualifyingAdHocPurchase(db, minimumPrice),
            LinqKitExamples.CustomersWithQualifyingAdHocPurchase(db, minimumPrice),
            RaffinertExamples.CustomersWithQualifyingAdHocPurchase(db, minimumPrice),
            showSql);

        await CompareAsync(
            "Combining reusable purchase criteria",
            PureDotNetExamples.CombinedPurchaseCriteria(db, minimumPrice),
            LinqKitExamples.CombinedPurchaseCriteria(db, minimumPrice),
            RaffinertExamples.CombinedPurchaseCriteria(db, minimumPrice),
            showSql);

        await CompareAsync(
            "Dynamic keyword search: all keywords",
            PureDotNetExamples.ProductsMatchingAllKeywords(db, "classic", "phone"),
            LinqKitExamples.ProductsMatchingAllKeywords(db, "classic", "phone"),
            RaffinertExamples.ProductsMatchingAllKeywords(db, "classic", "phone"),
            showSql);

        await CompareAsync(
            "Dynamic keyword search: any keyword",
            PureDotNetExamples.ProductsMatchingAnyKeyword(db, "BlackBerry", "iPhone"),
            LinqKitExamples.ProductsMatchingAnyKeyword(db, "BlackBerry", "iPhone"),
            RaffinertExamples.ProductsMatchingAnyKeyword(db, "BlackBerry", "iPhone"),
            showSql);

        await CompareAsync(
            "Nested predicates",
            PureDotNetExamples.NestedProductCriteria(db),
            LinqKitExamples.NestedProductCriteria(db),
            RaffinertExamples.NestedProductCriteria(db),
            showSql);

        await CompareAsync(
            "Reusable predicate library",
            PureDotNetExamples.ProductsFromReusableRuleScenario(db, recentSaleCutoff),
            LinqKitExamples.ProductsFromReusableRuleScenario(db, recentSaleCutoff),
            RaffinertExamples.ProductsFromReusableRuleScenario(db, recentSaleCutoff),
            showSql);

        await CompareAsync(
            "Generic reusable validity predicate",
            PureDotNetExamples.CurrentPriceListsStartingWith(db, asOf, "A"),
            LinqKitExamples.CurrentPriceListsStartingWith(db, asOf, "A"),
            RaffinertExamples.CurrentPriceListsStartingWith(db, asOf, "A"),
            showSql);

        await CompareAsync(
            "Reusable aggregate expression",
            PureDotNetExamples.DailyOrderAverages(db),
            LinqKitExamples.DailyOrderAverages(db),
            RaffinertExamples.DailyOrderAverages(db),
            showSql);

        Console.WriteLine();
        Console.WriteLine("All three implementations returned identical results.");
    }

    private static async Task CompareAsync<T>(
        string title,
        IQueryable<T> pureDotNet,
        IQueryable<T> linqKit,
        IQueryable<T> raffinert,
        bool showSql)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {title} ===");

        if (showSql)
        {
            WriteSql("Pure .NET", pureDotNet);
            WriteSql("LINQKit", linqKit);
            WriteSql("Raffinert.Expressions", raffinert);
        }

        var pureResults = await pureDotNet.ToArrayAsync();
        var linqKitResults = await linqKit.ToArrayAsync();
        var raffinertResults = await raffinert.ToArrayAsync();

        EnsureSame(title, pureResults, linqKitResults, raffinertResults);

        Console.WriteLine($"Pure .NET             : {Format(pureResults)}");
        Console.WriteLine($"LINQKit               : {Format(linqKitResults)}");
        Console.WriteLine($"Raffinert.Expressions : {Format(raffinertResults)}");
    }

    private static void EnsureSame<T>(
        string title,
        IReadOnlyList<T> pureDotNet,
        IReadOnlyList<T> linqKit,
        IReadOnlyList<T> raffinert)
    {
        if (!pureDotNet.SequenceEqual(linqKit) || !pureDotNet.SequenceEqual(raffinert))
            throw new InvalidOperationException($"The three implementations disagreed for '{title}'.");
    }

    private static string Format<T>(IEnumerable<T> values) =>
        string.Join(", ", values.Select(value => value?.ToString() ?? "<null>"));

    private static void WriteSql<T>(string label, IQueryable<T> query)
    {
        Console.WriteLine();
        Console.WriteLine($"-- {label}");
        Console.WriteLine(query.ToQueryString());
    }
}
