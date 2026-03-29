using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace ArchitectsStencilMod.Config
{
    /// <summary>
    /// Debug configuration for Architect's Stencil.
    /// This lives in the in-game Mod Config menu and can be reused
    /// for additional debug toggles.
    /// </summary>
    public sealed class DebugConfig : ModConfig
    {
        public static DebugConfig Instance;

        // Client-side so each local player can choose their own debug options.
        public override ConfigScope Mode => ConfigScope.ClientSide;
    }
}

