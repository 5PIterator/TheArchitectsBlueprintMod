using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Chat/console command to run vine interaction discovery.
    /// Scans TileID.Sets.IsVine / ReverseVineThreads and writes Vines.itr.
    /// Usage: /tab.discVine
    /// </summary>
    public sealed class DiscVineCommand : ModCommand
    {
        public override string Command => "tab.discVine";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/tab.discVine";
        public override string Description => "Runs vine discovery and writes Vines.itr.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                caller.Reply("This command is server-only in multiplayer.");
                return;
            }

            VineDiscovery.Run(Mod, msg => caller.Reply(msg));
        }
    }
}
