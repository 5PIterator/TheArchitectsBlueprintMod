using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Chat/console command to run paint and coating discovery.
    /// Scans paint tools and PaintOrCoating ammo, then writes Paints.itr.
    /// Usage: /tab.discPaint
    /// </summary>
    public sealed class DiscPaintCommand : ModCommand
    {
        public override string Command => "tab.discPaint";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/tab.discPaint";
        public override string Description => "Runs paint discovery and writes Paints.itr.json.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                caller.Reply("This command is server-only in multiplayer.");
                return;
            }

            PaintDiscovery.Run(Mod);
        }
    }
}
