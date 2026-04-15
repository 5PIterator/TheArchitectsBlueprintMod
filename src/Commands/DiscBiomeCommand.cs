using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using ArchitectsStencilMod.Core.TileMapping;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Chat/console command to run biome conversion discovery.
    /// Finds items that perform biome conversion, tiles/walls that can be converted,
    /// and tests WorldGen.Convert for each combination. Output: Biome.itr
    /// Usage: /asm.discBiome
    /// </summary>
    public sealed class DiscBiomeCommand : ModCommand
    {
        public override string Command => "asm.discBiome";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/asm.discBiome";
        public override string Description => "Runs biome conversion discovery and writes Biome.itr.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                caller.Reply("This command is server-only in multiplayer.");
                return;
            }

            int plr = caller.Player?.whoAmI ?? Main.myPlayer;
            BiomeDiscovery.Run(Mod, Main.player[plr], msg => caller.Reply(msg));
        }
    }
}
