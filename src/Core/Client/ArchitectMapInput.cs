using Microsoft.Xna.Framework;
using Terraria;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Resolves tool cursor tiles in-world and on the fullscreen map for architect tools.
    /// </summary>
    public static class ArchitectMapInput
    {
        /// <summary>Returns true when <paramref name="itemType"/> is a registered architect tool.</summary>
        public static bool IsArchitectTool(int itemType) =>
            ArchitectOverlayVisibility.GetItemScope(itemType) != ArchitectOverlayScope.None;

        /// <summary>
        /// Resolves the tile under the cursor for tool routing.
        /// On the fullscreen map, returns false when the held item is not an architect tool.
        /// </summary>
        public static bool TryGetCursorTile(Player player, out Point tile)
        {
            if (!Main.mapFullscreen)
            {
                tile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
                return true;
            }

            if (!IsArchitectTool(player.HeldItem.type))
            {
                tile = default;
                return false;
            }

            return TryFullscreenMapMouseToTile(out tile);
        }

        /// <summary>
        /// Converts screen mouse position to a tile on the player-centered fullscreen map.
        /// </summary>
        public static bool TryFullscreenMapMouseToTile(out Point tile)
        {
            if (!Main.mapFullscreen)
            {
                tile = default;
                return false;
            }

            Vector2 screenCenter = new(Main.screenWidth / 2f, Main.screenHeight / 2f);
            Vector2 mouse = new(Main.mouseX, Main.mouseY);
            float pixelsPerTile = 16f * Main.mapFullscreenScale * Main.UIScale;
            if (pixelsPerTile <= 0f)
            {
                tile = default;
                return false;
            }

            Vector2 playerTile = Main.LocalPlayer.Center / 16f;
            Vector2 mapTile = playerTile + (mouse - screenCenter) / pixelsPerTile;
            tile = new Point((int)mapTile.X, (int)mapTile.Y);
            return true;
        }
    }
}
