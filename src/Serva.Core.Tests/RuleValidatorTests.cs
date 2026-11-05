using Serva.Core.Services;

namespace Serva.Core.Tests;

public class RuleValidatorTests
{
    [Theory]
    [InlineData("**/node_modules/**")]
    [InlineData("build")]
    [InlineData(".minecraft")]
    [InlineData(@"src\generated\*")]
    [InlineData(" Thumbs.db ")]
    public void ValidRules_Pass(string pattern)
    {
        Assert.Equal(RuleValidationError.None, RuleValidator.Validate(pattern));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_IsRejected(string? pattern)
    {
        Assert.Equal(RuleValidationError.Empty, RuleValidator.Validate(pattern));
    }

    [Fact]
    public void TooLong_IsRejected()
    {
        string pattern = new('a', RuleValidator.MaxLength + 1);

        Assert.Equal(RuleValidationError.TooLong, RuleValidator.Validate(pattern));
    }

    [Theory]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a\"b")]
    [InlineData("a|b")]
    [InlineData("C:foo")]
    public void InvalidCharacters_AreRejected(string pattern)
    {
        Assert.Equal(RuleValidationError.InvalidCharacters, RuleValidator.Validate(pattern));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("**")]
    [InlineData("/")]
    [InlineData("**/")]
    [InlineData("?/")]
    public void WildcardOnly_IsRejected(string pattern)
    {
        Assert.Equal(RuleValidationError.WildcardOnly, RuleValidator.Validate(pattern));
    }

    [Fact]
    public void Duplicate_IsRejected_CaseInsensitive()
    {
        Assert.Equal(
            RuleValidationError.Duplicate,
            RuleValidator.Validate("  THUMBS.DB ", ["Thumbs.db"]));
    }

    [Fact]
    public void Duplicate_IsIgnoredWhenListIsEmpty()
    {
        Assert.Equal(RuleValidationError.None, RuleValidator.Validate("Thumbs.db", []));
    }
}
