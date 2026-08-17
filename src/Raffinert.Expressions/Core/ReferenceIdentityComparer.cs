using System.Runtime.CompilerServices;

namespace Raffinert.Expressions;

internal sealed class ReferenceIdentityComparer : IEqualityComparer<object>
{
    public static readonly ReferenceIdentityComparer Instance = new();

    private ReferenceIdentityComparer()
    {
    }

    public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

    public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
}
