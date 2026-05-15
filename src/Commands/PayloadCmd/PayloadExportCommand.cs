using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Opens the payload export dialog for the topmost projection under the cursor.
    /// Usage: /tab.export
    /// </summary>
    public sealed class PayloadExportCommand : ModCommand
    {
        private static BlueprintSystem BlueprintSystem => ModContent.GetInstance<BlueprintSystem>();
        public override string Command => "tab.export";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/tab.export";
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

            Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
            switch (BlueprintSystem.GetTopmostProjectionAt(cursorTile, out Projection? atCursor) ? atCursor : null)
            {
                case Projection projection:
                    PayloadSerialization.BeginExport(projection);
                    break;
                default:
                    caller.Reply("Payload export: No projection under the cursor.", Color.Orange);
                    return;
            }
        }
    }
}
