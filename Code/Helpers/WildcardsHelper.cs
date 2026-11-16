namespace IL.Misc.Helpers;

/// <summary>Case-sensitive ordinal wildcard matching without per-call allocations.</summary>
public static class WildcardsHelper
{
    /// <summary>Matches '*' (zero or more UTF-16 characters) and '?' (one UTF-16 character).</summary>
    /// <remarks>Null input or pattern never matches. Worst-case running time is O(input length × pattern length).</remarks>
    public static bool MatchesWildcard(this string? input, string? wildcard)
    {
        return input is not null && wildcard is not null && MatchesWildcardSpan(input.AsSpan(), wildcard.AsSpan());
    }

    private static bool MatchesWildcardSpan(ReadOnlySpan<char> input, ReadOnlySpan<char> wildcard)
    {
        int inputIndex = 0, wildcardIndex = 0;
        int inputLength = input.Length, wildcardLength = wildcard.Length;
        int starIndex = -1, inputBacktrackIndex = -1;

        while (inputIndex < inputLength)
        {
            if (wildcardIndex < wildcardLength && (wildcard[wildcardIndex] == '?' || wildcard[wildcardIndex] == input[inputIndex]))
            {
                inputIndex++;
                wildcardIndex++;
            }
            else if (wildcardIndex < wildcardLength && wildcard[wildcardIndex] == '*')
            {
                starIndex = wildcardIndex;
                inputBacktrackIndex = inputIndex;
                wildcardIndex++;
            }
            else if (starIndex != -1)
            {
                wildcardIndex = starIndex + 1;
                inputIndex = ++inputBacktrackIndex;
            }
            else
            {
                return false;
            }
        }

        while (wildcardIndex < wildcardLength && wildcard[wildcardIndex] == '*')
        {
            wildcardIndex++;
        }

        return wildcardIndex == wildcardLength;
    }
}