using System.IO;
using TheArchitectsBlueprintMod.Debug;
using TheArchitectsBlueprintMod.Core.Server;
using Terraria.ModLoader;
#if DEBUG
using TheArchitectsBlueprintMod.Core.TestCases;
#endif

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
            // --- Peek discriminators for chamber sync / DEBUG Test Cases before paste framing.
            long position = reader.BaseStream.Position;
            byte discriminator = reader.ReadByte();
            if (discriminator == TestChamberSystem.SyncPacketDiscriminator)
            {
                TestChamberSystem.HandleSyncPacket(this, reader, whoAmI);
                return;
            }

#if DEBUG
            if (discriminator == MpNetworking.PacketDiscriminator)
            {
                MpNetworking.HandlePacket(this, reader, whoAmI);
                return;
            }
#endif

            reader.BaseStream.Position = position;
            PasteNetworking.HandlePacket(this, reader, whoAmI);
        }
    }
}
