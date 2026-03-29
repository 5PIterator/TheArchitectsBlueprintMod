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
    [CustomPasteLayer]
    internal sealed class TreeLayer : Layer
    {
        public static readonly TreeLayer Definition = new();
        private const string DefinitionId = "Tree";
        private const LayerType DefinitionLayerType = LayerType.Block;

        private TreeLayer() : base(DefinitionId, DefinitionLayerType) { }

        public override bool Match(in PasteTile tile) => TileComparison.MatchTileType(in tile);

        /// <summary>True when this tile type is handled by the tree layer (block exclusion / identification).</summary>
        public static bool IsCustom(ushort tileType)
        {
            if (tileType >= TileLoader.TileCount) return false;
            return TileID.Sets.IsATreeTrunk[tileType]
                || TileID.Sets.IsShakeable[tileType]
                || TileID.Sets.CountsAsGemTree[tileType]
                || TileID.Sets.GetsCheckedForLeaves[tileType]
                || TileID.Sets.Leaves[tileType];
        }

        internal override bool IsCustomTileType(ushort tileType) => IsCustom(tileType);

        public override IReadOnlyList<Blueprint.BoundedTile> Identify(Point rel, in Tile sourceTile, in PasteLookupQuery lookup) =>
            sourceTile.HasTile && IsCustom(sourceTile.TileType) ? [new Blueprint.BoundedTile(rel, selected: true)] : NoIdentify();

        public override bool CanPlace(IReadOnlyList<Blueprint.BoundedTile> identifiedTiles, in PasteLookupQuery lookup, in PasteContext context) =>
            LayerPlacementPlan.TryReserveForPlacement(this, identifiedTiles, in lookup, in context, reserveEachTileInStack: false);

        public override IReadOnlyList<int>? GetItem(in Tile sourceTile)
        {
            if (!sourceTile.HasTile) return null;
            int style = TileObjectData.GetTileStyle(sourceTile);
            return InteractionMapProvider.GetTileItemIds(sourceTile.TileType, style);
        }

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            var origin = FrameOperations.GetPlacementOriginTile(tiles);
            return TileOperations.Clear(origin, in context);
        }

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context) =>
            TileOperations.Place(tiles, in context);

        public override bool Consume(in PasteTile tile, in PasteContext context) =>
            TileOperations.Consume(in tile, in context);
    }
}
