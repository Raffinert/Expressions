using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Raffinert.Expressions.EntityFrameworkCore.SqlServerTests;

internal sealed class LocalDbFixture : IAsyncDisposable
{
    internal const string DefaultMasterConnection = @"Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=True;Encrypt=False;Connection Timeout=30;";
    private readonly string _masterConnection;
    private readonly string _databaseConnection;
    private bool _creationStarted;
    private bool _disposed;
    public string DatabaseName { get; } = "Raffinert_PR7_" + Guid.NewGuid().ToString("N");
    public string EngineVersion { get; private set; } = "";
    public LocalDbCommandRecorder Commands { get; } = new();
    public List<string> Messages { get; } = [];
    public int QueryCompilations { get; private set; }
    public LocalDbOrdersContext Db { get; private set; } = null!;

    private LocalDbFixture()
    {
        var builder = ValidateMasterConnection(Environment.GetEnvironmentVariable("RAFFINERT_LOCALDB_MASTER_CONNECTION") ?? DefaultMasterConnection);
        _masterConnection = builder.ConnectionString;
        builder.InitialCatalog = DatabaseName;
        _databaseConnection = builder.ConnectionString;
    }

    internal static SqlConnectionStringBuilder ValidateMasterConnection(string connection)
    {
        SqlConnectionStringBuilder builder;
        try { builder = new(connection); }
        catch (ArgumentException) { throw new InvalidOperationException("Invalid LocalDB test connection configuration."); }
        if (!Regex.IsMatch(builder.DataSource, @"^\(localdb\)\\[^\\/;]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || !builder.IntegratedSecurity || builder.UserID.Length != 0 || builder.Password.Length != 0
            || builder.AttachDBFilename.Length != 0 || builder.FailoverPartner.Length != 0)
            throw new InvalidOperationException("Tests require a private LocalDB instance under the current Windows user with integrated authentication and no attached file.");
        builder.InitialCatalog = "master";
        builder.ConnectTimeout = 30;
        return builder;
    }

    public static async Task<LocalDbFixture> CreateAsync()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("LocalDB tests require Windows; run the separate SQL Server test project explicitly.");
        var fixture = new LocalDbFixture();
        try
        {
            await using (var master = new SqlConnection(fixture._masterConnection))
            {
                try { await master.OpenAsync(); }
                catch (SqlException)
                {
                    throw new InvalidOperationException("Cannot connect to LocalDB master. Check sqllocaldb info MSSQLLocalDB and start the configured current-user instance.");
                }
                await using var command = master.CreateCommand();
                command.CommandText = "SELECT @@VERSION";
                fixture.EngineVersion = (string)(await command.ExecuteScalarAsync())!;
                command.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @name";
                command.Parameters.AddWithValue("@name", fixture.DatabaseName);
                if ((int)(await command.ExecuteScalarAsync())! != 0)
                    throw new InvalidOperationException("Generated test database already exists; refusing to use or delete it.");
            }
            fixture.Db = fixture.CreateContext(fixture.Commands);
            fixture._creationStarted = true;
            await fixture.Db.Database.EnsureCreatedAsync();
            fixture.Db.Orders.AddRange(
                new SqlOrderRow { Id = 1, TotalCents = 200, Name = "Pencil", Active = true, CustomerId = 1 },
                new SqlOrderRow { Id = 2, TotalCents = 20000, Name = "Desk", Active = true, CustomerId = 1 },
                new SqlOrderRow { Id = 3, TotalCents = 1500, Name = "Uncategorized", Active = false },
                new SqlOrderRow { Id = 4, TotalCents = 9000, Name = "Hidden", Active = false, CustomerId = 2 });
            await fixture.Db.SaveChangesAsync();
            fixture.Db.ChangeTracker.Clear();
            fixture.Commands.Executed.Clear();
            fixture.Messages.Clear();
            fixture.QueryCompilations = 0;
            return fixture;
        }
        catch
        {
            try { await fixture.DisposeAsync(); }
            catch (Exception cleanup) { Trace.TraceError("LocalDB fixture cleanup failed ({0}); original initialization error preserved.", cleanup.GetType().Name); }
            throw;
        }
    }

    public LocalDbOrdersContext CreateContext(LocalDbCommandRecorder recorder) => new(
        new DbContextOptionsBuilder<LocalDbOrdersContext>().UseSqlServer(_databaseConnection)
            .UseRaffinertExpressions().AddInterceptors(recorder).EnableSensitiveDataLogging(false)
            .EnableServiceProviderCaching(false)
            .LogTo((eventId, _) => eventId == CoreEventId.QueryCompilationStarting || eventId == RelationalEventId.CommandExecuted,
                data =>
                {
                    if (data.EventId == CoreEventId.QueryCompilationStarting) QueryCompilations++;
                    else Messages.Add(data.ToString());
                })
            .Options);

    public async Task<bool> DatabaseExistsAsync()
    {
        await using var master = new SqlConnection(_masterConnection);
        await master.OpenAsync();
        await using var command = master.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @name";
        command.Parameters.AddWithValue("@name", DatabaseName);
        return (int)(await command.ExecuteScalarAsync())! != 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        try
        {
            if (Db != null) await Db.DisposeAsync();
        }
        finally
        {
            if (_creationStarted)
            {
                var builder = new SqlConnectionStringBuilder(_databaseConnection);
                if (builder.InitialCatalog != DatabaseName || !Regex.IsMatch(DatabaseName, "^Raffinert_PR7_[a-f0-9]{32}$"))
                    throw new InvalidOperationException("Refusing cleanup of a database not owned by this fixture.");
                await using var cleanup = new LocalDbOrdersContext(new DbContextOptionsBuilder<LocalDbOrdersContext>()
                    .UseSqlServer(builder.ConnectionString).Options);
                await cleanup.Database.EnsureDeletedAsync();
            }
            _disposed = true;
        }
    }
}

public sealed class LocalDbOrdersContext(DbContextOptions<LocalDbOrdersContext> options) : DbContext(options)
{
    public DbSet<SqlOrderRow> Orders => Set<SqlOrderRow>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<SqlOrderRow>().Property(x => x.Id).ValueGeneratedNever();
}

public sealed class SqlOrderRow
{
    public int Id { get; set; }
    public int TotalCents { get; set; }
    public string Name { get; set; } = "";
    public bool Active { get; set; }
    public int? CustomerId { get; set; }
}
