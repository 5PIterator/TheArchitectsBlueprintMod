using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ObjectData;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Builds paste-tile lookups for planning, execution, and manifest demand passes.</summary>
    internal static class PastePlanningLookup
    {
        /// <summary>
        /// Creates a lookup that pairs each payload-relative cell with source and target world state.
        /// </summary>
        public static PasteTileLookup Build(Projection projection)
        {
            // --- Fill a dense WxH grid so every relative index resolves, including vacant payload cells.

            Point size = projection.PayloadSize;
            int w = size.X;
            int h = size.Y;
            var list = new PasteTile[w, h];

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    Point rel = new(x, y);
                    if (!projection.TryGetTargetTile(rel, out Tile targetTile))
                        continue;

                    projection.TryGetSourceTile(rel, out Tile sourceTile);
                    Point world = projection.RelativeToWorld(rel);

                    PayloadCell cell = default;
                    InteractionEntry interaction;
                    if (projection.Payload.TryGetCell(rel, out PayloadCell payloadCell) && !payloadCell.IsEmpty)
                    {
                        cell = payloadCell;
                        interaction = ResolveInteraction(cell.ToTile());
                    }
                    else
                    {
                        interaction = InteractionEntry.Create("Tile", mainId: -1);
                    }

                    list[x, y] = new PasteTile(
                        payloadCell: cell,
                        interaction: interaction,
                        source: sourceTile,
                        target: targetTile,
                        relative: rel,
                        world: world);
                }
            }

            return new PasteTileLookup(list);
        }

        /// <summary>Resolves the interaction category and main ID for a payload tile.</summary>
        public static InteractionEntry ResolveInteraction(in Tile sourceTile)
        {
            // --- Prefer tile interaction metadata, then fall back to wall/liquid/default markers.

            if (sourceTile.HasTile)
            {
                int style = 0, alternate = 0;
                TileObjectData.GetTileInfo(sourceTile, ref style, ref alternate);
                return InteractionMapProvider.GetFirstTileInteraction(sourceTile.TileType, style)
                    ?? InteractionEntry.Create("Tile", mainId: sourceTile.TileType);
            }

            if (sourceTile.WallType != WallID.None)
                return InteractionEntry.Create("Wall", mainId: sourceTile.WallType);

            if (sourceTile.LiquidAmount > 0)
                return InteractionEntry.Create("Liquid", mainId: sourceTile.LiquidType);

            return InteractionEntry.Create("Tile", mainId: -1);
        }
    }
}
