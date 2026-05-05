using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Opens the payload import dialog for the topmost projection under the cursor, or a new projection from the current selection.
    /// Usage: /asm.import
    /// </summary>
    public sealed class PayloadImportCommand : ModCommand
    {
        public override string Command => "asm.import";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/asm.import";
        public override string Description => "Imports a payload JSON file onto the projection under the cursor, or onto a new projection from your selection.";

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

            var sys = ModContent.GetInstance<ProjectionSystem>();
            Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
            Projection? projection = null;
            if (sys.TryGetProjectionAt(player, cursorTile, out Projection? atCursor) && atCursor is not null && atCursor.IsDefined)
                projection = atCursor;
            else
                projection = sys.NewProjection(player, cursorTile);

            if (projection is null || !projection.IsDefined)
            {
                caller.Reply("Payload import: No projection or selection under the cursor.", Color.Orange);
                return;
            }

            PayloadSerialization.BeginImport(projection);
        }
    }
}
