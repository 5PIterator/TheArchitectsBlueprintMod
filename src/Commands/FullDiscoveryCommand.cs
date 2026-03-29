using System;
using System.Collections;
using Terraria;
using Terraria.ModLoader;
using ArchitectsStencilMod.Core.TileMapping;
using ArchitectsStencilMod.Debug;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Runs all discovery steps (placement, biome, shimmer) and then compiles the tile map.
    /// Usage: /asm.discAll. When holding Debug Stick: left-click to step through each phase, right-click to cancel.
    /// </summary>
    public sealed class FullDiscoveryCommand : ModCommand
    {
        public override string Command => "asm.discAll";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/asm.discAll";
        public override string Description => "Runs all discovery steps and compiles the tile map.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            Action<string> reply = msg => caller.Reply(msg);
            int plr = caller.Player?.whoAmI ?? Main.myPlayer;
            var player = Main.player[plr];

            var enumerator = CreateSteppedRun(Mod, player, reply);
            if (DebugStepper.IsHoldingDebugStick(player))
            {
                DebugStepper.StartSession(player, enumerator, onComplete: null, onCancel: null, log: reply);
                return;
            }

            while (enumerator.MoveNext()) { }
        }

        private static IEnumerator CreateSteppedRun(Mod mod, Player player, Action<string> reply)
        {
            reply("Discovery: Placement...");
            PlacementDiscovery.Run(mod, player, reply, allowStepping: false);
            yield return null;

            reply("Discovery: Biome...");
            BiomeDiscovery.Run(mod, player, reply, allowStepping: false);
            yield return null;

            reply("Discovery: Shimmer...");
            ShimmerDiscovery.Run(mod, reply, player, allowStepping: false);
            yield return null;

            reply("Compiling tile map...");
            TileMappingSystem.Compile(mod, reply);
            reply("Full discovery and compile complete.");
        }
    }
}
