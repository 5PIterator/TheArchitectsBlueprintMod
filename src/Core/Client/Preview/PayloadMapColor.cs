using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Map;
using TheArchitectsBlueprintMod.UI.Preview;

namespace TheArchitectsBlueprintMod.Core.Preview
{
    /// <summary>
    /// Resolves vanilla map pixel colors for payload cells using <see cref="MapHelper"/>.
    /// </summary>
    internal static class PayloadMapColor
    {
        /// <summary>
        /// Builds a map color for <paramref name="cell"/> via a scratch tile and vanilla map lookup.
        /// </summary>
        public static bool TryGetMapColor(PayloadCell cell, out Color color)
        {
            color = default;
            if (cell.IsEmpty)
                return false;

            PreviewTileDraw.GetSafePreviewCoords(out int scratchX, out int scratchY);
            if (!WorldGen.InWorld(scratchX, scratchY))
                return false;

            Tile live = Main.tile[scratchX, scratchY];
            Tile backup = new();
            backup.CopyFrom(live);
            cell.CopyToWorldTile(scratchX, scratchY);

            try
            {
                MapTile mapTile = MapHelper.CreateMapTile(scratchX, scratchY, byte.MaxValue);
                if (mapTile.Type == 0)
                    return false;

                color = MapHelper.GetMapTileXnaColor(ref mapTile);
                return true;
            }
            finally
            {
                live.CopyFrom(backup);
            }
        }
    }
}
