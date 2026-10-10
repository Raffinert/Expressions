using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.Extensions.DependencyInjection;

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
    [InlineData("duplicate")]
    public void UnsupportedDescriptorsFailEarly(string mode)
    {
        var services = new ServiceCollection();
        if (mode == "singleton") services.AddSingleton<IQueryCompiler, RecordingCompiler>();
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
