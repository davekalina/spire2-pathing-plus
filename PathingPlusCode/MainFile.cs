using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace PathingPlus.PathingPlusCode;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    /// <summary>
    /// The mod's permanent identity. It must equal the manifest id and the assembly name,
    /// because the loader loads "&lt;id&gt;.dll" from "&lt;game&gt;/mods/&lt;id&gt;/".
    /// Changing it after publishing orphans the Workshop item.
    /// </summary>
    public const string ModId = "PathingPlus";

    /// <summary>Shown in log lines; the manifest name.</summary>
    public const string ModName = "Pathing Plus";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        new Harmony(ModId).PatchAll();

        // Keep this string in sync with the manifest version.
        Logger.Info("Pathing Plus v0.10.1 initialized.");
    }
}
