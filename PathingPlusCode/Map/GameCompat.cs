using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using System.Reflection;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// The places where the game's recent builds disagree, answered for all of them, so one
/// DLL loads on 0.111 (main, and the manifest's minimum) and on 0.114 (the current beta)
/// alike, and on 0.113 (the previous beta) too.
///
/// Each member here exists because a direct call compiled against one build fails on
/// the other. That failure is not a compile error anyone sees. It is a
/// <c>MissingMethodException</c> thrown the first time the calling method runs, or a
/// texture that silently loads as null. So anything that differs between builds goes
/// through here, by name, and takes whichever form the running game has.
/// </summary>
internal static class GameCompat
{
    // ---- Hotkey bindings ------------------------------------------------------------
    //
    // NHotkeyManager's binding methods have had three shapes:
    //   0.111  void Push…Binding(string hotkey, Action action)
    //          void Remove…Binding(string hotkey, Action action)
    //   0.113  void Push…Binding(string hotkey, Action action, long sequence = 0)
    //          long Remove…Binding(string hotkey, Action action)
    //   0.114  void Push…Binding(string hotkey, Node owner, Action action, long sequence = 0)
    //          long Remove…Binding(string hotkey, Node owner, Action action)
    // Optional parameters are filled in by the compiler, not the runtime, so a call
    // compiled against any one of them is a MissingMethodException on the others.
    //
    // On 0.114 a binding fires only while its owner is in the tree, visible, and not
    // behind an open capstone screen, and Remove matches on owner and action together.
    // So the owner is the node the shortcut belongs to, and Remove must be given the same
    // one. Builds without an owner parameter ignore it.

    private static readonly MethodInfo? PushReleased = Binding("PushHotkeyReleasedBinding");
    private static readonly MethodInfo? RemoveReleased = Binding("RemoveHotkeyReleasedBinding");

    public static void PushHotkeyReleasedBinding(
        NHotkeyManager hotkeys, string hotkey, Node owner, Action action) =>
        InvokeBinding(PushReleased, "PushHotkeyReleasedBinding", hotkeys, hotkey, owner, action);

    public static void RemoveHotkeyReleasedBinding(
        NHotkeyManager hotkeys, string hotkey, Node owner, Action action) =>
        InvokeBinding(RemoveReleased, "RemoveHotkeyReleasedBinding", hotkeys, hotkey, owner, action);

    /// <summary>
    /// The public overload whose parameters start <c>(string, Action, …)</c> or
    /// <c>(string, Node, Action, …)</c>, fewest parameters first.
    /// </summary>
    private static MethodInfo? Binding(string name) =>
        typeof(NHotkeyManager)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == name && IsBindingShape(m.GetParameters()))
            .OrderBy(m => m.GetParameters().Length)
            .FirstOrDefault();

    private static bool IsBindingShape(ParameterInfo[] parameters) =>
        parameters.Length >= 2 &&
        parameters[0].ParameterType == typeof(string) &&
        (parameters[1].ParameterType == typeof(Action) ||
         parameters.Length >= 3 &&
         parameters[1].ParameterType == typeof(Node) &&
         parameters[2].ParameterType == typeof(Action));

    /// <summary>
    /// Arguments are filled by type: the string is the hotkey, the Node the owner, the
    /// Action the action, and anything else gets its type's default. For the sequence
    /// that is <c>noSequence</c> (0), meaning "a fresh binding at top priority", which is
    /// exactly what the two-argument call meant before the parameter existed.
    /// </summary>
    private static void InvokeBinding(MethodInfo? method, string name, NHotkeyManager hotkeys,
        string hotkey, Node owner, Action action)
    {
        if (method is null)
            throw new MissingMethodException(nameof(NHotkeyManager), name);
        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var type = parameters[i].ParameterType;
            args[i] =
                type == typeof(string) ? hotkey :
                type == typeof(Node) ? owner :
                type == typeof(Action) ? action :
                type.IsValueType ? Activator.CreateInstance(type) : null;
        }
        method.Invoke(hotkeys, args);
    }

    // ---- Row scroll -------------------------------------------------------------------
    //
    // Where the map scrolls to so the player's row is in view. 0.111 placed rows a fixed
    // `_distY` apart and scrolled to `-600 + row * _distY`. 0.113 sizes the rows per map
    // and asks a private `GetRowScrollPos(row)` instead, and `_distY` is gone.

    private static readonly MethodInfo? RowScrollPos =
        AccessTools.Method(typeof(NMapScreen), "GetRowScrollPos", [typeof(int)]);
    private static readonly FieldInfo? DistYField =
        AccessTools.Field(typeof(NMapScreen), "_distY");

    /// <summary>
    /// The scroll target the screen's own controller code would pick for
    /// <paramref name="row" />, or the bottom of the map when the player is not on it yet.
    /// </summary>
    public static float RowScrollTarget(NMapScreen screen, int? row)
    {
        const float bottom = -600f, top = 1800f;
        if (row is not { } r)
            return bottom;
        if (RowScrollPos is not null)
        {
            try
            {
                if (RowScrollPos.Invoke(screen, [r]) is float native)
                    return native;
            }
            catch (TargetInvocationException)
            {
                // The native method warns and answers for rows it cannot find; anything
                // it throws instead falls through to the old formula below.
            }
        }
        var distY = DistYField?.GetValue(screen) as float? ?? 155f;
        return Mathf.Clamp(bottom + r * distY, bottom, top);
    }
}

/// <summary>
/// Game art the mod borrows, under every path the supported builds keep it at.
///
/// 0.113 renamed the <c>compressed</c> atlas to <c>compressed_atlas</c> and flattened it,
/// so <c>compressed.sprites/map/map_dot.tres</c> became
/// <c>compressed_atlas.sprites/map_dot.tres</c>. A path that does not exist loads as
/// null, which draws every route and every pin ring as nothing at all, so each texture
/// lists the newest path first and the older ones after it.
///
/// Only the path is remembered. The texture is not: the engine's resource cache frees
/// textures on scene teardown, and a held wrapper would come back disposed.
/// <c>CacheMode.Reuse</c> keeps the repeat loads cheap.
/// </summary>
internal sealed class GameArt(params string[] paths)
{
    public static readonly GameArt MapDot = new(
        "res://images/atlases/compressed_atlas.sprites/map_dot.tres",
        "res://images/atlases/compressed.sprites/map/map_dot.tres");

    /// <summary>The last of the five map-circle frames, the only one that reads as a still.</summary>
    public static readonly GameArt MapCircle = new(
        "res://images/atlases/compressed_atlas.sprites/map_circle_4.tres",
        "res://images/atlases/compressed.sprites/map/map_circle_4.tres");

    private string? _resolved;

    public Texture2D? Load()
    {
        _resolved ??= paths.FirstOrDefault(p => ResourceLoader.Exists(p)) ?? paths[0];
        return ResourceLoader.Load<Texture2D>(_resolved, null, ResourceLoader.CacheMode.Reuse);
    }
}

/// <summary>
/// Fonts the mod borrows from the game, read at the point of use and never kept.
///
/// The game frees resources out from under anything that holds them. Every room change,
/// <c>PreloadManager</c> unloads the assets the next room does not list — and every asset
/// that was fetched through its cache without having been preloaded — by calling
/// <c>Dispose()</c> on the managed wrapper. Godot hands out one wrapper per native object,
/// so that is the same object any other C# code is holding: the font itself lives on in
/// the scene that uses it, but the mod's reference to it is dead, and the next label made
/// with it throws <c>ObjectDisposedException</c>.
///
/// The legend used to keep the header's font for the life of the map screen, and that is
/// exactly the shape of a routes table that goes blank until a save and reload (reported
/// on 0.113): once the font was disposed, every redraw cleared the table and then died
/// building the first label of the new one. Asking the header each time costs nothing
/// and always answers with a live wrapper.
/// </summary>
internal static class GameFont
{
    /// <summary>The native legend header's font: the face every mod label on the map wears.</summary>
    public static Font? Header(Node screen)
    {
        if (!GodotObject.IsInstanceValid(screen))
            return null;
        var font = screen.GetNodeOrNull<Label>("MapLegend/Header")?.GetThemeFont("font");
        return font is not null && GodotObject.IsInstanceValid(font) ? font : null;
    }
}
