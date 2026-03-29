using System.Collections.Generic;
using ArchitectsStencilMod.Core;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core.Layers
{
    internal sealed class UnknownLayer : Layer
    {
        public static readonly UnknownLayer Definition = new();

        private UnknownLayer() : base("Unknown", LayerType.Block) { }

        public static bool IsUnknownTileType(ushort tileType) => tileType >= TileLoader.TileCount;

        public override bool Match(in PasteTile tile) =>
            tile.Source.HasTile == tile.Target.HasTile && (!tile.Source.HasTile || tile.Source.TileType == tile.Target.TileType);

        public override IReadOnlyList<Blueprint.BoundedTile> Identify(Point rel, in Tile sourceTile, in PasteLookupQuery lookup) =>
            sourceTile.HasTile && IsUnknownTileType(sourceTile.TileType) ? [new Blueprint.BoundedTile(rel, selected: true)] : NoIdentify();

        public override bool CanPlace(IReadOnlyList<Blueprint.BoundedTile> identifiedTiles, in PasteLookupQuery lookup, in PasteContext context) =>
            LayerPlacementPlan.TryReserveForPlacement(this, identifiedTiles, in lookup, in context, reserveEachTileInStack: false);

        public override IReadOnlyList<int>? GetItem(in Tile sourceTile) => null;

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context) =>
            LayerResult.Ok();

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context) =>
            LayerResult.Ok();

        public override bool Consume(in PasteTile tile, in PasteContext context) =>
            ConsumeNoOp(in tile, in context);
    }
}
