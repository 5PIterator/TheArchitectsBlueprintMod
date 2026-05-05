using Terraria;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Opens the payload import dialog for the topmost projection under the cursor (no-op if none).
    /// Usage: /asm.import
    /// </summary>
    public sealed class PayloadImportCommand : ModCommand
    {
        public override string Command => "asm.import";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/asm.import";
        public override string Description => "Imports a payload JSON file onto the projection under the cursor.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (Main.dedServ)
            {
                caller.Reply("Payload import runs on the game client, not the dedicated server.");
                return;
            }

            if (Main.gameMenu)
            {
                caller.Reply("Join a world first.");
                return;
            }

            if (caller.Player is not Player player)
            {
                caller.Reply("No player context.");
                return;
            }

            if (player.whoAmI != Main.myPlayer)
                return;

            if (!PayloadSerialization.TryGetProjectionForFileIo(player, out Projection? projection))
                return;

            PayloadSerialization.BeginImport(projection);
        }
    }
}
