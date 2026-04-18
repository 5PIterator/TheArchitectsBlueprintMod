using System.Collections.Generic;
using ArchitectsStencilMod.Core;
using Terraria;

namespace ArchitectsStencilMod.Core.Layers
{
    internal sealed class WiringLayer : Layer
    {
        public static readonly WiringLayer Definition = new();

        private WiringLayer() : base("Wiring", LayerType.Wiring) { }

        public override IReadOnlyList<Blueprint.BoundedTile> Identify(in PasteTile tile, in PasteTileLookup _)
        {
            Tile subjectTile = tile.Subject;
            bool hasWiring = subjectTile.RedWire || subjectTile.BlueWire || subjectTile.GreenWire || subjectTile.YellowWire || subjectTile.HasActuator;
            return hasWiring ? [new Blueprint.BoundedTile(tile.Relative, selected: true)] : [];
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            Tile t = tile.Target;
            return t.RedWire || t.BlueWire || t.GreenWire || t.YellowWire || t.HasActuator;
        }

        public override bool CanPlace(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            if (!context.NeedsItems || context.Slots is null) return true;
            IReadOnlyList<int>? items = GetItem(in tile);
            return items is null || items.Count == 0 || context.Slots.FindItemSlot(items) >= 0;
        }

        public override bool CanConsume(in PasteTile tile, in PasteContext context)
        {
            if (!tile.PayloadCell.HasTile() || !ModifierOperations.IsMechanismTileType((ushort)tile.PayloadCell.Block.Type))
                return true;

            if (!context.NeedsItems || context.Slots is null)
                return true;

            int style = tile.PayloadCell.Detail.Style;
            return context.Slots.FindTileSlot((ushort)tile.PayloadCell.Block.Type, style) >= 0;
        }

        public override bool Match(in PasteTile tile) =>
            tile.Source.RedWire == tile.Target.RedWire
            && tile.Source.BlueWire == tile.Target.BlueWire
            && tile.Source.GreenWire == tile.Target.GreenWire
            && tile.Source.YellowWire == tile.Target.YellowWire
            && tile.Source.HasActuator == tile.Target.HasActuator;

        public override IReadOnlyList<int>? GetItem(in PasteTile tile) =>
            ModifierOperations.GetMechanismItems(in tile.PayloadCell);

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            var tile = tiles[0];
            ref var dest = ref ModifierOperations.GetState(tile.World.X, tile.World.Y);

            bool changed = false;
            if (dest.RedWire) { dest.RedWire = false; changed = true; }
            if (dest.BlueWire) { dest.BlueWire = false; changed = true; }
            if (dest.GreenWire) { dest.GreenWire = false; changed = true; }
            if (dest.YellowWire) { dest.YellowWire = false; changed = true; }
            if (dest.HasActuator) { dest.HasActuator = false; changed = true; }

            if (changed)
                LayerSync.SyncTileSquare(tile.World.X, tile.World.Y);

            return changed ? LayerResult.Ok(tile.World) : LayerResult.Ok();
        }

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            var tile = tiles[0];
            if (tile.PayloadCell.HasTile() && ModifierOperations.IsMechanismTileType((ushort)tile.PayloadCell.Block.Type))
            {
                var placeResult = TileOperations.Place([tile], in context);
                if (!placeResult.Success)
                    return placeResult;
            }

            var src = tile.PayloadCell.Wire;
            ref var dest = ref ModifierOperations.GetState(tile.World.X, tile.World.Y);
            bool changed = false;

            if (dest.RedWire != src.RedWire) { dest.RedWire = src.RedWire; changed = true; }
            if (dest.BlueWire != src.BlueWire) { dest.BlueWire = src.BlueWire; changed = true; }
            if (dest.GreenWire != src.GreenWire) { dest.GreenWire = src.GreenWire; changed = true; }
            if (dest.YellowWire != src.YellowWire) { dest.YellowWire = src.YellowWire; changed = true; }
            if (dest.HasActuator != src.HasActuator) { dest.HasActuator = src.HasActuator; changed = true; }

            if (changed)
                LayerSync.SyncTileSquare(tile.World.X, tile.World.Y);

            return changed ? LayerResult.Ok(tile.World) : LayerResult.Ok();
        }

        public override bool Consume(in PasteTile tile, in PasteContext context)
        {
            if (!tile.PayloadCell.HasTile() || !ModifierOperations.IsMechanismTileType((ushort)tile.PayloadCell.Block.Type))
                return true;

            return TileOperations.Consume(in tile, in context);
        }
    }
}
