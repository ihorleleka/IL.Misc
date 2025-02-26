using IL.Misc.Helpers;
using Xunit;

namespace IL.Misc.Tests.Helpers;

public class WildcardsHelperTests
{
    [Theory]
    [InlineData("teststring", "teststring", true)] // Exact match
    [InlineData("teststring", "test*", true)] // Wildcard at the end
    [InlineData("teststring", "*string", true)] // Wildcard at the beginning
    [InlineData("teststring", "t*st*ng", true)] // Wildcards in the middle
    [InlineData("teststring", "t?st*ng", true)] // Wildcard with '?'
    [InlineData("teststring", "t*st*ngx", false)] // No match
    [InlineData("teststring", "test", false)] // Partial match (no wildcard)
    [InlineData("teststring", "*", true)] // Match everything
    [InlineData("", "*", true)] // Empty input, wildcard matches everything
    [InlineData("string.Empty", "test", false)] // Empty input, no match
    [InlineData("teststring", "test*string", true)] // Valid wildcard pattern
    [InlineData("teststring", "test*string*", true)] // Valid wildcard pattern
    [InlineData("teststring", "test?string", false)] // '?' mismatch
    [InlineData("teststring", "test?tring", true)] // '?' matches one character
    public void MatchesWildcard_ShouldReturnExpectedResult(string input, string wildcard, bool expectedResult)
    {
        // Act
        var result = input.MatchesWildcard(wildcard);

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public void MatchesWildcard_WithNullInput_ShouldReturnFalse()
    {
        // Arrange
        string input = null;
        var wildcard = "test*";

        // Act
        var result = input.MatchesWildcard(wildcard);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MatchesWildcard_WithNullWildcard_ShouldReturnFalse()
    {
        // Arrange
        var input = "teststring";
        string wildcard = null;

        // Act
        var result = input.MatchesWildcard(wildcard);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MatchesWildcard_WithEmptyInputAndEmptyWildcard_ShouldReturnTrue()
    {
        // Arrange
        var input = string.Empty;
        var wildcard = string.Empty;

        // Act
        var result = input.MatchesWildcard(wildcard);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesWildcard_WithEmptyInputAndNonEmptyWildcard_ShouldMatchWildcard()
    {
        // Arrange
        var input = string.Empty;
        var wildcard = "*";

        // Act
        var result = input.MatchesWildcard(wildcard);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MatchesWildcard_WithNonEmptyInputAndEmptyWildcard_ShouldReturnFalse()
    {
        // Arrange
        var input = "teststring";
        var wildcard = string.Empty;

        // Act
        var result = input.MatchesWildcard(wildcard);

        // Assert
        Assert.False(result);
    }
}