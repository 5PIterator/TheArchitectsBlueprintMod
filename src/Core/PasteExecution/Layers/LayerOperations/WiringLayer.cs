using System.Collections.Generic;
using ArchitectsStencilMod.Core;
using Microsoft.Xna.Framework;
using Terraria;

namespace ArchitectsStencilMod.Core.Layers
{
    internal sealed class WiringLayer : Layer
    {
        public static readonly WiringLayer Definition = new();

        private WiringLayer() : base("Wiring", LayerType.Wiring) { }

        public override IReadOnlyList<Blueprint.BoundedTile> Identify(Point rel, in Tile sourceTile, in PasteLookupQuery lookup)
        {
            bool hasWiring = sourceTile.RedWire || sourceTile.BlueWire || sourceTile.GreenWire || sourceTile.YellowWire || sourceTile.HasActuator;
            return hasWiring ? [new Blueprint.BoundedTile(rel, selected: true)] : NoIdentify();
        }

        public override bool CanPlace(IReadOnlyList<Blueprint.BoundedTile> identifiedTiles, in PasteLookupQuery lookup, in PasteContext context) =>
            LayerPlacementPlan.TryReserveForPlacement(this, identifiedTiles, in lookup, in context, reserveEachTileInStack: false);

        public override bool Match(in PasteTile tile) =>
            tile.Source.RedWire == tile.Target.RedWire
            && tile.Source.BlueWire == tile.Target.BlueWire
            && tile.Source.GreenWire == tile.Target.GreenWire
            && tile.Source.YellowWire == tile.Target.YellowWire
            && tile.Source.HasActuator == tile.Target.HasActuator;

        public override IReadOnlyList<int>? GetItem(in Tile sourceTile) =>
            ModifierOperations.GetMechanismItems(in sourceTile);

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            var tile = tiles[0];
            ref var dest = ref ModifierOperations.GetState(tile.X, tile.Y);

            bool changed = false;
            if (dest.RedWire) { dest.RedWire = false; changed = true; }
            if (dest.BlueWire) { dest.BlueWire = false; changed = true; }
            if (dest.GreenWire) { dest.GreenWire = false; changed = true; }
            if (dest.YellowWire) { dest.YellowWire = false; changed = true; }
            if (dest.HasActuator) { dest.HasActuator = false; changed = true; }

            if (changed)
                LayerSync.SyncTileSquare(tile.X, tile.Y);

            return changed ? LayerResult.Ok(tile.World) : LayerResult.Ok();
        }

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            var tile = tiles[0];
            if (tile.Source.HasTile && ModifierOperations.IsMechanismTileType(tile.Source.TileType))
            {
                var placeResult = TileOperations.Place([tile], in context);
                if (!placeResult.Success)
                    return placeResult;
            }

            Tile src = tile.Source;
            ref var dest = ref ModifierOperations.GetState(tile.X, tile.Y);
            bool changed = false;

            if (dest.RedWire != src.RedWire) { dest.RedWire = src.RedWire; changed = true; }
            if (dest.BlueWire != src.BlueWire) { dest.BlueWire = src.BlueWire; changed = true; }
            if (dest.GreenWire != src.GreenWire) { dest.GreenWire = src.GreenWire; changed = true; }
            if (dest.YellowWire != src.YellowWire) { dest.YellowWire = src.YellowWire; changed = true; }
            if (dest.HasActuator != src.HasActuator) { dest.HasActuator = src.HasActuator; changed = true; }

            if (changed)
                LayerSync.SyncTileSquare(tile.X, tile.Y);

            return changed ? LayerResult.Ok(tile.World) : LayerResult.Ok();
        }

        public override bool Consume(in PasteTile tile, in PasteContext context)
        {
            if (!tile.Source.HasTile || !ModifierOperations.IsMechanismTileType(tile.Source.TileType))
                return true;

            return TileOperations.Consume(in tile, in context);
        }
    }
}
