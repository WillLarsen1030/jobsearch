using System.Text.RegularExpressions;

namespace JobSearch.Application.Applications;

public static partial class QuestionMatcher
{
    public static string Normalize(string value) =>
        Whitespace().Replace(NonWord().Replace(value.ToLowerInvariant(), " "), " ").Trim();

    public static AnswerMatch Match(string question, IEnumerable<ApplicationAnswer> answers)
    {
        var normalized = Normalize(question);
        ApplicationAnswer? best = null;
        decimal bestScore = 0;
        foreach (var answer in answers)
        {
            var pattern = string.IsNullOrWhiteSpace(answer.NormalizedPattern)
                ? Normalize(answer.Pattern)
                : answer.NormalizedPattern;
            var score = Similarity(normalized, pattern) * answer.Confidence;
            if (score > bestScore)
            {
                best = answer;
                bestScore = score;
            }
        }

        return new AnswerMatch(best, Math.Round(bestScore, 2));
    }

    private static decimal Similarity(string question, string pattern)
    {
        if (question == pattern) return 1m;
        if (question.Contains(pattern, StringComparison.Ordinal) || pattern.Contains(question, StringComparison.Ordinal)) return .94m;
        if (question.Contains("sponsor", StringComparison.Ordinal) && pattern.Contains("sponsor", StringComparison.Ordinal) &&
            question.Contains("require", StringComparison.Ordinal) && pattern.Contains("require", StringComparison.Ordinal)) return .96m;
        if (question.Contains("authorized", StringComparison.Ordinal) && pattern.Contains("authorized", StringComparison.Ordinal) &&
            question.Contains("work", StringComparison.Ordinal) && pattern.Contains("work", StringComparison.Ordinal)) return .96m;
        var a = question.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var b = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (a.Count == 0 || b.Count == 0) return 0;
        var overlap = a.Intersect(b).Count();
        return 2m * overlap / (a.Count + b.Count);
    }

    [GeneratedRegex("[^a-z0-9+#]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonWord();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
