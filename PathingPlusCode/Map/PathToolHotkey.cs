using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// The keyboard shortcut for the path tool, listed in Settings → Input beside the
/// game's own bindings so it can be rebound like any of them.
///
/// The game does not drive its shortcuts through Godot's input map. <c>NInputManager</c>
/// keeps its own action → key dictionaries, watches raw input, and synthesises an
/// <c>InputEventAction</c> when a binding matches; Settings → Input is built from lists
/// of remappable actions and edits those same dictionaries. So rather than watching for
/// a key press of its own — which would ignore any rebinding and fire twice for anyone
/// who had bound the key elsewhere — this action joins that system: registered with
/// Godot with no key event of its own, given a default, given a row title, and added to
/// the remappable lists so the settings screen builds it a row.
/// </summary>
internal static class PathToolHotkey
{
    /// <summary>Godot input action name. Namespaced so it cannot collide.</summary>
    public const string Action = "pathing_plus_path_tool";

    /// <summary>
    /// The key it starts on, and returns to under Reset to Default.
    ///
    /// The mouse-and-keyboard scheme uses A / S / D / X for the piles, E to confirm,
    /// M for the map, Space to peek and 1-0 for cards; Q is free, and sits next to the
    /// hand rather than across the keyboard from it.
    ///
    /// **Only that scheme gets a default.** Keyboard-only mode is a different map, and
    /// there Q is already the deck — a default is not worth a shortcut that opens two
    /// things at once. The row is still bindable in that column; it just starts empty.
    /// </summary>
    public const Key DefaultKey = Key.Q;

    private const string LocTable = "settings_ui";
    private const string TitleKey = "INPUT_SETTINGS.INPUT_TITLE." + Action;

    /// <summary>
    /// The row's label. Says which mod it came from as well as what it does, because a
    /// row appearing among the game's own bindings otherwise has no explanation.
    /// </summary>
    private const string SettingsTitle = "Pathing Plus: Path Tool";

    public static void Install()
    {
        // No key event of its own: NInputManager supplies the input, and a second
        // source would fire the shortcut twice and ignore any rebinding.
        if (!InputMap.HasAction(Action))
            InputMap.AddAction(Action);

        // Each row resolves its title through an indirection: this table maps the action
        // to a loc key, and the row reads that key out of `settings_ui`. Both halves
        // have to be there or the row throws while building itself — see EnsureTitle.
        NInputSettingsEntry.commandToLocTitle[Action] = Action;
        EnsureTitle();

        // The settings screen builds one row per action across the controller and
        // mouse-and-keyboard lists, so the second of those is what puts it on screen.
        // The keyboard-only list is what makes that column bindable.
        AddToRemappable(NInputManager.remappableMKbInputs);
        AddToRemappable(NInputManager.remappableKbOnlyInputs);
        // Deliberately not the controller list. Left off, the row simply says it has no
        // controller binding; added, the game's rebind path hands a button's previous
        // owner the button the rebound action used to have — and an action that starts
        // with none has nothing to hand over. Controller players already reach the tool
        // with the left stick click.
    }

    /// <summary>
    /// The row's visible text, in the table the row will look for it in.
    ///
    /// Re-asserted rather than set once: changing language replaces the whole table
    /// dictionary, and this key would go with it. It is called from Install for the
    /// normal case and again just before any row is built, which is the moment it has
    /// to be true — see <see cref="InputSettingsTitlePatch" />.
    /// </summary>
    public static void EnsureTitle() => ModStrings.Ensure(LocTable, TitleKey, SettingsTitle);

    /// <summary>
    /// The key the shortcut is on right now, or null if it is unbound. Asked fresh each
    /// time rather than cached, so a rebind shows up without anything having to watch
    /// for it. <c>GetCurrentHotkey</c> picks the right map for the control scheme in
    /// use, which is why keyboard-only mode reports its own binding rather than Q.
    /// </summary>
    public static string? CurrentKeyLabel() => Guard.Run("Reading the path tool shortcut", () =>
    {
        var key = NInputManager.Instance?.GetCurrentHotkey(Action) ?? Key.None;
        // The game's own Settings rows print the enum name, warts and all — Key1 for
        // the 1 key. Matching it beats inventing a prettier name the rebinding screen
        // then contradicts.
        return key == Key.None ? null : key.ToString();
    }, null);

    private static void AddToRemappable(IReadOnlyList<StringName> inputs)
    {
        if (inputs.Any(existing => existing.ToString() == Action))
            return;
        if (inputs is List<StringName> list)
            list.Add(Action);
        else
            MainFile.Logger.Warn(
                $"Remappable input list is {inputs.GetType().Name}, not a mutable list. " +
                "The path tool shortcut will not appear in Settings → Input.");
    }
}

/// <summary>
/// The keyboard default, put where the game's own defaults live.
///
/// The defaults are the base every saved mapping is layered onto, and they are exactly
/// what Reset to Default restores — so writing the shortcut into them covers a fresh
/// profile, a returning one, and a reset with one hook, where injecting it after load
/// would cover only the first.
/// </summary>
[HarmonyPatch(typeof(NInputManager))]
internal static class InputDefaultsPatch
{
    [HarmonyPostfix]
    [HarmonyPatch("DefaultHotkeyInputMap", MethodType.Getter)]
    private static void AfterDefaultHotkeyInputMap(Dictionary<StringName, Key> __result) =>
        __result[PathToolHotkey.Action] = PathToolHotkey.DefaultKey;
}

/// <summary>
/// Makes sure the shortcut's row can name itself before it tries to.
///
/// <c>NInputSettingsEntry._Ready</c> reads its title out of the loc table with no
/// tolerance for a missing key — <c>GetRawText</c> throws, outside the formatter's own
/// catch — and a row that dies partway through <c>_Ready</c> leaves the signals it had
/// not yet connected to be disconnected on the way out. That is a broken Settings panel
/// for the game's own bindings, not just a missing row, so the entry is re-asserted
/// here rather than trusted to have survived since startup: setting the language
/// replaces the table dictionary and takes the key with it.
/// </summary>
[HarmonyPatch(typeof(NInputSettingsEntry), nameof(NInputSettingsEntry._Ready))]
internal static class InputSettingsTitlePatch
{
    [HarmonyPrefix]
    private static void BeforeReady() =>
        Guard.Run("Naming the path tool row in Settings", PathToolHotkey.EnsureTitle);
}
