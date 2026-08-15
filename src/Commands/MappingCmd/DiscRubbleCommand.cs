using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Chat/console command to run FlexibleTileWand / Rubblemaker discovery.
    /// Writes Rubble.itr.json.
    /// Usage: /tab.discRubble
    /// </summary>
    public sealed class DiscRubbleCommand : ModCommand
    {
        public override string Command => "tab.discRubble";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/tab.discRubble";
        public override string Description => "Runs rubble discovery and writes Rubble.itr.json.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                caller.Reply("This command is server-only in multiplayer.");
                return;
            }

            RubbleDiscovery.Run(Mod, msg => caller.Reply(msg));
        }
    }
}
