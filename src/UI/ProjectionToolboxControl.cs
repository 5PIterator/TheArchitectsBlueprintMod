using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.UI.TmodGUI.Elements;
using TheArchitectsBlueprintMod.UI.TmodGUI.Widgets;
using TheArchitectsBlueprintMod.Utils;

namespace TheArchitectsBlueprintMod.UI
{
    /// <summary>Displays and handles the extensible toolbox attached to the frontmost projection.</summary>
    [Autoload(Side = ModSide.Client)]
    public sealed class ProjectionToolboxControl : ModSystem
    {
        private const int TilePixelSize = 16;
        private const int ButtonSizePx = 24;
        private const int ToolboxPaddingPx = 2;
        private const int ToolboxOffsetPx = 4;
        private const int ToolboxButtonCount = 2;

        private Projection? _projection;
        private int _hoveredButtonIndex = -1;
        private bool _prevLeftDown;

        /// <summary>Registers a toolbox row for the frontmost projection.</summary>
        internal void RegisterToolbox(string family)
        {
            // --- Resolve the frontmost projection and register one screen-space toolbox drawable.

            _projection = ModContent.GetInstance<BlueprintSystem>().GetTopmostProjection();
            if (_projection is not { IsDefined: true })
                return;

            OverlaySystem.RegScreen(
                "toolbox",
                DrawToolbox,
                family);
        }

        /// <summary>Updates toolbox hover and dispatches clicks to the selected tool.</summary>
        public override void UpdateUI(GameTime gameTime)
        {
            // --- Validate projection UI state, hit-test each row button, then handle fresh clicks.

            _hoveredButtonIndex = -1;

            if (!CanInteractWithToolbox(out Player? player))
            {
                _prevLeftDown = Main.mouseLeft;
                return;
            }

            Point mouse = new(Main.mouseX, Main.mouseY);
            for (int i = 0; i < ToolboxButtonCount; i++)
            {
                if (!GetButtonRect(_projection!.Area, i).Contains(mouse))
                    continue;

                _hoveredButtonIndex = i;
                player.mouseInterface = true;
                Main.instance.MouseText(GetButtonHoverText(i), 0, 0);
                break;
            }

            bool leftDown = Main.mouseLeft;
            bool clicked = leftDown && !_prevLeftDown;
            _prevLeftDown = leftDown;

            if (clicked && _hoveredButtonIndex >= 0)
                ActivateButton(_hoveredButtonIndex);
        }

        /// <summary>Clears toolbox state when leaving the current world.</summary>
        public override void OnWorldUnload()
        {
            // --- Release the tracked projection and reset pointer edge state.

            _projection = null;
            _hoveredButtonIndex = -1;
            _prevLeftDown = false;
        }

        /// <summary>Determines whether the local player can interact with the tracked toolbox.</summary>
        private bool CanInteractWithToolbox(out Player? player)
        {
            // --- Require an active projection overlay and reject conflicting UI gestures.

            player = null;
            if (Main.dedServ || Main.gameMenu || _projection is not { IsDefined: true })
                return false;

            player = Main.LocalPlayer;
            if (player is not { active: true } || player.dead)
                return false;

            string family = $"projection:{player.whoAmI}";
            ArchitectOverlayScope heldScope = player.GetModPlayer<ArchitectOverlayPlayer>().HeldOverlayScope;
            if (!ArchitectOverlayVisibility.IsFamilyVisible(family, heldScope))
                return false;

            return !ActionWheel.IsActive
                && !ActionWheel.IsPendingOpen
                && !ModContent.GetInstance<ProjectionControl>().IsActivelyDragging(player);
        }

        /// <summary>Runs the action assigned to a toolbox button.</summary>
        private void ActivateButton(int buttonIndex)
        {
            // --- Dispatch by stable row index so more projection tools can be appended later.

            switch (buttonIndex)
            {
                case 0:
                    OpenPayloadBrowser();
                    break;
                case 1:
                    PrintManifest();
                    break;
            }
        }

        /// <summary>Opens the payload browser for the toolbox projection.</summary>
        private void OpenPayloadBrowser()
        {
            // --- Avoid duplicate sessions and scope the browser to the tracked projection.

            if (_projection is null
                || PayloadBrowser.HasActive
                && global::TheArchitectsBlueprintMod.UI.TmodGUI.TmodGUI.GetActiveWidget() is TmodPayloadBrowserWidget)
            {
                return;
            }

            SoundEngine.PlaySound(SoundID.MenuTick);
            TmodPayloadBrowserWidget.Open(_projection);
        }

        /// <summary>Prints the paste item manifest for the toolbox projection into chat.</summary>
        private void PrintManifest()
        {
            // --- Resolve the tracked projection and emit a local chat bill of materials.

            if (_projection is null)
                return;

            SoundEngine.PlaySound(SoundID.MenuTick);
            ManifestPrinter.Print(_projection);
        }

        /// <summary>Draws the toolbox row and each registered projection tool button.</summary>
        private void DrawToolbox()
        {
            // --- Draw a compact row above the projection, followed by its button icons.

            if (_projection is not { IsDefined: true })
                return;

            Rectangle toolboxRect = GetToolboxRect(_projection.Area);
            Texture2D pixel = ModUtils.DummyTexture;
            Main.spriteBatch.Draw(pixel, toolboxRect, new Color(35, 40, 83, 220));
            DrawBorder(toolboxRect, new Color(89, 116, 213));

            for (int i = 0; i < ToolboxButtonCount; i++)
                DrawButton(i, GetButtonRect(_projection.Area, i), i == _hoveredButtonIndex);
        }

        /// <summary>Draws one toolbox button from its stable row index.</summary>
        private static void DrawButton(int buttonIndex, Rectangle rect, bool hovered)
        {
            // --- Resolve the indexed icon and add vanilla hover chrome around it.

            string iconPath = buttonIndex switch
            {
                0 => TmodUIAssets.Bestiary.ButtonSearchPath,
                1 => TmodUIAssets.Bestiary.StatCopperPath,
                _ => TmodUIAssets.Bestiary.IconLockedPath
            };

            if (hovered)
            {
                Texture2D border = TmodUIAssets.RequestTexture(TmodUIAssets.Bestiary.ButtonBorderPath).Value;
                Main.spriteBatch.Draw(border, rect, Color.White);
            }

            const int iconInset = 4;
            Rectangle iconRect = new(
                rect.X + iconInset,
                rect.Y + iconInset,
                rect.Width - iconInset * 2,
                rect.Height - iconInset * 2);
            Texture2D icon = TmodUIAssets.RequestTexture(iconPath).Value;
            Main.spriteBatch.Draw(icon, iconRect, Color.White * (hovered ? 1f : 0.85f));
        }

        /// <summary>Returns tooltip text for a toolbox button.</summary>
        private static string GetButtonHoverText(int buttonIndex)
        {
            // --- Map stable row indices to localized tool descriptions.

            return buttonIndex switch
            {
                0 => Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.OpenPayloadBrowser"),
                1 => Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.PrintManifest"),
                _ => string.Empty
            };
        }

        /// <summary>Positions the toolbox row above the projection's top-left corner.</summary>
        private static Rectangle GetToolboxRect(Rectangle projectionArea)
        {
            // --- Convert the projection anchor to screen pixels and size the row for all buttons.

            int width = ToolboxPaddingPx * 2 + ToolboxButtonCount * ButtonSizePx;
            int height = ToolboxPaddingPx * 2 + ButtonSizePx;
            int x = projectionArea.Left * TilePixelSize - (int)Main.screenPosition.X;
            int y = projectionArea.Top * TilePixelSize - (int)Main.screenPosition.Y - height - ToolboxOffsetPx;
            return new Rectangle(x, y, width, height);
        }

        /// <summary>Returns the screen rectangle for a toolbox button.</summary>
        private static Rectangle GetButtonRect(Rectangle projectionArea, int buttonIndex)
        {
            // --- Offset the indexed button horizontally within the toolbox row.

            Rectangle toolbox = GetToolboxRect(projectionArea);
            int x = toolbox.X + ToolboxPaddingPx + buttonIndex * ButtonSizePx;
            int y = toolbox.Y + ToolboxPaddingPx;
            return new Rectangle(x, y, ButtonSizePx, ButtonSizePx);
        }

        /// <summary>Draws a one-pixel border around a screen-space rectangle.</summary>
        private static void DrawBorder(Rectangle rect, Color color)
        {
            // --- Draw four pixel strips along the toolbox edges.

            Texture2D pixel = ModUtils.DummyTexture;
            Main.spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), color);
            Main.spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), color);
            Main.spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), color);
            Main.spriteBatch.Draw(pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), color);
        }
    }
}
