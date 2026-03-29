using Terraria;
using Terraria.ModLoader;
using ArchitectsStencilMod.Core.TileMapping;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Chat/console command to run placement discovery.
    /// Scans items with createTile/createWall and writes Placement.itr.
    /// Usage: /asm.discPlace
    /// </summary>
    public sealed class DiscPlacementCommand : ModCommand
    {
        public override string Command => "asm.discPlace";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/asm.discPlace";
        public override string Description => "Runs placement discovery and writes Placement.itr.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            int plr = caller.Player?.whoAmI ?? Main.myPlayer;
            PlacementDiscovery.Run(Mod, Main.player[plr], msg => caller.Reply(msg));
        }
    }
}
