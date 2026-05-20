using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.UI.TmodGUI;
using TheArchitectsBlueprintMod.UI.TmodGUI.Widgets;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Opens the payload browser for the topmost projection under cursor.
    /// If the topmost blueprint is a selection, a projection is created first.
    /// Usage: /tab.openBrowser
    /// </summary>
    public sealed class PayloadOpenBrowserCommand : ModCommand
    {
        public override string Command => "tab.openBrowser";
        public override CommandType Type => CommandType.Chat;
        public override string Usage => "/tab.openBrowser";
        public override string Description => "Opens the Payload Browser for projection import/export.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (Main.dedServ)
            {
                caller.Reply("Payload Browser runs on the game client, not the dedicated server.");
                return;
            }

            if (Main.gameMenu)
            {
                caller.Reply("Join a world first.");
                return;
            }

            if (caller.Player is not Player player || player.whoAmI != Main.myPlayer)
                return;

            if (!PayloadSerialization.TryResolveProjectionFromCursor(player, out Projection? projection) || projection == null)
            {
                caller.Reply("Payload Browser: No projection or selection under the cursor.", Color.Orange);
                return;
            }

            if (PayloadSerialization.HasBrowserSession &&
                TmodGUI.GetActiveWidget() is TmodPayloadBrowserWidget &&
                PayloadSerialization.SetActiveBrowserProjection(projection))
            {
                caller.Reply("Payload Browser target updated.", Color.LightGreen);
                return;
            }

            TmodPayloadBrowserWidget.Open(projection);
        }
    }
}
