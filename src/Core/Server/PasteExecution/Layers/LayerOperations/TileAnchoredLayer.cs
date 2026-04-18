using System.Collections.Generic;
using ArchitectsStencilMod.Core;
using ArchitectsStencilMod.Core.TileMapping;
using Terraria;
namespace ArchitectsStencilMod.Core.Layers
{
    internal sealed class TileAnchoredLayer : Layer
    {
        public static readonly TileAnchoredLayer Definition = new();

        private TileAnchoredLayer() : base("TileAnchored", LayerType.Frame) { }

        public override bool Match(in PasteTile tile) => TileComparison.MatchTileType(in tile);

        public override IReadOnlyList<Blueprint.BoundedTile> Identify(in PasteTile tile, in PasteTileLookup lookup)
        {
            Tile subjectTile = tile.Subject;
            if (!FrameOperations.HasTileAnchor(subjectTile))
                return [];
            if (FrameOperations.HasWallAnchor(subjectTile) || FrameOperations.HasOtherAnchor(subjectTile))
                return [];

            IReadOnlyList<Blueprint.BoundedTile>? tiles = FrameOperations.GetFrameTilesAsBounded(tile.Relative, subjectTile, lookup);
            return tiles ?? [];
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context) =>
            Main.tile[tile.World.X, tile.World.Y].HasTile;

        public override bool CanPlace(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            if (!context.NeedsItems || context.Slots is null || context.CheatSweep) return true;
            if (!tile.PayloadCell.HasTile()) return true;
            int style = tile.PayloadCell.Detail.Style;
            return context.Slots.FindTileSlot((ushort)tile.PayloadCell.Block.Type, style) >= 0;
        }

        public override bool CanConsume(in PasteTile tile, in PasteContext context)
        {
            if (!tile.PayloadCell.HasTile() || !context.NeedsItems || context.Slots is null) return true;
            int style = tile.PayloadCell.Detail.Style;
            return context.Slots.FindTileSlot((ushort)tile.PayloadCell.Block.Type, style) >= 0;
        }

        public override IReadOnlyList<int>? GetItem(in PasteTile tile)
        {
            if (!tile.PayloadCell.HasTile()) return null;
            int style = tile.PayloadCell.Detail.Style;
            return InteractionMapProvider.GetTileItemIds((ushort)tile.PayloadCell.Block.Type, style);
        }

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context) =>
            FrameOperations.Clear(tiles, in context);

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context) =>
            FrameOperations.Place(tiles, in context);

        public override bool Consume(in PasteTile tile, in PasteContext context) =>
            FrameOperations.Consume(in tile, in context);
    }
}

