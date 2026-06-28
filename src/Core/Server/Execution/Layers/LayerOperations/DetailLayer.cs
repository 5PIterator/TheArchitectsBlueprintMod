using System.Collections.Generic;
using TheArchitectsBlueprintMod.Core;
using Terraria;
using Terraria.Enums;
using Terraria.ID;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    internal sealed class DetailLayer : Layer
    {
        public static readonly DetailLayer Definition = new();

        private DetailLayer() : base("Detail", LayerType.Detail) { }

        public override IReadOnlyList<Blueprint.BlueprintCell> Identify(in PasteTile tile, in PasteTileLookup _, PasteSubject identifySubject)
        {
            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            bool hasDetail = subjectTile.IsActuated || subjectTile.BlockType != BlockType.Solid;
            return hasDetail ? [new Blueprint.BlueprintCell(tile.Relative, tile.World, true)] : [];
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            Tile t = tile.Target;
            return t.IsActuated || t.BlockType != BlockType.Solid;
        }

        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool HasRequiredItems(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool CanConsume(in PasteTile tile, in PasteContext context) => true;

        public override bool Consume(in PasteTile tile, in PasteContext context) => true;

        public override bool Match(in PasteTile tile)
        {
            if (TileComparison.IsSourceCutIntent(in tile))
                return tile.Source.IsActuated == tile.Target.IsActuated && tile.Source.BlockType == tile.Target.BlockType;
            Tile intent = tile.PayloadTile;
            return intent.IsActuated == tile.Target.IsActuated && intent.BlockType == tile.Target.BlockType;
        }

        public override IReadOnlyList<int>? GetItem(in PasteTile tile) => null;

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            var tile = tiles[0];
            int wx = tile.World.X, wy = tile.World.Y;
            Tile live = Main.tile[wx, wy];

            bool changed = false;
            if (live.IsActuated)
            {
                TileWallWireStateData state = DetailOperations.ReadState(wx, wy);
                state.IsActuated = false;
                DetailOperations.WriteState(wx, wy, in state);
                changed = true;
            }

            if (live.IsHalfBlock)
            {
                WorldGen.PoundTile(wx, wy);
                changed = true;
            }

            if (live.Slope != SlopeType.Solid)
            {
                WorldGen.SlopeTile(wx, wy, 0);
                changed = true;
            }
            else if (live.BlockType != BlockType.Solid)
            {
                TileWallWireStateData state = DetailOperations.ReadState(wx, wy);
                state.BlockType = BlockType.Solid;
                DetailOperations.WriteState(wx, wy, in state);
                changed = true;
            }

            return changed ? LayerResult.Ok(tile.World) : LayerResult.Ok();
        }

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            var tile = tiles[0];
            var src = tile.PayloadCell.Detail;
            int wx = tile.World.X, wy = tile.World.Y;
            if (!Main.tile[wx, wy].HasTile)
                return LayerResult.Ok();

            Tile live = Main.tile[wx, wy];
            bool changed = false;

            if (src.IsHalfBlock != live.IsHalfBlock)
            {
                WorldGen.PoundTile(wx, wy);
                changed = true;
                live = Main.tile[wx, wy];
            }

            if (src.Slope != live.Slope)
            {
                if (src.Slope == SlopeType.Solid)
                    WorldGen.SlopeTile(wx, wy, 0);
                else
                    WorldGen.SlopeTile(wx, wy, (int)src.Slope, noEffects: false);
                changed = true;
                live = Main.tile[wx, wy];
            }

            TileWallWireStateData state = DetailOperations.ReadState(wx, wy);
            bool stateChanged = false;
            if (state.IsActuated != src.IsActuated)
            {
                state.IsActuated = src.IsActuated;
                stateChanged = true;
            }

            if (state.BlockType != src.BlockType)
            {
                state.BlockType = src.BlockType;
                stateChanged = true;
            }

            if (stateChanged)
            {
                DetailOperations.WriteState(wx, wy, in state);
                changed = true;
            }

            return changed ? LayerResult.Ok(tile.World) : LayerResult.Ok();
        }
    }
}
