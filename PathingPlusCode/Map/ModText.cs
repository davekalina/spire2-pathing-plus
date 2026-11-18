using MegaCrit.Sts2.Core.Localization;
using PathingPlus.PathingPlusCode.Text;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// The mod's player-facing text, in the language the game is set to.
///
/// Each language is a folder under <c>text/</c> named with the game's own language code
/// (<c>eng</c>, <c>zhs</c>, <c>zht</c> — <c>LocManager.Language</c>), holding a
/// <c>strings.txt</c> and a <c>help.txt</c>. They are embedded in the assembly as
/// <c>&lt;lang&gt;/&lt;file&gt;</c>, so there is nothing extra to package.
///
/// Lookups fall back **key by key** to English, then to the key itself: a translation
/// that is missing one line shows that line in English rather than breaking a panel.
///
/// The language is read at the point of use rather than once. The game only lets it be
/// changed from the main menu, so every map screen is built in one language, but the
/// Settings → Input row is built while the main menu's settings are open, and has to
/// follow a change made a moment earlier on the same screen.
/// </summary>
internal static class ModText
{
    private static readonly Dictionary<string, Dictionary<string, string>> Tables = new();
    private static readonly Dictionary<string, string> Prose = new();

    /// <summary>
    /// The game's language code, or English before the game has made its
    /// <c>LocManager</c> (mod initializers run first).
    /// </summary>
    public static string Language => LocManager.Instance?.Language ?? TextFiles.FallbackLanguage;

    public static string Get(string key)
    {
        var language = Language;
        if (Table(language).TryGetValue(key, out var text))
            return text;
        if (language != TextFiles.FallbackLanguage &&
            Table(TextFiles.FallbackLanguage).TryGetValue(key, out text))
            return text;
        return key;
    }

    /// <summary><see cref="Get" />, with <c>{name}</c> placeholders filled.</summary>
    public static string Format(string key, params (string Name, string Value)[] values) =>
        TextFiles.Format(Get(key), values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal));

    /// <summary>
    /// The help panel's prose in the current language, or English when there is no
    /// translation of it. Null only if even English cannot be read.
    /// </summary>
    public static string? Help()
    {
        var language = Language;
        var text = HelpFor(language);
        if (text.Length == 0 && language != TextFiles.FallbackLanguage)
            text = HelpFor(TextFiles.FallbackLanguage);
        return text.Length == 0 ? null : text;
    }

    private static string HelpFor(string language)
    {
        if (!Prose.TryGetValue(language, out var text))
            Prose[language] = text = Read(language, "help.txt") is { } raw ? TextFiles.ParseProse(raw) : "";
        return text;
    }

    private static Dictionary<string, string> Table(string language)
    {
        if (!Tables.TryGetValue(language, out var table))
            Tables[language] = table = Read(language, "strings.txt") is { } raw
                ? TextFiles.ParseStrings(raw)
                : new Dictionary<string, string>();
        return table;
    }

    private static string? Read(string language, string file) => Guard.Run<string?>($"Reading {language}/{file}", () =>
    {
        using var stream = typeof(ModText).Assembly.GetManifestResourceStream($"{language}/{file}");
        if (stream is null)
            return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }, null);
}
