using Terraria;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.UI.TmodGUI;
using TheArchitectsBlueprintMod.UI.TmodGUI.Widgets;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Opens the payload browser. Import/export use the current topmost projection.
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

            if (caller.Player is not { } player || player.whoAmI != Main.myPlayer)
                return;

            if (PayloadBrowser.HasActive && TmodGUI.GetActiveWidget() is TmodPayloadBrowserWidget)
            {
                caller.Reply("Payload Browser is already open.");
                return;
            }

            TmodPayloadBrowserWidget.Open();
        }
    }
}
