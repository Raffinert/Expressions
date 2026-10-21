using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Raffinert.Expressions;

internal sealed class RaffinertOptionsExtension : IDbContextOptionsExtension
{
    internal IReadOnlyList<Func<Expression, Expression>> Transformers { get; }

    public RaffinertOptionsExtension() : this([]) { }

    private RaffinertOptionsExtension(IReadOnlyList<Func<Expression, Expression>> transformers) =>
        Transformers = transformers;

    internal RaffinertOptionsExtension WithTransformers(Func<Expression, Expression>[] added) =>
        new([.. Transformers, .. added]);

    public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services) => NativeExtractionQueryCompiler.Decorate(services);

    public void Validate(IDbContextOptions options) { }

    // Service registrations do not depend on callbacks: the scoped compiler resolves
    // the current context's IDbContextOptions rather than capturing a callback globally.
    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;
        public override string LogFragment => "RaffinertExpressions ";
        public override int GetServiceProviderHashCode() => 0;
        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;
        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) => debugInfo["Raffinert:Expansion"] = "1";
    }
}
