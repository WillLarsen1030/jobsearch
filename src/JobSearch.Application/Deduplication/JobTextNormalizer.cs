using System.Text;

namespace JobSearch.Application.Deduplication;

public static class JobTextNormalizer
{
    private static readonly HashSet<string> CompanySuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "corp", "corporation", "inc", "incorporated", "llc", "ltd", "limited", "company", "co"
    };

    public static string NormalizeCompany(string value) =>
        NormalizeWords(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !CompanySuffixes.Contains(word))
            .Aggregate(new StringBuilder(), (builder, word) => builder.Append(word).Append(' '))
            .ToString()
            .Trim();

    public static string NormalizeTitle(string value) => NormalizeWords(value)
        .Replace("dot net", "dotnet", StringComparison.Ordinal)
        .Replace("asp net", "aspnet", StringComparison.Ordinal)
        .Replace("sr ", "senior ", StringComparison.Ordinal);

    public static string NormalizeLocation(string value) => NormalizeWords(value)
        .Replace("united states of america", "us", StringComparison.Ordinal)
        .Replace("united states", "us", StringComparison.Ordinal)
        .Replace("usa", "us", StringComparison.Ordinal);

    public static string CanonicalizeUrl(Uri url)
    {
        var builder = new UriBuilder(url)
        {
            Fragment = string.Empty,
            Query = string.Empty,
            Host = url.Host.ToLowerInvariant()
        };

        return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/').ToLowerInvariant();
    }

    public static double Similarity(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return 0;
        }

        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return 1;
        }

        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var union = leftTokens.Union(rightTokens).Count();
        var tokenScore = union == 0 ? 0 : (double)leftTokens.Intersect(rightTokens).Count() / union;
        var editScore = 1d - ((double)LevenshteinDistance(left, right) / Math.Max(left.Length, right.Length));

        return Math.Max(tokenScore, editScore);
    }

    private static string NormalizeWords(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static int LevenshteinDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                var substitution = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
