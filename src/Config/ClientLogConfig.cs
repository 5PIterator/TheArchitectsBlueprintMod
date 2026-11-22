using System.ComponentModel;
using Terraria.ModLoader.Config;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Config
{
    /// <summary>
    /// Client-only logging preferences. Sent with each paste request so the dedicated server
    /// knows which owner-scoped lines to forward back to this client.
    /// </summary>
    public sealed class ClientLogConfig : ModConfig
    {
        public static ClientLogConfig Instance = null!;

        public override ConfigScope Mode => ConfigScope.ClientSide;

        [Header("Logging")]

        [DefaultValue(TabLogLevel.Debug)]
        public TabLogLevel MinimumLogLevel = TabLogLevel.Debug;

        [DefaultValue(TabLogLevel.Off)]
        public TabLogLevel MinimumChatLogLevel = TabLogLevel.Off;

        [Header("LogTags")]

        [DefaultValue(true)]
        public bool TagGeneric;

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
        public bool TagFrameLayer;

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
        public bool TagManifest;

        [DefaultValue(true)]
        public bool TagOther;

        public override void OnLoaded()
        {
            Instance = this;
        }
    }
}
