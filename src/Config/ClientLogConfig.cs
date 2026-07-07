using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace TheArchitectsBlueprintMod.Config
{
    public enum LogCategoryFilter
    {
        All,
        TileExecutorOnly,
    }

    /// <summary>
    /// Client-only logging preferences. Does not change paste behavior or tile-map compilation on the server.
    /// </summary>
    public sealed class ClientLogConfig : ModConfig
    {
        public static ClientLogConfig Instance = null!;

        public override ConfigScope Mode => ConfigScope.ClientSide;

        [Header("Logging")]

        [DefaultValue(LogCategoryFilter.All)]
        public LogCategoryFilter CategoryFilter;

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
