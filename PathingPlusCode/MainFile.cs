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

    /// <summary>Shown in log lines and the in-game byline; the manifest name.</summary>
    public const string ModName = "Pathing Plus";

    /// <summary>Keep in sync with the manifest version. Printed in-game and logged.</summary>
    public const string Version = "v1.3.0";

    /// <summary>The Steam handle, shown beside the version so screenshots identify themselves.</summary>
    public const string Author = "realtruegravy";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        new Harmony(ModId).PatchAll();
        PathingPlusCode.Map.PathingOptions.Load();

        Logger.Info($"{ModName} {Version} initialized.");
    }
}
