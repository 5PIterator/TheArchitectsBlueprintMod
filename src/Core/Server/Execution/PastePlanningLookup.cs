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
        /// <summary>Dummy interaction for unindexed holes; those slots are never consumed.</summary>
        private static readonly InteractionEntry UnindexedInteraction = InteractionEntry.Create("Tile", mainId: -1);

        /// <summary>
        /// Creates a lookup that pairs each included payload-relative cell with source and target world state.
        /// </summary>
        public static PasteTileLookup Build(Projection projection)
        {
            // --- Fill a WxH grid, leaving excluded and unresolved cells unindexed so cluster Identify stops at those holes.

            Point size = projection.PayloadSize;
            int w = size.X;
            int h = size.Y;
            var list = new PasteTile[w, h];

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    Point rel = new(x, y);

                    // Leave excluded and missing target cells unindexed so TryGetPasteTile fails at those holes.
                    if (!projection.TryGetTileByRelative(rel, out Blueprint.BlueprintCell projCell) || !projCell.Included
                        || !projection.TryGetTargetTile(rel, out Tile targetTile))
                    {
                        list[x, y] = UnindexedPasteTile();
                        continue;
                    }

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

        /// <summary>Returns a grid slot that <see cref="PasteTileLookup"/> will not index.</summary>
        private static PasteTile UnindexedPasteTile()
        {
            // --- Stamp a Relative that cannot match any array index so holes stay out of TryGetPasteTile.
            return new PasteTile(
                payloadCell: default,
                interaction: UnindexedInteraction,
                source: default,
                target: default,
                relative: new Point(-1, -1),
                world: default);
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
