using System.Collections.Generic;
using ArchitectsStencilMod.Core;
using ArchitectsStencilMod.Core.TileMapping;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ObjectData;

namespace ArchitectsStencilMod.Core.Layers
{
    internal sealed class WallAnchoredLayer : Layer
    {
        public static readonly WallAnchoredLayer Definition = new();

        private WallAnchoredLayer() : base("WallAnchored", LayerType.Frame) { }

        public override bool Match(in PasteTile tile) => TileComparison.MatchTileType(in tile);

        public override IReadOnlyList<Blueprint.BoundedTile> Identify(Point rel, in Tile sourceTile, in PasteLookupQuery lookup)
        {
            if (!FrameOperations.HasWallAnchor(sourceTile))
                return NoIdentify();
            if (FrameOperations.HasOtherAnchor(sourceTile))
                return NoIdentify();

            IReadOnlyList<Blueprint.BoundedTile>? tiles = FrameOperations.GetFrameTilesAsBounded(rel, sourceTile, lookup);
            return tiles is null ? NoIdentify() : tiles;
        }

        public override bool CanPlace(IReadOnlyList<Blueprint.BoundedTile> identifiedTiles, in PasteLookupQuery lookup, in PasteContext context) =>
            LayerPlacementPlan.TryReserveForPlacement(this, identifiedTiles, in lookup, in context, reserveEachTileInStack: false);

        public override IReadOnlyList<int>? GetItem(in Tile sourceTile)
        {
            if (!sourceTile.HasTile) return null;
            int style = TileObjectData.GetTileStyle(sourceTile);
            return InteractionMapProvider.GetTileItemIds(sourceTile.TileType, style);
        }

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context) =>
            FrameOperations.Clear(tiles, in context);

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context) =>
            FrameOperations.Place(tiles, in context);

        public override bool Consume(in PasteTile tile, in PasteContext context) =>
            FrameOperations.Consume(in tile, in context);
    }
}
