using System.Collections.Generic;
using Microsoft.Xna.Framework;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    /// <summary>
    /// Vegetation that needs ordered or deferred placement (columns, regrowth plants, surface plants).
    /// </summary>
    internal sealed class VegetationLayer : Layer
    {
        public static readonly VegetationLayer Definition = new();

        private VegetationLayer() : base("Vegetation", LayerType.Vegetation) { }

        public override bool Match(in PasteTile tile) => TileComparison.MatchTileType(in tile);

        public override IReadOnlyList<Blueprint.BlueprintCell> Identify(in PasteTile tile, in PasteTileLookup lookup, PasteSubject identifySubject)
        {
            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            if (!IsVegetationTile(subjectTile))
                return [];

            if (IsVegetationColumnTileType(subjectTile.TileType))
                return IdentifyVegetationColumn(in tile, in lookup, identifySubject, subjectTile.TileType);

            IReadOnlyList<Blueprint.BlueprintCell>? tiles = FrameOperations.GetFrameTilesAsBounded(tile.Relative, subjectTile, lookup);
            if (tiles is { Count: > 0 })
                return tiles;

            return [new Blueprint.BlueprintCell(tile.Relative, tile.World, true)];
        }

        internal static bool IsVegetationTile(Tile tile)
        {
            if (!tile.HasTile || tile.TileType >= TileLoader.TileCount)
                return false;

            if (TileID.Sets.Falling[tile.TileType])
                return false;

            return IsVegetationColumnTileType(tile.TileType)
                || IsRegrowthPlantTile(tile.TileType)
                || RequiresGroundSupport(tile.TileType);
        }

        /// <summary>Tile types that should not be claimed by <see cref="TreeLayer"/>.</summary>
        internal static bool ExcludesTreeLayer(ushort tileType) =>
            IsVegetationTileType(tileType) || BlockLayer.IsLeafBlock(tileType);

        internal static bool IsVegetationTileType(ushort tileType)
        {
            if (tileType >= TileLoader.TileCount)
                return false;

            return IsVegetationColumnTileType(tileType)
                || IsRegrowthPlantTile(tileType)
                || RequiresGroundSupport(tileType)
                || TileID.Sets.IsATreeTrunk[tileType]
                || TileID.Sets.IsShakeable[tileType]
                || TileID.Sets.CountsAsGemTree[tileType]
                || TileID.Sets.GetsCheckedForLeaves[tileType];
        }

        private static bool IsVegetationColumnTileType(ushort tileType)
        {
            if (tileType >= TileLoader.TileCount)
                return false;

            return TileID.Sets.IsVine[tileType]
                || TileID.Sets.ReverseVineThreads[tileType]
                || tileType == TileID.Cactus;
        }

        private static bool IsRegrowthPlantTile(ushort tileType)
        {
            if (tileType >= TileLoader.TileCount || !TileID.Sets.TileCutIgnore.Regrowth[tileType])
                return false;

            if (TileID.Sets.Grass[tileType])
                return false;
            if (TileID.Sets.IsVine[tileType])
                return false;
            if (TileID.Sets.ReverseVineThreads[tileType])
                return false;
            if (tileType == TileID.Cactus)
                return false;
            if (TileID.Sets.IsATreeTrunk[tileType])
                return false;

            return true;
        }

        private static bool RequiresGroundSupport(ushort tileType) =>
            tileType == TileID.Plants
            || tileType == TileID.JunglePlants
            || tileType == TileID.HallowedPlants
            || tileType == TileID.CrimsonPlants
            || tileType == TileID.CorruptPlants;

        private static IReadOnlyList<Blueprint.BlueprintCell> IdentifyVegetationColumn(
            in PasteTile tile,
            in PasteTileLookup lookup,
            PasteSubject identifySubject,
            ushort tileType)
        {
            bool sourceCut = TileComparison.IsSourceCutIntent(in tile);
            PasteSubject columnSubject = sourceCut ? PasteSubject.Source : identifySubject;

            var list = new List<Blueprint.BlueprintCell>();
            for (int dy = 0; ; dy--)
            {
                var p = new Point(tile.Relative.X, tile.Relative.Y + dy);
                if (!lookup.TryGetPasteTile(p, out PasteTile lookupTile))
                    break;
                Tile t = PasteTile.GetTileForIdentify(in lookupTile, columnSubject);
                if (!t.HasTile || t.TileType != tileType)
                    break;
                list.Insert(0, new Blueprint.BlueprintCell(p, ColumnCellWorld(in tile, p, in lookupTile, sourceCut), true));
            }

            for (int dy = 1; ; dy++)
            {
                var p = new Point(tile.Relative.X, tile.Relative.Y + dy);
                if (!lookup.TryGetPasteTile(p, out PasteTile lookupTile))
                    break;
                Tile t = PasteTile.GetTileForIdentify(in lookupTile, columnSubject);
                if (!t.HasTile || t.TileType != tileType)
                    break;
                list.Add(new Blueprint.BlueprintCell(p, ColumnCellWorld(in tile, p, in lookupTile, sourceCut), true));
            }

            return list.Count > 0 ? list : [];
        }

        private static Point ColumnCellWorld(in PasteTile anchor, Point relative, in PasteTile lookupTile, bool sourceCut) =>
            sourceCut
                ? new Point(anchor.World.X + relative.X - anchor.Relative.X, anchor.World.Y + relative.Y - anchor.Relative.Y)
                : lookupTile.World;

        private static bool UsesBaseTilePlacement(ushort tileType, int style) =>
            InteractionMapProvider.GetRequiredBaseTile(tileType, style).HasValue;

        private static bool UsesTilePlacement(IReadOnlyList<PasteTile> tiles, in PasteTile origin)
        {
            if (!origin.PayloadCell.HasTile())
                return false;

            ushort tileType = origin.PayloadCell.Block.Type;
            if (tileType >= TileLoader.TileCount)
                return false;

            int style = origin.PayloadCell.Detail.Style;
            if (RequiresGroundSupport(tileType)
                || UsesBaseTilePlacement(tileType, style)
                || TileID.Sets.Grass[tileType])
                return true;

            if (tiles.Count != 1)
                return false;

            var data = TileObjectData.GetTileData(tileType, style);
            return data is null || (data.Width <= 1 && data.Height <= 1);
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

        public override bool Consume(in PasteTile tile, in PasteContext context)
        {
            if (!tile.PayloadCell.HasTile())
                return true;

            ushort tileType = tile.PayloadCell.Block.Type;
            int style = tile.PayloadCell.Detail.Style;
            return RequiresGroundSupport(tileType) || UsesBaseTilePlacement(tileType, style) || TileID.Sets.Grass[tileType]
                ? TileOperations.Consume(in tile, in context)
                : FrameOperations.Consume(in tile, in context);
        }

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
            if (IsVegetationColumn(tiles))
                return PlaceVegetationColumn(tiles, in context);

            var origin = tiles.Count > 0 ? tiles[0] : default;
            bool useTilePlace = UsesTilePlacement(tiles, in origin);
            var res = useTilePlace
                ? TileOperations.Place(tiles, in context)
                : FrameOperations.Place(tiles, in context);
            if (res.Success)
            {
                if (res.RemainingTiles.Count > 0 || tiles.Count == 0)
                    return res;

                var placementOrigin = useTilePlace ? origin : FrameOperations.GetPlacementOriginTile(tiles);
                if (!context.IsSweepCheat && !Consume(in placementOrigin, in context))
                    return LayerResult.Fail(placementOrigin.World, LayerFailReason.MissingItems);

                return res;
            }

            if (res.FailReason == LayerFailReason.InvalidPlacement && tiles.Count > 0)
            {
                if (!context.RetryOnInvalidPlacement)
                    return LayerResult.Fail(origin.World, LayerFailReason.InvalidPlacement);
                return LayerResult.OkWithRemaining(tiles);
            }

            return res;
        }

        private static bool IsVegetationColumn(IReadOnlyList<PasteTile> tiles)
        {
            if (tiles.Count == 0)
                return false;

            ushort tileType = tiles[0].PayloadCell.Block.Type;
            if (!IsVegetationColumnTileType(tileType))
                return false;

            for (int i = 0; i < tiles.Count; i++)
            {
                if (!tiles[i].PayloadCell.HasTile() || tiles[i].PayloadCell.Block.Type != tileType)
                    return false;
            }

            return true;
        }

        private static LayerResult PlaceVegetationColumn(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            var pending = new List<PasteTile>(tiles);
            bool placeTopDown = pending[0].PayloadCell.Block.Type < TileLoader.TileCount
                && TileID.Sets.IsVine[pending[0].PayloadCell.Block.Type];
            Point lastPlaced = pending[0].World;

            while (pending.Count > 0)
            {
                int nextIdx = SelectNextVegetationSegmentIndex(pending, placeTopDown);
                PasteTile segment = pending[nextIdx];
                LayerResult res = TileOperations.Place([segment], in context);
                if (!res.Success)
                {
                    if (res.FailReason == LayerFailReason.InvalidPlacement && context.RetryOnInvalidPlacement)
                        return LayerResult.OkWithRemaining(pending);
                    return res;
                }

                if (!context.IsSweepCheat && !Definition.Consume(in segment, in context))
                    return LayerResult.Fail(segment.World, LayerFailReason.MissingItems);

                lastPlaced = segment.World;
                pending.RemoveAt(nextIdx);
            }

            return LayerResult.Ok(lastPlaced);
        }

        private static int SelectNextVegetationSegmentIndex(IReadOnlyList<PasteTile> pending, bool placeTopDown)
        {
            int nextIdx = 0;
            for (int i = 1; i < pending.Count; i++)
            {
                int candidateY = pending[i].Relative.Y;
                int selectedY = pending[nextIdx].Relative.Y;
                if (placeTopDown ? candidateY < selectedY : candidateY > selectedY)
                    nextIdx = i;
            }

            return nextIdx;
        }
    }
}
