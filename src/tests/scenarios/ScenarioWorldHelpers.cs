#if DEBUG
#nullable enable

using Microsoft.Xna.Framework;
using Terraria;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Shared world-coordinate helpers for scenario setup.</summary>
    internal static class ScenarioWorldHelpers
    {
        /// <summary>Returns a tile coordinate near the player for placing temporary test geometry.</summary>
        public static Point TileNearPlayer(Player player, int offsetX, int offsetY)
        {
            // --- Convert the player center to tiles, then apply the requested offset.
            int baseX = (int)(player.Center.X / 16f);
            int baseY = (int)(player.Center.Y / 16f);
            return new Point(baseX + offsetX, baseY + offsetY);
        }
    }
}
#endif
