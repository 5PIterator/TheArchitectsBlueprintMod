using System.IO;
using ArchitectsBlueprintMod.Debug;
using ArchitectsBlueprintMod.Core.Server;
using Terraria.ModLoader;

namespace ArchitectsBlueprintMod
{
    /// <summary>
    /// Root mod class for Architect's Blueprint.
    /// This is intentionally minimal; core systems live under the src/Core namespace.
    /// </summary>
    public sealed class ArchitectsBlueprintMod : Mod
    {
        public const string ModInternalName = "ArchitectsBlueprintMod";

        public override void Load()
        {
            ABPLog.Initialize(this);
        }

        public override void Unload()
        {
            ABPLog.Unload();
        }

        public override void HandlePacket(BinaryReader reader, int whoAmI)
        {
            PasteNetworking.HandlePacket(this, reader, whoAmI);
        }
    }
}
