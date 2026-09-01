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
            if (!ArchitectMapTransform.IsMapViewActive)
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
        /// Converts screen mouse position to a tile on the pannable fullscreen map.
        /// </summary>
        public static bool TryFullscreenMapMouseToTile(out Point tile)
        {
            if (!ArchitectMapTransform.IsMapViewActive)
            {
                tile = default;
                return false;
            }

            return ArchitectMapTransform.TryScreenToTile(new Vector2(Main.mouseX, Main.mouseY), out tile);
        }
    }
}
