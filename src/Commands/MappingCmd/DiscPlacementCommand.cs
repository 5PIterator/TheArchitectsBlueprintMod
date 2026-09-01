using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Chat/console command to run placement discovery.
    /// Scans items with createTile/createWall and writes Placement.itr.
    /// Usage: /tab.discPlace
    /// </summary>
    public sealed class DiscPlacementCommand : ModCommand
    {
        public override string Command => "tab.discPlace";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/tab.discPlace";
        public override string Description => "Runs placement discovery and writes Placement.itr.json.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                caller.Reply("This command is server-only in multiplayer.");
                return;
            }

            int plr = caller.Player?.whoAmI ?? Main.myPlayer;
            PlacementDiscovery.Run(Mod, Main.player[plr]);
        }
    }
}
