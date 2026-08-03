using Godot;
using PathingPlus.PathingPlusCode.Pathing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// Pins and the locked route, persisted across game restarts in the game's own user
/// data directory. One file, one map: the key is a hash of the act map's structure,
/// so restoring a run finds its pins and any other map ignores them. Purely
/// informational state — losing the file loses nothing but pins.
/// </summary>
internal static class PinStore
{
    internal sealed record Saved(string MapKey, string[] Pins, string[]? LockedRoute);

    private static string FilePath =>
        Path.Combine(OS.GetUserDataDir(), "PathingPlus.pins.json");

    private static string? _lastWritten;

    /// <summary>Identity of one generated act map: its nodes, kinds, and edges.</summary>
    public static string KeyFor(SpireMapGraph graph)
    {
        var canon = new StringBuilder();
        foreach (var node in graph.Nodes.OrderBy(n => n.Id, StringComparer.Ordinal))
        {
            canon.Append(node.Id).Append('=').Append(node.RoomKind).Append(';');
            foreach (var successor in graph.Successors(node.Id).OrderBy(s => s, StringComparer.Ordinal))
                canon.Append('>').Append(successor);
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canon.ToString()));
        return Convert.ToHexString(hash)[..16];
    }

    public static Saved? Load() => Guard.Run("Loading saved pins", () =>
    {
        if (!File.Exists(FilePath))
            return null;
        var json = File.ReadAllText(FilePath);
        var saved = JsonSerializer.Deserialize<Saved>(json);
        _lastWritten = json;
        return saved;
    }, null);

    public static void SaveIfChanged(Saved data) => Guard.Run("Saving pins", () =>
    {
        var json = JsonSerializer.Serialize(data);
        if (json == _lastWritten)
            return;
        File.WriteAllText(FilePath, json);
        _lastWritten = json;
    });
}
