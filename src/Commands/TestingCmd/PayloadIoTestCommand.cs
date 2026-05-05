#if DEBUG
#nullable enable

using ArchitectsStencilMod.Tests;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Debug-only chat trigger for <see cref="PayloadIoTestScript"/>.
    /// </summary>
    public sealed class PayloadIoTestCommand : ModCommand
    {
        public override string Command => "asm.test.payload";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/asm.test.payload clear|fill";
        public override string Description => "[Debug] Runs payload fixture tests (implementation in tests/PayloadIoTestScript.cs).";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (Main.dedServ)
            {
                caller.Reply("Payload IO tests run on the client, not on a dedicated server.");
                return;
            }

            if (Main.gameMenu)
            {
                caller.Reply("Join a world first.");
                return;
            }

            if (caller.Player is not Player pl || pl.whoAmI != Main.myPlayer)
            {
                if (caller.Player is null)
                    caller.Reply("No player context.");
                return;
            }

            if (args.Length < 1 || (args[0] != "clear" && args[0] != "fill"))
            {
                caller.Reply("Usage: /asm.test.payload clear|fill", Color.Orange);
                return;
            }

            PayloadIoTestScript.Run(caller, pl, args[0]);
        }
    }
}
#endif
