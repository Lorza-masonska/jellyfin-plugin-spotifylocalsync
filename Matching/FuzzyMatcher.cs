using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.SpotifyLocalSync.Matching;

/// <summary>
/// Holds the result of matching a Spotify track against a local Jellyfin audio item.
/// </summary>
public sealed record MatchResult(
    string  SpotifyTrackId,
    string  SpotifyTitle,
    string  SpotifyArtist,
    Guid    LocalItemId,
    string  LocalTitle,
    string  LocalArtist,
    int     TitleScore,
    int     ArtistScore,
    int     CombinedScore
);

/// <summary>
/// Computes a fuzzy similarity score (0–100) between two strings using a blend of:
///   1. Normalisation  – strip accents, punctuation, case-fold, expand common abbreviations.
///   2. Levenshtein edit-distance ratio.
///   3. Token-set overlap (handles word-order differences, e.g. "feat." positions).
///
/// The scoring is deterministic and free of external dependencies.
/// </summary>
public static class FuzzyMatcher
{
    // ── Normalisation ───────────────────────────────────────────────

    // Common parenthetical suffixes that appear on one source but not the other.
    private static readonly Regex _suffixNoise = new(
        @"\s*[\(\[](feat\.?|ft\.?|with|explicit|radio edit|remaster(ed)?|live|acoustic|"     +
        @"single|version|edit|mix|extended|instrumental|cover|tribute|karaoke|"               +
        @"demo|bonus|commentary|original|re-?record)[^\)\]]*[\)\]]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex _nonAlphanumeric = new(
        @"[^a-z0-9\s]", RegexOptions.Compiled);

    private static readonly Regex _multiSpace = new(
        @"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Normalises a music metadata string for comparison:
    /// lowercase → remove accents → remove parenthetical noise → strip punctuation → collapse whitespace.
    /// </summary>
    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        // 1. Lowercase
        var s = input.ToLowerInvariant();

        // 2. Unicode normalise to NFD so accented chars split to base + combining
        s = s.Normalize(NormalizationForm.FormD);

        // 3. Remove combining diacritical marks (category Mn)
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        s = sb.ToString().Normalize(NormalizationForm.FormC);

        // 4. Strip common parenthetical noise ("(feat. X)", "(Remastered)", …)
        s = _suffixNoise.Replace(s, string.Empty);

        // 5. Replace common abbreviations / separators
        s = s.Replace("&", " and ")
             .Replace("+", " and ")
             .Replace(" vs ", " versus ")
             .Replace("'",  "")
             .Replace("`",  "");

        // 6. Keep only a-z, 0-9, space
        s = _nonAlphanumeric.Replace(s, " ");

        // 7. Collapse whitespace
        s = _multiSpace.Replace(s, " ").Trim();

        return s;
    }

    // ── Levenshtein distance ─────────────────────────────────────────

    /// <summary>Computes the Levenshtein edit distance between two strings.</summary>
    public static int EditDistance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var dp = new int[a.Length + 1, b.Length + 1];

        for (var i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) dp[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        for (var j = 1; j <= b.Length; j++)
        {
            var cost = a[i - 1] == b[j - 1] ? 0 : 1;
            dp[i, j] = Math.Min(
                Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                dp[i - 1, j - 1] + cost);
        }

        return dp[a.Length, b.Length];
    }

    /// <summary>Levenshtein similarity in range 0–100.</summary>
    public static int LevenshteinScore(string a, string b)
    {
        if (a == b) return 100;
        var maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0) return 100;
        var dist = EditDistance(a, b);
        return (int)Math.Round((1.0 - (double)dist / maxLen) * 100);
    }

    // ── Token-set score ──────────────────────────────────────────────

    /// <summary>
    /// Token-set ratio: sorts the tokens of both strings, intersects them,
    /// then scores the intersection against each remainder.
    /// This is resilient to word order differences and additional tokens.
    /// </summary>
    public static int TokenSetScore(string a, string b)
    {
        var tokA = new SortedSet<string>(a.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var tokB = new SortedSet<string>(b.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        var intersection = new SortedSet<string>(tokA);
        intersection.IntersectWith(tokB);

        var sortedIntersection = string.Join(" ", intersection);
        var remainA = new SortedSet<string>(tokA); remainA.ExceptWith(intersection);
        var remainB = new SortedSet<string>(tokB); remainB.ExceptWith(intersection);

        var t0 = sortedIntersection;
        var t1 = t0 + " " + string.Join(" ", remainA);
        var t2 = t0 + " " + string.Join(" ", remainB);

        return Math.Max(
            LevenshteinScore(t0.Trim(), t1.Trim()),
            Math.Max(
                LevenshteinScore(t0.Trim(), t2.Trim()),
                LevenshteinScore(t1.Trim(), t2.Trim())));
    }

    // ── Combined score ────────────────────────────────────────────────

    /// <summary>
    /// Final score (0–100) as a weighted average of Levenshtein and token-set ratios.
    /// </summary>
    public static int Score(string query, string candidate)
    {
        var nQ = Normalize(query);
        var nC = Normalize(candidate);

        if (nQ == nC) return 100;
        if (nQ.Length == 0 || nC.Length == 0) return 0;

        var lev  = LevenshteinScore(nQ, nC);
        var tok  = TokenSetScore(nQ, nC);

        // Weight: 40% Levenshtein (exact character match), 60% token-set (word order insensitive)
        return (int)Math.Round(lev * 0.4 + tok * 0.6);
    }
}
