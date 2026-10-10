using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Raffinert.Expressions.EntityFrameworkCore.IntegrationTests;

#pragma warning disable EF1001 // Test the exact EF10 compiler contract and provider descriptor ownership.
public class NativeCompilerContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DecorationPreservesScopedProviderCompilerAndDisposesItOnce(bool factory)
    {
        var services = new ServiceCollection();
        if (factory) services.AddScoped<IQueryCompiler>(_ => new RecordingCompiler());
        else services.AddScoped<IQueryCompiler, RecordingCompiler>();
        NativeExtractionQueryCompiler.Decorate(services);
        using var provider = services.BuildServiceProvider();
        RecordingCompiler first;
        using (var scope = provider.CreateScope())
        {
            var compiler = scope.ServiceProvider.GetRequiredService<IQueryCompiler>();
            Assert.Same(compiler, scope.ServiceProvider.GetRequiredService<IQueryCompiler>());
            var query = Expression.Constant(7);
            Assert.Equal(7, compiler.Execute<int>(query));
            first = RecordingCompiler.Last!;
            Assert.Same(query, first.Query);
            Assert.Equal(1, first.Calls);
            using var other = provider.CreateScope();
            Assert.NotSame(compiler, other.ServiceProvider.GetRequiredService<IQueryCompiler>());
        }
        Assert.Equal(1, first.Disposals);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("singleton")]
    [InlineData("instance")]
    [InlineData("duplicate")]
    public void UnsupportedDescriptorsFailEarly(string mode)
    {
        var services = new ServiceCollection();
        if (mode == "singleton") services.AddSingleton<IQueryCompiler, RecordingCompiler>();
        if (mode == "instance") services.AddSingleton<IQueryCompiler>(new RecordingCompiler());
        if (mode == "duplicate")
        {
            services.AddScoped<IQueryCompiler, RecordingCompiler>();
            services.AddScoped<IQueryCompiler, RecordingCompiler>();
        }
        Assert.Contains("one scoped", Assert.Throws<InvalidOperationException>(() => NativeExtractionQueryCompiler.Decorate(services)).Message);
    }

    [Fact]
    public void AsyncAndCompiledMembersForwardExpressionAndTokenOnce()
    {
        var inner = new RecordingCompiler();
        var compiler = new NativeExtractionQueryCompiler(inner);
        var query = Expression.Constant(7);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Equal(7, compiler.ExecuteAsync<int>(query, cancellation.Token));
        Assert.Equal(cancellation.Token, inner.Token);
        Assert.Same(query, inner.Query);
        Assert.Same(inner.Sync, compiler.CreateCompiledQuery<int>(query));
        Assert.Same(inner.Async, compiler.CreateCompiledAsyncQuery<int>(query));
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public void NoMarkerPrecompilationForwardsUnmodifiedAndMarkersFailBeforeProvider()
    {
        var inner = new RecordingCompiler();
        var compiler = new NativeExtractionQueryCompiler(inner);
        // Reflection exercises the experimental boundary without adding an EF9100 exception at the call site.
        var method = typeof(NativeExtractionQueryCompiler).GetMethod("PrecompileQuery")!.MakeGenericMethod(typeof(int));
        var query = Expression.Constant(7);
        Assert.Same(inner.Precompiled, method.Invoke(compiler, [query, true]));
        Assert.Same(query, inner.Query);
        Assert.True(inner.IsAsync);
        var condition = Condition<OrderRow>.True;
        Expression<Func<OrderRow, bool>> marker = row => condition.Invoke(row);
        var failure = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(compiler, [marker, false]));
        Assert.IsType<NotSupportedException>(failure.InnerException);
        Assert.Equal(1, inner.Calls);
        Func<OrderRow, bool> callback = condition.Invoke;
        Expression<Func<OrderRow, bool>> delegated = row => callback(row);
        var delegatedFailure = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(compiler, [delegated, false]));
        Assert.IsType<NotSupportedException>(delegatedFailure.InnerException);
        Assert.Equal(1, inner.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomCompilerDecoratorComposesInEitherExtensionOrder(bool first)
    {
        var calls = new CompilerCalls();
        await using var f = await SqliteFixture.CreateAsync(false, builder =>
        {
            if (first) builder.UseRaffinertExpressions();
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new ObservingCompilerExtension(calls));
            if (!first) builder.UseRaffinertExpressions();
            builder.UseRaffinertExpressions();
        });
        calls.Count = 0;
        var threshold = 1000;
        var condition = Condition<OrderRow>.Create(x => x.TotalCents > threshold);
        Assert.Equal(3, await f.Db.Orders.Where(x => condition.Invoke(x)).CountAsync());
        threshold = 10000;
        Assert.Equal(1, f.Db.Orders.Count(x => condition.Invoke(x)));
        Assert.Equal(2, calls.Count);
    }

    private sealed class CompilerCalls { public int Count; }

    private sealed class ObservingCompilerExtension(CompilerCalls calls) : IDbContextOptionsExtension
    {
        public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);
        public void Validate(IDbContextOptions options) { }
        public void ApplyServices(IServiceCollection services)
        {
            var original = services.Last(x => x.ServiceType == typeof(IQueryCompiler) && !x.IsKeyedService);
            services.Remove(original);
            services.AddScoped<IQueryCompiler>(provider => new ObservingCompiler((IQueryCompiler)(original.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, original.ImplementationType!)), calls));
        }
        private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
        {
            public override bool IsDatabaseProvider => false;
            public override string LogFragment => "ObservingCompiler ";
            public override int GetServiceProviderHashCode() => 0;
            public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => false;
            public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) => debugInfo["Tests:Compiler"] = "1";
        }
    }

    private sealed class ObservingCompiler(IQueryCompiler inner, CompilerCalls calls) : IQueryCompiler
    {
        public TResult Execute<TResult>(Expression query) { calls.Count++; return inner.Execute<TResult>(query); }
        public TResult ExecuteAsync<TResult>(Expression query, CancellationToken token) { calls.Count++; return inner.ExecuteAsync<TResult>(query, token); }
        public Func<QueryContext, TResult> CreateCompiledQuery<TResult>(Expression query) => inner.CreateCompiledQuery<TResult>(query);
        public Func<QueryContext, TResult> CreateCompiledAsyncQuery<TResult>(Expression query) => inner.CreateCompiledAsyncQuery<TResult>(query);
#pragma warning disable EF9100 // Owner-authorized: test decorator forwards the experimental member only.
        public Expression<Func<QueryContext, TResult>> PrecompileQuery<TResult>(Expression query, bool async) => inner.PrecompileQuery<TResult>(query, async);
#pragma warning restore EF9100
    }

    public sealed class RecordingCompiler : IQueryCompiler, IDisposable
    {
        public static RecordingCompiler? Last;
        public Expression? Query;
        public CancellationToken Token;
        public int Calls;
        public int Disposals;
        public bool IsAsync;
        public readonly Func<QueryContext, int> Sync = _ => 7;
        public readonly Func<QueryContext, int> Async = _ => 7;
        public readonly Expression<Func<QueryContext, int>> Precompiled = _ => 7;
        public RecordingCompiler() { Last = this; }
        private T Record<T>(Expression query, T result) { Query = query; Calls++; return result; }
        public TResult Execute<TResult>(Expression query) => Record(query, (TResult)(object)7);
        public TResult ExecuteAsync<TResult>(Expression query, CancellationToken token) { Token = token; return Execute<TResult>(query); }
        public Func<QueryContext, TResult> CreateCompiledQuery<TResult>(Expression query) => Record(query, (Func<QueryContext, TResult>)(object)Sync);
        public Func<QueryContext, TResult> CreateCompiledAsyncQuery<TResult>(Expression query) => Record(query, (Func<QueryContext, TResult>)(object)Async);
        public Expression<Func<QueryContext, TResult>> PrecompileQuery<TResult>(Expression query, bool async)
        {
            IsAsync = async;
            return Record(query, (Expression<Func<QueryContext, TResult>>)(object)Precompiled);
        }
        public void Dispose() { Disposals++; }
    }
}
#pragma warning restore EF1001
