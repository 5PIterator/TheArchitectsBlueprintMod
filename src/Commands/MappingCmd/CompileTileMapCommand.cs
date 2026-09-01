using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Chat/console command to compile the tile map.
    /// Usage: /tab.compile
    /// </summary>
    public sealed class CompileTileMapCommand : ModCommand
    {
        public override string Command => "tab.compile";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/tab.compile";
        public override string Description => "Compiles the interaction map from enabled SourceMaps.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                caller.Reply("This command is server-only in multiplayer.");
                return;
            }

            TileMappingSystem.Compile(Mod);
        }
    }
}
