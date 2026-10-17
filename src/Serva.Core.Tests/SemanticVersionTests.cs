using Serva.Core.Models;

namespace Serva.Core.Tests;

public class SemanticVersionTests
{
    [Theory]
    [InlineData("v0.2.0", "0.1.0")]
    [InlineData("0.2", "0.1.9")]
    [InlineData("1.0.0", "0.9.9")]
    [InlineData("0.1.1", "0.1.0")]
    [InlineData("1.0.0-beta.3", "1.0.0-beta.2")]
    public void NewerVersion_ComparesGreater(string latest, string current) =>
        Assert.True(Compare(latest, current) > 0);

    [Theory]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("v0.1.0", "0.1.0")]
    [InlineData("0.1", "0.1.0")]
    [InlineData("1.2.3+build.7", "1.2.3")]
    public void SameVersion_ComparesEqual(string left, string right) =>
        Assert.Equal(0, Compare(left, right));

    [Theory]
    [InlineData("0.1.0", "0.2.0")]
    [InlineData("1.0.0-beta", "1.0.0")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.10")]
    [InlineData("1.0.0-alpha", "1.0.0-beta")]
    [InlineData("1.0.0-1", "1.0.0-alpha")]
    public void OlderVersion_ComparesLess(string left, string right) =>
        Assert.True(Compare(left, right) < 0);

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("1.x.0")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3.4.5")]
    public void InvalidText_FailsToParse(string? text) =>
        Assert.False(SemanticVersion.TryParse(text, out _));

    [Fact]
    public void ParsedParts_AreExposed()
    {
        Assert.True(SemanticVersion.TryParse("v0.2.3-beta.1+build", out SemanticVersion version));

        Assert.Equal(0, version.Major);
        Assert.Equal(2, version.Minor);
        Assert.Equal(3, version.Patch);
        Assert.Equal("beta.1", version.PreRelease);
    }

    private static int Compare(string latest, string current)
    {
        Assert.True(SemanticVersion.TryParse(latest, out SemanticVersion left));
        Assert.True(SemanticVersion.TryParse(current, out SemanticVersion right));

        return left.CompareTo(right);
    }
}
