using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core.ArchitectMode;

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
        /// Switches mouse zoom so <see cref="Main.mouseX"/> / <see cref="Main.MouseWorld"/> match
        /// screen hit-tests and tile picks for the current view (world vs map).
        /// </summary>
        public static void ApplyCursorZoom()
        {
            // --- Map overlays share UI mouse space; world tools need game zoom.

            if (ArchitectMapTransform.IsMapViewActive)
                PlayerInput.SetZoom_UI();
            else
                PlayerInput.SetZoom_World();
        }

        /// <summary>
        /// Resolves the tile under the cursor for tool routing.
        /// On the map, returns true only while Architect Mode is active.
        /// </summary>
        public static bool TryGetCursorTile(Player player, out Point tile)
        {
            // --- Align mouse zoom, then map MouseWorld / map screen coords to a tile.

            ApplyCursorZoom();

            if (!ArchitectMapTransform.IsMapViewActive)
            {
                tile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
                return true;
            }

            // Map gestures are owned by Architect Mode; held items cannot interact on the map.
            if (!ModContent.GetInstance<ArchitectModeSystem>().Active)
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
            // --- Reject when not in a map view, then convert mouse pixels to tile coords.

            if (!ArchitectMapTransform.IsMapViewActive)
            {
                tile = default;
                return false;
            }

            ApplyCursorZoom();
            return ArchitectMapTransform.TryScreenToTile(new Vector2(Main.mouseX, Main.mouseY), out tile);
        }
    }
}
