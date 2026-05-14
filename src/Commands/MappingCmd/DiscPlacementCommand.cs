using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using ArchitectsBlueprintMod.Core.TileMapping;

namespace ArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Chat/console command to run placement discovery.
    /// Scans items with createTile/createWall and writes Placement.itr.
    /// Usage: /abp.discPlace
    /// </summary>
    public sealed class DiscPlacementCommand : ModCommand
    {
        public override string Command => "abp.discPlace";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/abp.discPlace";
        public override string Description => "Runs placement discovery and writes Placement.itr.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                caller.Reply("This command is server-only in multiplayer.");
                return;
            }

            int plr = caller.Player?.whoAmI ?? Main.myPlayer;
            PlacementDiscovery.Run(Mod, Main.player[plr], msg => caller.Reply(msg));
        }
    }
}
