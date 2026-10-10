# PR #7 SQL Server LocalDB validation

> Historical checkpoint report. Current support is .NET 10 / EF Core 10.x only;
> EF7/8/9 are no longer supported. Counts and CI below belong to earlier commits.
> See [current EF10 validation](pr7-ef10-only-validation.md) for 228 solution tests,
> 42 LocalDB tests and the final migration evidence.


## Baseline and prerequisites

Starting PR HEAD: `07e3de1c3bf65d85f455585887b11aa8c672ae79`, matching the plan.
Work stayed on `feature/efcore-integration`. Changes since parameter naming were
two conversion cleanups and an async-test import cleanup; both remain intact.
All pre-existing untracked plans and `pr_summary.md` are preserved.

SDK 10.0.401 on Windows 10.0.26200; runtime 10.0.12. Before adding SQL Server
files, solution restore, tests and format verification passed: 209 tests (62 core,
5 QuerySyntax, 14 existing integration, 128 adapter), zero failed/skipped.

`sqllocaldb versions/info/info MSSQLLocalDB` found the installed automatic instance
stopped. `sqllocaldb start MSSQLLocalDB` started it without recreation. A temporary
Microsoft.Data.SqlClient probe opened `master` and executed SELECT @@VERSION:
SQL Server 2025 RTM-CU3 (KB5077896), **17.0.4025.3**, X64 Express Edition.
No credentials or parameter values were logged. The probe found zero Raffinert
test databases before work and zero after the final local suite.

Prerequisite handling follows Microsoft's [LocalDB documentation](https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/sql-server-express-localdb)
and [SqlLocalDB utility reference](https://learn.microsoft.com/en-us/sql/tools/sqllocaldb-utility).
Database setup/cleanup use EF's public [EnsureCreated/EnsureDeleted APIs](https://learn.microsoft.com/en-us/ef/core/managing-schemas/ensure-created).

## Test-first checkpoints

The separate net10.0 SQL Server project references Microsoft.EntityFrameworkCore.SqlServer
10.0.11 only in tests, plus the existing adapter project. Test SDK/xUnit/collector
versions match the existing EF integration suite. No SQLite test dependency or
production SQL Server package dependency was added. The project is outside `.slnx`.

1. Project restore/build passed with zero warnings/errors.
2. SimpleSelectExecutesAgainstRealLocalDb passed. It asserted real seed results,
   database existence and absence after fixture disposal.
3. EmbeddedSensitiveStringUsesSqlServerDbParameterNotSqlLiteral passed immediately.
   There was no RED provider defect and **no production change**.
4. Each remaining required scenario was added and executed individually; all
   passed against real LocalDB, without library changes.
5. Seven unsafe connection inputs are rejected without echoing configuration;
   a catalog-override test proves supplied catalogs are replaced with master.
6. Full local suite passed 23 cases (15 real database tests plus 8 safety cases),
   zero failed/skipped. The cross-platform solution still passed all 209 tests.
   All 50 focused SQLite runtime/naming/cache/execution-audit cases also passed.
   Both formatting checks and git diff --check passed.

A test-fixture review caught that sequential LogTo registrations replace one
another. The fixture uses one public structured event callback to count only
QueryCompilationStarting and record only CommandExecuted diagnostics. It never
records captured values in successful test output. This was a fixture correction,
not a production or SQL Server translation defect.

## Fixture isolation and cleanup

The optional RAFFINERT_LOCALDB_MASTER_CONNECTION is parsed with
SqlConnectionStringBuilder. Only a private `(localdb)\Instance` with integrated
authentication is accepted. Remote/shared instances, explicit credentials,
attached files and failover servers are rejected with sanitized errors.
Master checks have a 30-second connection timeout. Default Encrypt=False applies
only to this ephemeral local Windows environment.

Each fixture generates `Raffinert_PR7_` plus 32 random hexadecimal characters,
verifies that exact database does not already exist, sets InitialCatalog using
the builder and calls EnsureCreatedAsync. IDs are configured ValueGeneratedNever
to allow the deterministic four-row seed. Recording/counting is reset afterward.
The random database identifier concerns test isolation, not production parameter names.

Disposal closes the context and, in finally, checks the exact generated name and
connection catalog before EnsureDeletedAsync through a separate cleanup context.
Partial initialization attempts cleanup without masking the original failure.
No user-supplied database name or master connection is a deletion target.
Independent contexts have independent command recorders and are used sequentially.

The recorder intercepts sync/async reader and scalar execution, retaining SQL,
logical names, raw provider names, values and cancellation tokens separately.
DBNull normalizes to null for assertions. SqlParameter identity is checked without
brittle size/type assumptions. Recorded values are asserted, never printed.

## Executed SQL and regression evidence

The sensitive-string test's real executed command:

```sql
SELECT [o].[Id], [o].[Active], [o].[CustomerId], [o].[Name], [o].[TotalCents]
FROM [Orders] AS [o]
WHERE [o].[Name] = @__raffinert_customerEmail_0
```

Assertions check logical name `__raffinert_customerEmail_0`, the SQL placeholder,
actual SqlClient parameter binding, and absence of the captured marker from SQL
and non-sensitive diagnostics. Neither synthetic markers nor values are copied here.

The 25-value-plus-repeat test executes:

```sql
SELECT COUNT(*)
FROM [Orders] AS [o]
WHERE [o].[TotalCents] > @__raffinert_threshold_0
```

Observed: 26 correct executions, current bound values, stable logical name,
**one compilation and one command shape**. No server plan-cache claim is made.
Additional real database tests verify A → B → A, nested getter read counts,
two distinct repeated-occurrence parameters, native EF collision independence,
nullable value/null/value semantics, server composition, wrapper reassignment,
ToQueryString rejection and ordinary/direct controls, unsupported/compiled capture
fail-fast behavior, supported scalar compiled delegate parameters, independent
contexts, and recovery after getter failure and cancellation.

## CI and coverage scope

Source checkpoint: `5048b550cc18abdb57e5c7835a3bf9d87214b48d`.
[CI run 38063725982](https://github.com/Raffinert/Expressions/actions/runs/38063725982)
completed successfully: **all 11 jobs passed**, including the new
[LocalDB job](https://github.com/Raffinert/Expressions/actions/runs/38063725982/job/114247023415).
The actual runner installed SQL Server 2025 CU3 **17.0.4025.3**, and its real
master/SELECT test output confirms that engine. Runtime **10.0.12** was installed.
All 23 SQL Server/safety cases, separate-project formatting and TRX upload passed.
The prior ten Windows/Linux solution, EF 7–10 and isolated SQLite package-consumer
jobs also passed. The subsequent documentation-only HEAD is checked separately;
its exact SHA/run/job conclusions are recorded in the PR description after completion.

The separate SQL Server LocalDB (EF Core 10) job runs on windows-latest.
The inspected [official Windows runner image inventory](https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-Readme.md)
lists SQL.LocalDB.Runtime; the job nevertheless discovers the binary, inspects
versions, creates MSSQLLocalDB only when absent and starts only when stopped.
Missing tooling/startup/connectivity makes this job fail clearly, never skip.
The fixture verifies master connectivity independently of utility status.
TRX is uploaded under sqlserver-localdb-windows, including engine and placeholder
SQL evidence. Existing verify and four-version Windows/Linux matrices are unchanged.

Verified locally: SQL Server provider **10.0.11**, net10.0 / runtime **10.0.12**,
Windows LocalDB engine **17.0.4025.3**. This does not certify SQL Server EF 7/8/9,
Azure SQL, Linux containers, every SQL Server version or every collation.
Those EF versions are a separate follow-up; pooling/additional SQL Server mappings
and a SQL Server packed consumer remain optional future coverage.

Runtime values stay out of executed SQL text for these lifted cases. They remain
in DbParameter.Value and can be exposed by explicit sensitive logging, bind profiling
or custom telemetry. Authored constants may appear in SQL. ToQueryString intentionally
rejects lifted captures before provider rendering. Existing compiled/AOT/provider
and same-context concurrency limitations remain documented.

Production files changed: **none**. No package publication, merge, force push,
GitHub comment, production database or credential change occurred.
Final documentation-head results are also recorded in the PR description and
[current checks](https://github.com/Raffinert/Expressions/pull/7/checks).
