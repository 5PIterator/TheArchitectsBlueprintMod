using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Chat/console command to run conversion family discovery.
    /// Scans authored conversion families (e.g. unsafe walls) and writes Conversion.itr.
    /// Usage: /tab.discConversion
    /// </summary>
    public sealed class DiscConversionCommand : ModCommand
    {
        public override string Command => "tab.discConversion";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/tab.discConversion";
        public override string Description => "Runs conversion discovery and writes Conversion.itr.json.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                caller.Reply("This command is server-only in multiplayer.");
                return;
            }

            ConversionDiscovery.Run(Mod);
        }
    }
}
