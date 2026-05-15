using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Opens the payload import dialog for the topmost projection under the cursor, or a new projection from the current selection.
    /// Usage: /tab.import
    /// </summary>
    public sealed class PayloadImportCommand : ModCommand
    {
        private static BlueprintSystem BlueprintSystem => ModContent.GetInstance<BlueprintSystem>();
        private static ProjectionSystem ProjectionSystem => ModContent.GetInstance<ProjectionSystem>();
        public override string Command => "tab.import";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/tab.import";
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

            Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
            switch (BlueprintSystem.GetTopmostBlueprintAt(cursorTile, out Blueprint? atCursor) ? atCursor : null)
            {
                case Projection projection:
                    PayloadSerialization.BeginImport(projection);
                    break;
                case Selection selection:
                {
                    Projection projection = ProjectionSystem.NewProjection(player, selection);
                    PayloadSerialization.BeginImport(projection);
                    break;
                }
                default:
                    caller.Reply("Payload import: No projection or selection under the cursor.", Color.Orange);
                    return;
            }

        }
    }
}
