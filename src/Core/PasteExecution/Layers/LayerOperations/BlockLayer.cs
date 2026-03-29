using System.Collections.Generic;
using ArchitectsStencilMod.Core;
using ArchitectsStencilMod.Core.TileMapping;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace ArchitectsStencilMod.Core.Layers
{
    internal sealed class BlockLayer : Layer
    {
        public static readonly BlockLayer Definition = new();

        private BlockLayer() : base("Block", LayerType.Block) { }

        public override bool Match(in PasteTile tile) => TileComparison.MatchTileType(in tile);

        public override IReadOnlyList<Blueprint.BoundedTile> Identify(Point rel, in Tile sourceTile, in PasteLookupQuery lookup)
        {
            if (!sourceTile.HasTile) return NoIdentify();
            if (sourceTile.TileType >= TileLoader.TileCount) return NoIdentify();
            if (TileID.Sets.Falling[sourceTile.TileType]) return NoIdentify();
            if (FrameOperations.HasTileAnchor(sourceTile)) return NoIdentify();
            if (FrameOperations.HasWallAnchor(sourceTile)) return NoIdentify();
            if (FrameOperations.HasOtherAnchor(sourceTile)) return NoIdentify();
            if (CustomLayer.IsCustom(sourceTile.TileType)) return NoIdentify();

            return FrameOperations.GetFrameTilesAsBounded(rel, sourceTile, lookup) ?? [new Blueprint.BoundedTile(rel, selected: true)];
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
            TileOperations.Clear(FrameOperations.GetPlacementOriginTile(tiles), in context);

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context) =>
            TileOperations.Place(tiles, in context);

        public override bool Consume(in PasteTile tile, in PasteContext context) =>
            TileOperations.Consume(in tile, in context);
    }
}
