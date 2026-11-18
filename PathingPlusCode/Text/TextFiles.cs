using System.Text;
using System.Text.RegularExpressions;

namespace PathingPlus.PathingPlusCode.Text;

/// <summary>
/// Reading the player-facing text files under <c>text/</c>. Pure string work, linked into
/// the tests, so a translation that drops a key or a placeholder fails the build's tests
/// rather than a player's map screen.
///
/// Two formats, both edited as plain prose with no escaping:
///
/// <list type="bullet">
/// <item><c>strings.txt</c> — one <c>key = value</c> per line. Everything after the first
/// <c>=</c> is the value, so values may contain <c>=</c> themselves.</item>
/// <item><c>help.txt</c> — paragraphs. A blank line starts a new one, a line starting
/// <c>- </c> is a bullet on its own line, and any other line continues the one above, so
/// the file can be hard-wrapped for editing without the wraps showing on screen.</item>
/// </list>
///
/// In both, a line whose first non-blank character is <c>#</c> is a comment.
/// </summary>
public static partial class TextFiles
{
    /// <summary>The language every other one falls back to, key by key.</summary>
    public const string FallbackLanguage = "eng";

    public static Dictionary<string, string> ParseStrings(string content)
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in Lines(content))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var split = line.IndexOf('=');
            if (split <= 0)
                continue;
            var key = line[..split].Trim();
            var value = line[(split + 1)..].Trim();
            if (key.Length > 0)
                table[key] = value;
        }
        return table;
    }

    /// <summary>The help panel's prose, unwrapped. Empty when the file holds none.</summary>
    public static string ParseProse(string content)
    {
        var paragraphs = new List<string>();
        var current = new List<string>();
        foreach (var raw in Lines(content))
        {
            if (raw.TrimStart().StartsWith('#'))
                continue;
            var text = raw.Trim();
            if (text.Length == 0)
            {
                if (current.Count > 0)
                    paragraphs.Add(string.Join('\n', current));
                current.Clear();
                continue;
            }
            // A bullet is its own line; anything else continues the line above it.
            // Without this a list would be joined into one run-on paragraph by the same
            // rule that usefully unwraps prose.
            if (current.Count == 0 || text.StartsWith("- ") || text.StartsWith("* "))
                current.Add(text);
            else
                current[^1] = JoinWrapped(current[^1], text);
        }
        if (current.Count > 0)
            paragraphs.Add(string.Join('\n', current));
        return string.Join("\n\n", paragraphs);
    }

    /// <summary>
    /// Rejoin a line that was wrapped in the file. English needs the space the wrap
    /// replaced; Chinese is written without spaces, and joining two halves of a sentence
    /// with one would put a visible gap in the middle of it.
    /// </summary>
    public static string JoinWrapped(string before, string after) =>
        IsCjk(before[^1]) || IsCjk(after[0]) ? before + after : $"{before} {after}";

    /// <summary>
    /// CJK ideographs and the full-width punctuation that goes with them. Generous on
    /// purpose: wrongly dropping a space between two of these is invisible, while wrongly
    /// keeping one is a gap in the sentence.
    /// </summary>
    public static bool IsCjk(char c) =>
        c is >= '⺀' and <= '鿿'     // radicals, punctuation, kana, ideographs
            or >= '豈' and <= '﫿'   // compatibility ideographs
            or >= '＀' and <= '￯';  // full-width forms: （）：，

    /// <summary>The <c>{name}</c> placeholders a value expects to be filled.</summary>
    public static IReadOnlySet<string> Placeholders(string value) =>
        PlaceholderPattern().Matches(value).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Fill <c>{name}</c> placeholders. Unknown ones are left as written, so a typo in a
    /// translation shows up on screen as itself rather than as nothing.
    /// </summary>
    public static string Format(string template, IReadOnlyDictionary<string, string> values)
    {
        if (values.Count == 0)
            return template;
        var sb = new StringBuilder(template.Length);
        var last = 0;
        foreach (Match m in PlaceholderPattern().Matches(template))
        {
            sb.Append(template, last, m.Index - last);
            sb.Append(values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
            last = m.Index + m.Length;
        }
        sb.Append(template, last, template.Length - last);
        return sb.ToString();
    }

    private static IEnumerable<string> Lines(string content) =>
        content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex PlaceholderPattern();
}
