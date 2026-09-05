using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Chat/console command to run wand and ammo-tool discovery.
    /// Scans Item.tileWand links and writes Wands.itr.
    /// Usage: /tab.discWand
    /// </summary>
    public sealed class DiscWandCommand : ModCommand
    {
        public override string Command => "tab.discWand";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/tab.discWand";
        public override string Description => "Runs wand discovery and writes Wands.itr.json.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                caller.Reply("This command is server-only in multiplayer.");
                return;
            }

            WandDiscovery.Run(Mod);
        }
    }
}
