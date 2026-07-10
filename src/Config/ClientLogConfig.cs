using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace TheArchitectsBlueprintMod.Config
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
        public bool EnableDebugLogs;

        [Header("LogTags")]

        [DefaultValue(true)]
        public bool TagPaste;

        [DefaultValue(true)]
        public bool TagTileExecutor;

        [DefaultValue(true)]
        public bool TagInventory;

        [DefaultValue(true)]
        public bool TagProjection;

        [DefaultValue(true)]
        public bool TagSelection;

        [DefaultValue(true)]
        public bool TagLayers;

        [DefaultValue(true)]
        public bool TagTileMapping;

        [DefaultValue(true)]
        public bool TagInteractionMap;

        [DefaultValue(true)]
        public bool TagNetworking;

        [DefaultValue(true)]
        public bool TagToolAction;

        [DefaultValue(true)]
        public bool TagTemplate;

        [DefaultValue(true)]
        public bool TagOther;

        [DefaultValue(false)]
        public bool PrintDebugLogsToChat;

        [DefaultValue(false)]
        public bool PrintInfoLogsToChat;

        public override void OnLoaded()
        {
            Instance = this;
        }
    }
}
