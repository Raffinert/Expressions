using LinqKitComparison;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();

var options = new DbContextOptionsBuilder<ExampleDbContext>()
    .UseSqlite(connection)
    .EnableSensitiveDataLogging()
    .Options;

await using var db = new ExampleDbContext(options);
await db.Database.EnsureCreatedAsync();
await ExampleData.SeedAsync(db);

var showSql = args.Contains("--sql", StringComparer.OrdinalIgnoreCase);
await ComparisonRunner.RunAsync(db, showSql);
await RaffinertSpecificExamples.RunAsync(db, showSql);
