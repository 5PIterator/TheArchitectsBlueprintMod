using System.Collections.Generic;
using TheArchitectsBlueprintMod.Core;
using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    internal sealed class UnknownLayer : Layer
    {
        public static readonly UnknownLayer Definition = new();

        private UnknownLayer() : base("Unknown", LayerType.Block) { }

        public static bool IsUnknownTileType(ushort tileType) => tileType >= TileLoader.TileCount;

        public override bool Match(in PasteTile tile)
        {
            if (TileComparison.IsSourceCutIntent(in tile))
                return tile.Source.HasTile == tile.Target.HasTile && (!tile.Source.HasTile || tile.Source.TileType == tile.Target.TileType);
            Tile intent = tile.PayloadTile;
            return intent.HasTile == tile.Target.HasTile && (!intent.HasTile || intent.TileType == tile.Target.TileType);
        }

        public override IReadOnlyList<Blueprint.BlueprintCell> Identify(in PasteTile tile, in PasteTileLookup _, PasteSubject identifySubject)
        {
            Tile view = PasteTile.GetTileForIdentify(in tile, identifySubject);
            return view.HasTile && IsUnknownTileType(view.TileType) ? [new Blueprint.BlueprintCell(tile.Relative, tile.World, true)] : [];
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context) =>
            tile.Target.HasTile;

        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool HasRequiredItems(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool CanConsume(in PasteTile tile, in PasteContext context) => true;

        public override bool Consume(in PasteTile tile, in PasteContext context) => true;

        public override IReadOnlyList<int>? GetItem(in PasteTile tile) => null;

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context) =>
            LayerResult.Ok();

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context) =>
            LayerResult.Ok();
    }
}
