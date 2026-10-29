# EF Core adapter architecture

Internal technical reference for `Raffinert.Expressions.EntityFrameworkCore` 2.0.0.
Consumer setup and supported behavior are documented in the
[EF integration guide](../efcore-integration.md).

## Ownership and execution pipeline

Raffinert owns expression composition and expansion. EF owns evaluation, native
parameter extraction, names, bindings, cache keys, compilation and SQL translation.
The adapter targets `net10.0` with EF Core/Relational `[10.0.11,11.0.0)`; Core and
QuerySyntax have no EF dependency.

Ordinary execution follows this sequence:

1. EF's query provider calls the registered scoped `IQueryCompiler`.
2. `NativeExtractionQueryCompiler.Execute` or `ExecuteAsync` expands the tree with
   the shared Core `ExpressionExpander`.
3. The decorator forwards the expanded tree once to the original provider compiler,
   passing the asynchronous cancellation token unchanged.
4. EF creates its execution context, extracts parameters and prepares its native
   cache key. It compiles on a cache miss and executes with current bindings.

Expansion must precede native extraction: a wrapper body can introduce captured
values, directives and provider expressions that EF needs to process. The ordinary
query compilation interceptor runs after extraction/cache lookup, so it cannot
serve as this boundary for runtime wrapper captures.

Using the provider compiler preserves native semantics for computed operands,
collections, client evaluation, metadata/functions and provider-specific SQL. The
adapter neither calls/copies `ExpressionTreeFuncletizer` nor constructs another
compiler or query provider. There is no parallel parameter binder, naming policy,
execution state or cache-key pipeline. Cache reuse depends on EF's expression shape
and policy; stable SQL text is not guaranteed for every directive/collection mode.

Sources: [NativeExtractionQueryCompiler](../../src/Raffinert.Expressions.EntityFrameworkCore/NativeExtractionQueryCompiler.cs),
[ExpressionExpander](../../src/Raffinert.Expressions/Core/ExpressionExpander.cs).

## Scoped decoration and service ownership

`UseRaffinertExpressions()` installs an options extension and the compatibility
interceptor. Repeated typed/untyped registration calls are idempotent. Configure
the provider first so its compiler services are available.

`RaffinertOptionsExtension.ApplyServices` requires one unkeyed scoped `IQueryCompiler`
descriptor. Its implementation type or factory is preserved under a private keyed
service descriptor; the unkeyed service becomes the Raffinert decorator. Resolving
the private key avoids recursive resolution of the decorated service. DI owns the
original compiler, its scope and disposal; the decorator does not dispose it again.

Missing, duplicate and non-scoped compiler registrations fail early. Instance
registrations are outside the supported descriptor contract. Custom compiler
decorators are covered in both tested extension-registration orders, including
scoped lifetime and single-disposal checks. This does not certify arbitrary service
replacement combinations.

Sources: [registration helper](../../src/Raffinert.Expressions.EntityFrameworkCore/RaffinertDbContextOptionsBuilderExtensions.cs),
[options extension](../../src/Raffinert.Expressions.EntityFrameworkCore/RaffinertOptionsExtension.cs).

## Compatibility interceptor

The public `RaffinertExpressionInterceptor` delegates to `ClosedWrapperExpansion`.
Ordinary queries arrive with their wrappers expanded, so this path normally leaves
them unchanged. Its purpose is closed-wrapper compatibility during EF compiled-query
creation and standalone interception.

When this path expands a wrapper, `ClosedOperandVisitor` validates its closed
operands, rejects runtime member captures and embedded `EF.Parameter`/`EF.Constant`,
and normalizes supported CLR default nodes. It does not extract names or bind values.
Standalone interception can resolve closed constant targets, but cannot recover
ordinary captured wrapper targets that EF has already extracted.

Sources: [interceptor](../../src/Raffinert.Expressions.EntityFrameworkCore/RaffinertExpressionInterceptor.cs),
[closed-wrapper expansion](../../src/Raffinert.Expressions.EntityFrameworkCore/ClosedWrapperExpansion.cs).

## Compiled and precompiled queries

`CreateCompiledQuery` and `CreateCompiledAsyncQuery` forward their original trees
unchanged. EF owns compiled-query creation; the compatibility interceptor expands
stable closed wrappers on that path. Scalar arguments to the compiled delegate
remain EF parameters. Captures in wrapper bodies and wrapper-valued delegate
parameters are unsupported. Closed wrappers are fixed at compiled delegate creation,
so wrapper reassignment requires ordinary execution or a new compiled delegate.

The experimental `PrecompileQuery` member checks whether Raffinert expansion changes
the tree. Detected expansion throws `NotSupportedException`; an unchanged tree is
forwarded once to the original compiler with the original asynchronous flag. Boundary
forwarding tests use a recording compiler. They do not certify native precompiled-code
generation or NativeAOT. Compiled-wrapper directive/collection scenarios beyond the
documented supported scope are not certified.

## Delegate classification and expansion-time evaluation

Exact `Invoke`/`InvokeOrDefault` markers on native wrappers and their typed interface
are resolved by `SafeValueEvaluator`, then expanded through parameter substitution.
Cycle detection and per-wrapper nested-expression caching remain in Core. Arbitrary
factory methods and row-dependent wrapper targets are not evaluated for resolution.

Method-group resolution checks the represented `MethodInfo` is an exact Raffinert
marker before resolving its receiver. Constants and constant/static-rooted field-only
delegate chains preserve captured-delegate expansion. The entire receiver chain is
checked structurally; a field whose receiver contains a property or constructor is
not eagerly resolved merely because its final member is a field.

Opaque delegate properties and property-backed fields are resolved at actual
`InvocationExpression` targets and delegate-typed method arguments. Each such callback
occurrence can execute a getter to discover whether its runtime delegate targets a
Raffinert wrapper. Carried opaque values are left to the execution provider.

A delegate type alone cannot identify its runtime target. Preserving opaque marker
callback support therefore requires runtime evaluation in callback positions; no
universal side-effect-free classification guarantee exists there. The structural
check does not invoke getters/constructors or add an evaluator. Ordinary captured
expressions after expansion are evaluated entirely by EF.

## Diagnostics and security rationale

`ToQueryString()` uses native EF rendering, which can include parameter values even
with sensitive-data logging disabled. Native diagnostics are adopted to keep one
coherent provider evaluation/binding/rendering contract rather than reconstructing
EF behavior in a query-result wrapper or parameter-prefix filter. Consumers must
handle rendered SQL and telemetry as potentially sensitive.

Native execution/extraction exceptions are forwarded without an adapter-wide
sanitizer. The expansion boundary sanitizes getter/constructor failures while resolving
wrapper targets by omitting their inner exception details. This is a limited boundary,
not a general privacy guarantee for SQL, EF exceptions or arbitrary user telemetry.

## Internal API dependency and warning policy

`IQueryCompiler` is an EF10 internal contract. Its method surface, service-registration
shape and extraction/interception ordering can change across patches. The bounded
dependency range is not evidence that every patch is validated. Patch upgrades require
provider, cache/binding, DI/disposal, compiled-query and packed-consumer checks.

Local `EF1001` pragmas cover the compiler boundary and its contract tests only.
`EF9100` is confined to implementations forwarding `PrecompileQuery`, whose EF10
interface member is experimental. These exceptions allow the complete compiler
contract without changing `TreatWarningsAsErrors` or adding global `NoWarn` entries.
No other diagnostics are suppressed. The experimental annotation is documented in
[EF10 IQueryCompiler source](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/IQueryCompiler.cs).

## Compatibility evidence and engineering records

Provider coverage includes SQLite on Windows/Linux and real current-user SQL Server
LocalDB on Windows. LocalDB ownership/initialization/cleanup protections live in
[LocalDbFixture](../../tests/Raffinert.Expressions.EntityFrameworkCore.SqlServerTests/LocalDbFixture.cs)
and its safety tests. Other providers, Azure SQL, other server versions/collations and
custom `HasDbFunction` mappings are outside the certified matrix.

Historical investigations, actual test totals, RED/GREEN output, commit hashes and
exact-head CI evidence belong in engineering records and PR validation:

- [Native extraction experiment](../experiments/ef10-native-extraction-experiment.md).
- [Delegate getter review](../experiments/pr10-delegate-getter-review.md).

These records provide evidence and trade-off analysis; they are not prerequisites
for using the public API.
