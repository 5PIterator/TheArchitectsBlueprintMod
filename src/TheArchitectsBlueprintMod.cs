using System.IO;
using TheArchitectsBlueprintMod.Debug;
using TheArchitectsBlueprintMod.Core.Server;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod
{
    /// <summary>
    /// Root mod class for The Architect's Blueprint.
    /// This is intentionally minimal; core systems live under the src/Core namespace.
    /// </summary>
    public sealed class TheArchitectsBlueprintMod : Mod
    {
        public const string ModInternalName = "TheArchitectsBlueprintMod";

        public override void HandlePacket(BinaryReader reader, int whoAmI)
        {
            PasteNetworking.HandlePacket(this, reader, whoAmI);
        }
    }
}
