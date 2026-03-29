using System;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Chat/console command to compile the tile map.
    /// Usage: /asm.compile
    /// </summary>
    public sealed class CompileTileMapCommand : ModCommand
    {
        public override string Command => "asm.compile";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/asm.compile";
        public override string Description => "Compiles the tile map to a JSON file.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            TileMappingSystem.Compile(Mod, msg => caller.Reply(msg));
        }
    }
}
