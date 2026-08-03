using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace __MOD_ID__.__MOD_ID__Code;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    /// <summary>
    /// The mod's permanent identity. It must equal the manifest id and the assembly name,
    /// because the loader loads "&lt;id&gt;.dll" from "&lt;game&gt;/mods/&lt;id&gt;/".
    /// Changing it after publishing orphans the Workshop item.
    /// </summary>
    public const string ModId = "__MOD_ID__";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        new Harmony(ModId).PatchAll();

        // Keep this string in sync with the manifest version.
        Logger.Info("__MOD_NAME__ v0.1.0 initialized.");
    }
}
