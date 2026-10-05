using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.Architect;
using TheArchitectsBlueprintMod.UI.TmodGUI.Elements;
using TheArchitectsBlueprintMod.Utils;
using TheArchitectsBlueprintMod.UI.Style;

namespace TheArchitectsBlueprintMod.UI
{
    /// <summary>Displays and handles the extensible toolbox attached to the frontmost defined selection or projection.</summary>
    public sealed class ProjectionToolboxControl
    {
        private const int TilePixelSize = 16;
        private const int ButtonSizePx = 24;
        private const int ToolboxPaddingPx = 2;
        private const int ToolboxOffsetPx = 4;
        private const int ToolboxButtonCount = 3;

        private Blueprint? _target;
        private int _hoveredButtonIndex = -1;
        private bool _prevLeftDown;

        /// <summary>True when the local pointer is over a toolbox button.</summary>
        internal bool IsHovering => _hoveredButtonIndex >= 0;

        /// <summary>Registers a toolbox row for the frontmost defined selection or projection.</summary>
        internal void RegisterToolbox(Player player)
        {
            // --- Attach one toolbox drawable to the frontmost defined selection or projection.

            ArgumentNullException.ThrowIfNull(player);

            string family = $"toolbox:{player.whoAmI}";
            if (ModContent.GetInstance<ArchitectSystem>().Active
                || ArchitectMapTransform.IsMapViewActive)
            {
                OverlaySystem.UnregisterFamily(family);
                _target = null;
                return;
            }

            _target = ModContent.GetInstance<BlueprintSystem>().GetTopmostDefinedSelectionOrProjection();
            if (_target is not { IsDefined: true })
            {
                OverlaySystem.UnregisterFamily(family);
                return;
            }

            OverlaySystem.RegScreen(
                "toolbox",
                DrawToolbox,
                family);
        }

        /// <summary>Updates toolbox hover and dispatches clicks to the selected tool.</summary>
        internal void UpdateUI(GameTime gameTime)
        {
            // --- Validate blueprint UI state, hit-test each row button, then handle fresh clicks.

            _hoveredButtonIndex = -1;

            if (!CanInteractWithToolbox(out Player? player))
            {
                _prevLeftDown = Main.mouseLeft;
                return;
            }

            Point mouse = new(Main.mouseX, Main.mouseY);
            for (int i = 0; i < ToolboxButtonCount; i++)
            {
                if (!GetButtonRect(_target!.Area, i).Contains(mouse))
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
        internal void OnWorldUnload()
        {
            // --- Release the tracked blueprint and reset pointer edge state.

            _target = null;
            _hoveredButtonIndex = -1;
            _prevLeftDown = false;
        }

        /// <summary>Determines whether the local player can interact with the tracked toolbox.</summary>
        private bool CanInteractWithToolbox(out Player? player)
        {
            // --- Require an active toolbox overlay and reject conflicting UI gestures.

            player = null;
            if (Main.dedServ || Main.gameMenu || _target is not { IsDefined: true })
                return false;

            if (ModContent.GetInstance<ArchitectSystem>().Active)
                return false;

            // Map view without Architect Mode is render-only; toolbox clicks must not drive tools.
            if (ArchitectMapTransform.IsMapViewActive)
                return false;

            player = Main.LocalPlayer;
            if (player is not { active: true } || player.dead)
                return false;

            string family = $"toolbox:{player.whoAmI}";
            ArchitectOverlayScope heldScope = player.GetModPlayer<ArchitectOverlayPlayer>().HeldOverlayScope;
            if (!ArchitectOverlayVisibility.IsFamilyVisible(family, heldScope))
                return false;

            return !ActionWheel.IsActive
                && !ActionWheel.IsPendingOpen
                && !ModContent.GetInstance<ProjectionControl>().IsActivelyDragging(player)
                && !ModContent.GetInstance<SelectionControl>().IsActivelyGesturing(player)
                && !ModContent.GetInstance<InclusionControl>().IsActivelyGesturing(player);
        }

        /// <summary>Runs the action assigned to a toolbox button.</summary>
        private void ActivateButton(int buttonIndex)
        {
            // --- Dispatch by stable row index so more projection tools can be appended later.

            switch (buttonIndex)
            {
                case 0:
                    ArchitectSystem.OpenPayloadBrowser();
                    break;
                case 1:
                    ArchitectSystem.PrintManifest();
                    break;
                case 2:
                    ArchitectSystem.ToggleCut();
                    break;
            }
        }

        /// <summary>Draws the toolbox row and each registered projection tool button.</summary>
        private void DrawToolbox()
        {
            // --- Draw a compact row above the blueprint, followed by its button icons.

            if (_target is not { IsDefined: true })
                return;

            Rectangle toolboxRect = GetToolboxRect(_target.Area);
            Texture2D pixel = ModUtils.DummyTexture;
            Main.spriteBatch.Draw(pixel, toolboxRect, new Color(35, 40, 83, 220));
            DrawBorder(toolboxRect, new Color(89, 116, 213));

            for (int i = 0; i < ToolboxButtonCount; i++)
                DrawButton(i, GetButtonRect(_target.Area, i), i == _hoveredButtonIndex);
        }

        /// <summary>Draws one toolbox button from its stable row index.</summary>
        private static void DrawButton(int buttonIndex, Rectangle rect, bool hovered)
        {
            // --- Resolve the indexed icon and add vanilla hover chrome around it.

            bool cutSelected = buttonIndex == 2 && ArchitectSystem.IsFrontmostCutEnabled();
            if (hovered || cutSelected)
            {
                Texture2D border = TmodUIAssets.RequestTexture(TmodUIAssets.Bestiary.ButtonBorderPath).Value;
                Color borderTint = cutSelected && !hovered
                    ? Palette.ProjectionCutColor
                    : Color.White;
                Main.spriteBatch.Draw(border, rect, borderTint);
            }

            const int iconInset = 4;
            Rectangle iconRect = new(
                rect.X + iconInset,
                rect.Y + iconInset,
                rect.Width - iconInset * 2,
                rect.Height - iconInset * 2);
            Texture2D? icon = TryGetButtonIcon(buttonIndex);
            if (icon is null)
                return;

            Color iconTint = cutSelected
                ? Palette.ProjectionCutColor
                : Color.White;
            Main.spriteBatch.Draw(icon, iconRect, iconTint * (hovered ? 1f : 0.85f));
        }

        /// <summary>Loads the toolbox button icon from Architect Mode assets, falling back to vanilla search.</summary>
        private static Texture2D? TryGetButtonIcon(int buttonIndex)
        {
            // --- Prefer Content/Icons; keep vanilla search as a fallback for the browser slot.

            var architect = ModContent.GetInstance<ArchitectSystem>();

            // Search may fall back to the vanilla bestiary icon; Manifest and Cut are mod assets.
            return buttonIndex switch
            {
                0 => architect.TryGetIconTexture(ArchitectSystem.SearchIconPath)
                    ?? TmodUIAssets.RequestTexture(TmodUIAssets.Bestiary.ButtonSearchPath).Value,
                1 => architect.TryGetIconTexture(ArchitectSystem.ManifestIconPath),
                2 => architect.TryGetIconTexture(ArchitectSystem.CutIconPath),
                _ => null
            };
        }

        /// <summary>Returns tooltip text for a toolbox button.</summary>
        private static string GetButtonHoverText(int buttonIndex)
        {
            // --- Map stable row indices to localized tool descriptions.

            return buttonIndex switch
            {
                0 => Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.OpenPayloadBrowser"),
                1 => Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.PrintManifest"),
                2 => Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.ToggleCut"),
                _ => string.Empty
            };
        }

        /// <summary>Positions the toolbox row above the blueprint's top-left corner.</summary>
        private static Rectangle GetToolboxRect(Rectangle blueprintArea)
        {
            // --- Convert the blueprint anchor to screen pixels and size the row for all buttons.

            int width = ToolboxPaddingPx * 2 + ToolboxButtonCount * ButtonSizePx;
            int height = ToolboxPaddingPx * 2 + ButtonSizePx;
            int x = blueprintArea.Left * TilePixelSize - (int)Main.screenPosition.X;
            int y = blueprintArea.Top * TilePixelSize - (int)Main.screenPosition.Y - height - ToolboxOffsetPx;
            return new Rectangle(x, y, width, height);
        }

        /// <summary>Returns the screen rectangle for a toolbox button.</summary>
        private static Rectangle GetButtonRect(Rectangle blueprintArea, int buttonIndex)
        {
            // --- Offset the indexed button horizontally within the toolbox row.

            Rectangle toolbox = GetToolboxRect(blueprintArea);
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
