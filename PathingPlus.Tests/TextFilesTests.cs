using PathingPlus.PathingPlusCode.Text;
using Xunit;

namespace PathingPlus.Tests;

public class TextFilesTests
{
    [Fact]
    public void A_strings_file_reads_keys_and_values_and_skips_comments()
    {
        var table = TextFiles.ParseStrings("""
            # a comment
              # an indented comment
            view.zoom_out = Zoom Out

            settings.value = {name}: {value}
            odd = a = b
            not a pair
            """);
        Assert.Equal(3, table.Count);
        Assert.Equal("Zoom Out", table["view.zoom_out"]);
        Assert.Equal("{name}: {value}", table["settings.value"]);
        Assert.Equal("a = b", table["odd"]);
    }

    [Fact]
    public void Wrapped_english_prose_is_rejoined_with_a_space()
    {
        var text = TextFiles.ParseProse("""
            # comment
            First line
            continues here.

            - a bullet
            - another
              that wraps
            """);
        Assert.Equal("First line continues here.\n\n- a bullet\n- another that wraps", text);
    }

    [Fact]
    public void Wrapped_chinese_prose_is_rejoined_without_a_gap()
    {
        Assert.Equal("重新打开地图时，会保持视图。",
            TextFiles.ParseProse("重新打开地图时，\n会保持视图。"));
        // Chinese meeting a Latin word still needs no space: the file writes the spaces
        // it wants around embedded English on the line itself.
        Assert.Equal("使用Pathing Plus", TextFiles.JoinWrapped("使用", "Pathing Plus"));
        Assert.Equal("Pathing Plus使用", TextFiles.JoinWrapped("Pathing Plus", "使用"));
    }

    [Fact]
    public void Format_fills_known_placeholders_and_leaves_unknown_ones_visible()
    {
        var values = new Dictionary<string, string> { ["name"] = "宽度", ["value"] = "5" };
        Assert.Equal("宽度：5", TextFiles.Format("{name}：{value}", values));
        Assert.Equal("{nmae}: 5", TextFiles.Format("{nmae}: {value}", values));
    }

    private static List<string> Languages() =>
        Directory.GetDirectories(TextRoot())
            .Select(dir => Path.GetFileName(dir))
            .Where(language => language != TextFiles.FallbackLanguage)
            .ToList();

    public static TheoryData<string> Translations()
    {
        var data = new TheoryData<string>();
        foreach (var language in Languages())
            data.Add(language);
        return data;
    }

    [Fact]
    public void Chinese_is_shipped_in_both_scripts()
    {
        var languages = Languages();
        Assert.Contains("zhs", languages);
        Assert.Contains("zht", languages);
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void A_translation_has_exactly_the_english_keys(string language)
    {
        var english = Strings(TextFiles.FallbackLanguage);
        var translated = Strings(language);
        Assert.Empty(english.Keys.Except(translated.Keys));
        Assert.Empty(translated.Keys.Except(english.Keys));
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void A_translation_keeps_every_placeholder(string language)
    {
        var english = Strings(TextFiles.FallbackLanguage);
        foreach (var (key, value) in Strings(language))
            if (english.TryGetValue(key, out var source))
                Assert.True(TextFiles.Placeholders(source).SetEquals(TextFiles.Placeholders(value)),
                    $"{language}/strings.txt {key}: expected {string.Join(", ", TextFiles.Placeholders(source))}");
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void A_translated_help_panel_names_the_shortcut(string language)
    {
        var english = TextFiles.ParseProse(File.ReadAllText(Path.Combine(TextRoot(), "eng", "help.txt")));
        var translated = TextFiles.ParseProse(File.ReadAllText(Path.Combine(TextRoot(), language, "help.txt")));
        Assert.NotEmpty(translated);
        Assert.Equal(TextFiles.Placeholders(english), TextFiles.Placeholders(translated));
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void A_chinese_help_panel_has_no_stray_spaces_between_characters(string language)
    {
        var text = TextFiles.ParseProse(File.ReadAllText(Path.Combine(TextRoot(), language, "help.txt")));
        for (var i = 1; i < text.Length - 1; i++)
            Assert.False(text[i] == ' ' && TextFiles.IsCjk(text[i - 1]) && TextFiles.IsCjk(text[i + 1]),
                $"{language}/help.txt has a space between two CJK characters near \"{text.Substring(Math.Max(0, i - 6), Math.Min(12, text.Length - Math.Max(0, i - 6)))}\"");
    }

    private static Dictionary<string, string> Strings(string language) =>
        TextFiles.ParseStrings(File.ReadAllText(Path.Combine(TextRoot(), language, "strings.txt")));

    private static string TextRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "PathingPlus.json")))
                return Path.Combine(dir.FullName, "text");
        throw new DirectoryNotFoundException("Could not find the repository's text/ folder.");
    }
}
