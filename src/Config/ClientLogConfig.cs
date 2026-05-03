using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace ArchitectsStencilMod.Config
{
    /// <summary>
    /// Client-only logging preferences. Does not change paste behavior or tile-map compilation on the server.
    /// </summary>
    public sealed class ClientLogConfig : ModConfig
    {
        public static ClientLogConfig Instance = null!;

        public override ConfigScope Mode => ConfigScope.ClientSide;

        [Header("Logging")]

        [DefaultValue(true)]
        public bool ShowDebugLogs;

        public override void OnLoaded()
        {
            Instance = this;
        }
    }
}
