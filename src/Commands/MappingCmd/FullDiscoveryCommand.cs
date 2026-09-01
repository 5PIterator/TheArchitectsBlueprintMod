using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Runs all discovery steps without compiling.
    /// Usage: /tab.discAll. Debug Stick: click-to-step; otherwise auto at 500 steps/frame.
    /// </summary>
    public sealed class FullDiscoveryCommand : ModCommand
    {
        public override string Command => "tab.discAll";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/tab.discAll";
        public override string Description => "Runs all discovery steps.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                caller.Reply("This command is server-only in multiplayer.");
                return;
            }

            int plr = caller.Player?.whoAmI ?? Main.myPlayer;
            TileMappingSystem.StartFullDiscovery(Mod, Main.player[plr], allowStepping: true, compileAtEnd: false);
        }
    }
}
