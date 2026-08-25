namespace Raffinert.Expressions.NetFrameworkCompatibility;

using Microsoft.EntityFrameworkCore;

/// <summary>Provides compile-time coverage for the .NET Framework 4.7.2 async query surface.</summary>
public static class NetFrameworkAsyncCompatibility
{
    /// <summary>Builds and asynchronously materializes a query through the provider's async extension.</summary>
    public static Task<List<T>> MaterializeAsync<T>(IQueryable<T> source, Condition<T> condition)
    {
        return source.Where(condition).ToListAsync();
    }
}
