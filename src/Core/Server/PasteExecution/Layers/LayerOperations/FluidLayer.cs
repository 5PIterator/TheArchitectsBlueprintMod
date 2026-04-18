using System.Collections.Generic;
using ArchitectsStencilMod.Core;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace ArchitectsStencilMod.Core.Layers
{
    internal sealed class FluidLayer : Layer
    {
        public static readonly FluidLayer Definition = new();

        private FluidLayer() : base("Fluid", LayerType.Liquid) { }

        public override IReadOnlyList<Blueprint.BoundedTile> Identify(in PasteTile tile, in PasteTileLookup _) =>
            tile.Subject.LiquidAmount > 0 ? [new Blueprint.BoundedTile(tile.Relative, selected: true)] : [];

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context) =>
            tile.Target.LiquidAmount > 0;

        public override bool CanPlace(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool CanConsume(in PasteTile tile, in PasteContext context) => true;

        public override bool Match(in PasteTile tile) =>
            tile.Source.LiquidAmount == tile.Target.LiquidAmount && tile.Source.LiquidType == tile.Target.LiquidType;

        public override IReadOnlyList<int>? GetItem(in PasteTile tile) => null;

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            var tile = tiles[0];
            int x = tile.World.X, y = tile.World.Y;
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
            int x = tile.World.X, y = tile.World.Y;
            var src = tile.PayloadCell.Liquid;
            int idx = x + y * (int)Main.tile.Width;
            var liquids = Main.tile.GetData<LiquidData>();
            liquids[idx].Amount = (byte)src.Amount;
            liquids[idx].LiquidType = (byte)src.Type;

            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.LiquidUpdate, -1, -1, null, x, y);

            return LayerResult.Ok(tile.World);
        }

        public override bool Consume(in PasteTile tile, in PasteContext context) => true;
    }
}
