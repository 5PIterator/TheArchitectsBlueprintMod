using System.Collections.Generic;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;
using Terraria;
namespace TheArchitectsBlueprintMod.Core.Layers
{
    internal sealed class OtherAnchoredLayer : Layer
    {
        public static readonly OtherAnchoredLayer Definition = new();

        private OtherAnchoredLayer() : base("OtherAnchored", LayerType.Frame) { }

        public override bool Match(in PasteTile tile) => TileComparison.MatchTileType(in tile);

        public override IReadOnlyList<Blueprint.BlueprintCell> Identify(in PasteTile tile, in PasteTileLookup lookup, PasteSubject identifySubject)
        {
            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            if (!FrameOperations.HasOtherAnchor(subjectTile))
                return [];

            IReadOnlyList<Blueprint.BlueprintCell>? tiles = FrameOperations.GetFrameTilesAsBounded(tile.Relative, subjectTile, lookup);
            return tiles ?? [];
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context) =>
            Main.tile[tile.World.X, tile.World.Y].HasTile;

        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool HasRequiredItems(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            if (!context.RequireItems || context.Items is null || context.IsSweepCheat) return true;
            if (!tile.PayloadCell.HasTile()) return true;
            int style = tile.PayloadCell.Detail.Style;
            return InventoryManager.FindItemByTile(context.Items, (ushort)tile.PayloadCell.Block.Type, style) is not null;
        }

        public override bool CanConsume(in PasteTile tile, in PasteContext context)
        {
            if (!tile.PayloadCell.HasTile() || !context.RequireItems || context.Items is null) return true;
            int style = tile.PayloadCell.Detail.Style;
            return InventoryManager.FindItemByTile(context.Items, (ushort)tile.PayloadCell.Block.Type, style) is not null;
        }

        public override bool Consume(in PasteTile tile, in PasteContext context) =>
            FrameOperations.Consume(in tile, in context);

        public override IReadOnlyList<int>? GetItem(in PasteTile tile)
        {
            if (!tile.PayloadCell.HasTile()) return null;
            int style = tile.PayloadCell.Detail.Style;
            return InteractionMapProvider.GetTileItemIds((ushort)tile.PayloadCell.Block.Type, style);
        }

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context) =>
            FrameOperations.Clear(tiles, in context);

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            var res = FrameOperations.Place(tiles, in context);
            if (!res.Success || res.RemainingTiles.Count > 0 || tiles.Count == 0)
                return res;

            var origin = FrameOperations.GetPlacementOriginTile(tiles);
            if (!context.IsSweepCheat && !Consume(in origin, in context))
                return LayerResult.Fail(origin.World, LayerFailReason.MissingItems);

            return res;
        }
    }
}
