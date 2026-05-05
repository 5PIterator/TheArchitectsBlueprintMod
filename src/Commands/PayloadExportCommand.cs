using Terraria;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Opens the payload export dialog for the topmost projection under the cursor (no-op if none).
    /// Usage: /asm.export
    /// </summary>
    public sealed class PayloadExportCommand : ModCommand
    {
        public override string Command => "asm.export";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/asm.export";
        public override string Description => "Exports the projection payload under the cursor to a JSON file.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (Main.dedServ)
            {
                caller.Reply("Payload export runs on the game client, not the dedicated server.");
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

            PayloadSerialization.BeginExport(projection);
        }
    }
}
