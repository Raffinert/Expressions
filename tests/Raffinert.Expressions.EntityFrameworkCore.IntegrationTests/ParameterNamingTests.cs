using System.Linq.Expressions;

namespace Raffinert.Expressions.EntityFrameworkCore.IntegrationTests;

public class ParameterNamingTests
{
    private static MemberExpression Capture<T>(Expression<Func<T>> expression) => (MemberExpression)expression.Body;

    [Fact]
    public void LocalNamesAndRepeatedOccurrencesUsePerBaseSuffixes()
    {
        var threshold = 100;
        var customerEmail = "synthetic-private@example.invalid";
        var generator = new RaffinertParameterNameGenerator([]);
        Assert.Equal("__raffinert_threshold_0", generator.Next(Capture(() => threshold)));
        Assert.Equal("__raffinert_customerEmail_0", generator.Next(Capture(() => customerEmail)));
        Assert.Equal("__raffinert_threshold_1", generator.Next(Capture(() => threshold)));
        Assert.Equal("__raffinert_threshold_2", generator.Next(Capture(() => threshold)));
    }

    [Fact]
    public void NestedConvertedAndStaticPathsNeverEvaluateObjects()
    {
        var settings = new Settings();
        var generator = new RaffinertParameterNameGenerator([]);
        Assert.Equal("__raffinert_settings_MinPrice_0", generator.Next(Capture(() => settings.MinPrice)));
        Assert.Equal("__raffinert_settings_MinPrice_1", generator.Next(Capture(() => ((Settings)(object)settings).MinPrice)));
        Assert.Equal("__raffinert_StaticPrice_0", generator.Next(Capture(() => Settings.StaticPrice)));
    }

    [Fact]
    public void ExistingNamesCollideCaseInsensitively()
    {
        var threshold = 1;
        var generator = new RaffinertParameterNameGenerator(["__RAFFINERT_threshold_0", "__raffinert_threshold_2"]);
        Assert.Equal("__raffinert_threshold_1", generator.Next(Capture(() => threshold)));
        Assert.Equal("__raffinert_threshold_3", generator.Next(Capture(() => threshold)));
    }

    [Theory]
    [InlineData("a...b", "a_b")]
    [InlineData("__a :@$ b__", "a_b")]
    [InlineData("", "p")]
    [InlineData("你好!", "p")]
    public void NormalizationUsesOnlyAsciiMetadata(string path, string expected)
    {
        Assert.Equal(expected, RaffinertParameterNameGenerator.Normalize(path));
    }

    [Fact]
    public void NormalizedAndTruncatedCollisionsRemainUniqueAndBounded()
    {
        var generator = new RaffinertParameterNameGenerator([]);
        Assert.Equal("__raffinert_a_b_0", generator.NextPath("a.b"));
        Assert.Equal("__raffinert_a_b_1", generator.NextPath("a:b"));
        var path = new string('a', 150);
        var other = new RaffinertParameterNameGenerator([]);
        for (var i = 0; i < 12; i++)
        {
            var name = generator.NextPath(path + i);
            Assert.Equal(other.NextPath(path + i), name);
            Assert.True(name.Length <= 96);
            Assert.Matches("^__raffinert_[A-Za-z0-9_]+_[0-9]+$", name);
            Assert.EndsWith("_" + i, name);
        }
    }

    [Fact]
    public void IndependentGeneratorsIgnoreChangedRuntimeValues()
    {
        var threshold = 1;
        var first = new RaffinertParameterNameGenerator([]).Next(Capture(() => threshold));
        threshold = 999;
        Assert.Equal(first, new RaffinertParameterNameGenerator([]).Next(Capture(() => threshold)));
    }

    private sealed class Settings
    {
        public int MinPrice => throw new InvalidOperationException("Getter must not run.");
        public static int StaticPrice => throw new InvalidOperationException("Getter must not run.");
        public override string ToString() => throw new InvalidOperationException("Serialization must not run.");
    }
}
