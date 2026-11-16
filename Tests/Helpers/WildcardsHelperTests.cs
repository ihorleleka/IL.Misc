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

    [Theory]
    [InlineData(null, "*")]
    [InlineData(null, "")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void MatchesWildcard_NullNeverMatches(string? input, string? pattern)
    {
        Assert.False(input.MatchesWildcard(pattern));
    }

    [Fact]
    public void MatchesWildcard_AgreesWithReferenceMatcherForAllShortInputs()
    {
        static IEnumerable<string> Words(string alphabet, int length)
        {
            yield return "";
            if (length == 0) yield break;
            foreach (var prefix in Words(alphabet, length - 1))
            foreach (var character in alphabet)
                yield return prefix + character;
        }

        static bool ReferenceMatch(string input, string pattern)
        {
            var matches = new bool[input.Length + 1, pattern.Length + 1];
            matches[0, 0] = true;
            for (var j = 1; j <= pattern.Length; j++)
            {
                matches[0, j] = pattern[j - 1] == '*' && matches[0, j - 1];
                for (var i = 1; i <= input.Length; i++)
                    matches[i, j] = pattern[j - 1] == '*'
                        ? matches[i, j - 1] || matches[i - 1, j]
                        : (pattern[j - 1] == '?' || pattern[j - 1] == input[i - 1]) && matches[i - 1, j - 1];
            }
            return matches[input.Length, pattern.Length];
        }

        foreach (var input in Words("ab", 4).Distinct())
        foreach (var pattern in Words("ab*?", 4).Distinct())
            Assert.Equal(ReferenceMatch(input, pattern), input.MatchesWildcard(pattern));
    }

    [Fact]
    public void MatchesWildcard_WithNullInput_ShouldReturnFalse()
    {
        // Arrange
        string? input = null;
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
        string? wildcard = null;

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