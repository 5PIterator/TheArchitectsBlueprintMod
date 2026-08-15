using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Chat/console command to run plant interaction discovery.
    /// Scans vines, ground plants, sunflower styles, and tree/cactus wand pairs; writes Plants.itr.
    /// Usage: /tab.discPlant
    /// </summary>
    public sealed class DiscPlantCommand : ModCommand
    {
        public override string Command => "tab.discPlant";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/tab.discPlant";
        public override string Description => "Runs plant discovery and writes Plants.itr.json.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                caller.Reply("This command is server-only in multiplayer.");
                return;
            }

            PlantDiscovery.Run(Mod, msg => caller.Reply(msg));
        }
    }
}
