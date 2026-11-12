using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using System.Reflection;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// The places where the game's current builds disagree, answered for all of them, so one
/// DLL loads on the default branch (0.111) and on the beta (0.113) alike.
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
    // 0.113 gave NHotkeyManager's bindings a priority sequence: Push takes a trailing
    // `long sequence = 0` and Remove returns the sequence it removed. Optional
    // parameters are filled in by the compiler, not the runtime, so a call compiled
    // against either signature is a MissingMethodException on the other.

    private static readonly MethodInfo? PushReleased = Binding("PushHotkeyReleasedBinding");
    private static readonly MethodInfo? RemoveReleased = Binding("RemoveHotkeyReleasedBinding");

    public static void PushHotkeyReleasedBinding(NHotkeyManager hotkeys, string hotkey, Action action) =>
        InvokeBinding(PushReleased, "PushHotkeyReleasedBinding", hotkeys, hotkey, action);

    public static void RemoveHotkeyReleasedBinding(NHotkeyManager hotkeys, string hotkey, Action action) =>
        InvokeBinding(RemoveReleased, "RemoveHotkeyReleasedBinding", hotkeys, hotkey, action);

    private static MethodInfo? Binding(string name) =>
        typeof(NHotkeyManager)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == name)
            .Where(m => m.GetParameters() is [{ } a, { } b, ..] &&
                a.ParameterType == typeof(string) && b.ParameterType == typeof(Action))
            .OrderBy(m => m.GetParameters().Length)
            .FirstOrDefault();

    /// <summary>
    /// Any parameter past the first two gets its type's default. For 0.113's sequence that
    /// is <c>noSequence</c> (0), meaning "a fresh binding at top priority", which is exactly
    /// what the two-argument call meant before the parameter existed.
    /// </summary>
    private static void InvokeBinding(MethodInfo? method, string name, NHotkeyManager hotkeys,
        string hotkey, Action action)
    {
        if (method is null)
            throw new MissingMethodException(nameof(NHotkeyManager), name);
        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        args[0] = hotkey;
        args[1] = action;
        for (var i = 2; i < parameters.Length; i++)
            args[i] = parameters[i].ParameterType.IsValueType
                ? Activator.CreateInstance(parameters[i].ParameterType)
                : null;
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
