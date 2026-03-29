using System.Collections.Generic;
using ArchitectsStencilMod.Core;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace ArchitectsStencilMod.Core.Layers
{
    internal sealed class FluidLayer : Layer
    {
        public static readonly FluidLayer Definition = new();

        private FluidLayer() : base("Fluid", LayerType.Liquid) { }

        public override IReadOnlyList<Blueprint.BoundedTile> Identify(Point rel, in Tile sourceTile, in PasteLookupQuery lookup) =>
            sourceTile.LiquidAmount > 0 ? [new Blueprint.BoundedTile(rel, selected: true)] : NoIdentify();

        public override bool CanPlace(IReadOnlyList<Blueprint.BoundedTile> identifiedTiles, in PasteLookupQuery lookup, in PasteContext context) =>
            LayerPlacementPlan.TryReserveForPlacement(this, identifiedTiles, in lookup, in context, reserveEachTileInStack: false);

        public override bool Match(in PasteTile tile) =>
            tile.Source.LiquidAmount == tile.Target.LiquidAmount && tile.Source.LiquidType == tile.Target.LiquidType;

        public override IReadOnlyList<int>? GetItem(in Tile sourceTile) => null;

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            var tile = tiles[0];
            int x = tile.X, y = tile.Y;
            int idx = x + y * (int)Main.tile.Width;
            var liquids = Main.tile.GetData<LiquidData>();
            liquids[idx].Amount = 0;
            liquids[idx].LiquidType = 0;

            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.LiquidUpdate, -1, -1, null, x, y);

            return LayerResult.Ok(tile.World);
        }

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            var tile = tiles[0];
            int x = tile.X, y = tile.Y;
            Tile src = tile.Source;
            int idx = x + y * (int)Main.tile.Width;
            var liquids = Main.tile.GetData<LiquidData>();
            liquids[idx].Amount = src.LiquidAmount;
            liquids[idx].LiquidType = src.LiquidType;

            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.LiquidUpdate, -1, -1, null, x, y);

            return LayerResult.Ok(tile.World);
        }

        public override bool Consume(in PasteTile tile, in PasteContext context) =>
            ConsumeNoOp(in tile, in context);
    }
}
