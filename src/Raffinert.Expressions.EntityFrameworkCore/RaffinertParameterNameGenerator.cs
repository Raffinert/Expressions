using System.Globalization;
using System.Linq.Expressions;
using System.Text;

namespace Raffinert.Expressions;

// Names use expression metadata only; allocation belongs to one preparation.
internal sealed class RaffinertParameterNameGenerator(IEnumerable<string> usedNames)
{
    internal const string Prefix = "__raffinert_";
    private const int MaximumLength = 96;
    private readonly HashSet<string> _used = new(usedNames, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _next = new(StringComparer.OrdinalIgnoreCase);

    public string Next(MemberExpression capture)
    {
        var members = new Stack<string>();
        Expression? node = capture;
        while (true)
        {
            if (node is MemberExpression member)
            {
                // Compiler-generated closure links are implementation details.
                if (!member.Member.Name.StartsWith("<", StringComparison.Ordinal)) members.Push(member.Member.Name);
                node = member.Expression;
            }
            else if (node is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs } conversion)
                node = conversion.Operand;
            else break;
        }
        return NextPath(string.Join("_", members));
    }

    internal string NextPath(string path)
    {
        // A fixed base budget leaves room for any Int32 suffix, including growth.
        var normalized = Normalize(path);
        var nameBase = normalized[..Math.Min(normalized.Length, MaximumLength - Prefix.Length - 11)].TrimEnd('_');
        if (nameBase.Length == 0) nameBase = "p";
        _next.TryGetValue(nameBase, out var index);
        while (true)
        {
            var name = Prefix + nameBase + "_" + index.ToString(CultureInfo.InvariantCulture);
            index = checked(index + 1);
            if (!_used.Add(name)) continue;
            _next[nameBase] = index;
            return name;
        }
    }

    internal static string Normalize(string path)
    {
        var result = new StringBuilder();
        foreach (var character in path)
        {
            if (character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_')
                result.Append(character);
            else if (result.Length > 0 && result[^1] != '_') result.Append('_');
        }
        var normalized = result.ToString().Trim('_');
        return normalized.Length == 0 ? "p" : normalized;
    }
}
