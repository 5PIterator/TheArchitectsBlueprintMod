using System.Collections.Generic;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    [CustomPasteLayer]
    /// <summary>Identifies and places non-grass plants and regrowth tiles.</summary>
    internal sealed class PlantLayer : Layer
    {
        public static readonly PlantLayer Definition = new();

        private PlantLayer() : base("Plant", LayerType.Custom) { }

        /// <summary>Checks tile-type equality between payload/source intent and target world.</summary>
        public override bool Match(in PasteTile tile) => TileComparison.MatchTileType(in tile);

        /// <summary>Returns the bounded footprint claimed by this plant tile.</summary>
        public override LayerResult Identify(in PasteTile tile, in PasteTileLookup lookup, PasteSubject identifySubject)
        {
            // --- Classify the identify subject, then capture either a frame footprint or a single tile.
            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            if (!IsPlantTile(subjectTile))
                return LayerResult.IdentifyNone();

            string message = LayerLogFormat.FormatTile(subjectTile);
            IReadOnlyList<Blueprint.BlueprintCell>? tiles = FrameOperations.GetFrameTilesAsBounded(tile.Relative, subjectTile, lookup);
            if (tiles is { Count: > 0 })
                return LayerResult.Identified(TileBatch.FromCells(tiles), message);

            return LayerResult.Identified(
                TileBatch.FromCells([new Blueprint.BlueprintCell(tile.Relative, tile.World, true)]),
                message);
        }

        /// <summary>Checks whether this layer should own the provided tile instance.</summary>
        private static bool IsPlantTile(Tile tile)
        {
            // --- Reject non-tiles and unsupported ids, then keep only plant-like tiles that are not owned elsewhere.
            if (!tile.HasTile || !TileLoaderSupport.IsKnownTileType(tile.TileType))
                return false;

            if (TileID.Sets.Falling[tile.TileType])
                return false;

            if (FrameLayer.IsFrameTile(tile))
                return false;

            ushort tileType = tile.TileType;
            if (IsGrassBlockType(tileType) || IsVineTileType(tileType) || IsTreeTileType(tileType)
                || tileType == TileID.Cactus || tileType == TileID.Bamboo)
                return false;

            return IsThornTileType(tileType)
                || IsRegrowthPlantTile(tileType)
                || RequiresGroundSupport(tileType)
                || IsSwayingPlantTile(tileType);
        }

        /// <summary>Checks whether this layer should claim the provided tile type.</summary>
        private static bool IsPlantTileType(ushort tileType)
        {
            // --- Mirror tile instance classification so BlockLayer can exclude only plant-owned ids.
            if (!TileLoaderSupport.IsKnownTileType(tileType))
                return false;

            if (FrameLayer.IsFrameTileType(tileType))
                return false;

            if (IsGrassBlockType(tileType) || IsVineTileType(tileType) || IsTreeTileType(tileType)
                || tileType == TileID.Cactus || tileType == TileID.Bamboo)
                return false;

            return IsThornTileType(tileType)
                || IsRegrowthPlantTile(tileType)
                || RequiresGroundSupport(tileType)
                || IsSwayingPlantTile(tileType);
        }

        /// <summary>Returns true for corruption, crimson, jungle, and Plantera thorn bushes.</summary>
        public static bool IsThornTileType(ushort tileType) =>
            tileType == TileID.CorruptThorns
            || tileType == TileID.CrimsonThorns
            || tileType == TileID.JungleThorns
            || tileType == TileID.PlanteraThorns;

        /// <summary>Returns true when the tile is a vine or reverse vine family tile.</summary>
        private static bool IsVineTileType(ushort tileType) =>
            TileLoaderSupport.IsKnownTileType(tileType)
            && (TileID.Sets.IsVine[tileType] || TileID.Sets.ReverseVineThreads[tileType]);

        /// <summary>Returns true when the tile belongs to grass block families handled by BlockLayer.</summary>
        private static bool IsGrassBlockType(ushort tileType)
        {
            if (!TileLoaderSupport.IsKnownTileType(tileType))
                return false;

            return TileID.Sets.Grass[tileType]
                || TileID.Sets.GrassSpecial[tileType]
                || TileID.Sets.SpreadOverground[tileType]
                || TileID.Sets.SpreadUnderground[tileType]
                || TileID.Sets.NeedsGrassFraming[tileType];
        }

        /// <summary>Returns true when the tile belongs to tree or tree-adjacent families.</summary>
        private static bool IsTreeTileType(ushort tileType)
        {
            if (!TileLoaderSupport.IsKnownTileType(tileType))
                return false;

            return TileID.Sets.IsATreeTrunk[tileType]
                || TileID.Sets.IsShakeable[tileType]
                || TileID.Sets.CountsAsGemTree[tileType]
                || TileID.Sets.GetsCheckedForLeaves[tileType]
                || TileID.Sets.TreeSapling[tileType]
                || TileID.Sets.CommonSapling[tileType]
                || BlockLayer.IsLeafBlock(tileType);
        }

        /// <summary>Returns true for Staff-of-Regrowth plants that are not grass, vines, trees, or cactus.</summary>
        private static bool IsRegrowthPlantTile(ushort tileType)
        {
            // --- Use the regrowth set as the broad classifier, then subtract families owned by other layers.
            if (!TileLoaderSupport.IsKnownTileType(tileType) || !TileID.Sets.TileCutIgnore.Regrowth[tileType])
                return false;

            if (IsGrassBlockType(tileType) || IsVineTileType(tileType) || IsTreeTileType(tileType)
                || tileType == TileID.Cactus || tileType == TileID.Bamboo)
                return false;

            return true;
        }

        /// <summary>Returns true for vanilla one-tile plants that require a supporting ground tile.</summary>
        private static bool RequiresGroundSupport(ushort tileType) =>
            tileType == TileID.Plants
            || tileType == TileID.JunglePlants
            || tileType == TileID.HallowedPlants
            || tileType == TileID.CrimsonPlants
            || tileType == TileID.CorruptPlants;

        /// <summary>Returns true for small swaying plants that are not saplings or other owned families.</summary>
        private static bool IsSwayingPlantTile(ushort tileType)
        {
            if (!TileLoaderSupport.IsKnownTileType(tileType) || !TileID.Sets.SwaysInWindBasic[tileType])
                return false;

            if (TileID.Sets.TreeSapling[tileType] || TileID.Sets.CommonSapling[tileType])
                return false;

            return !IsGrassBlockType(tileType) && !IsVineTileType(tileType) && !IsTreeTileType(tileType)
                && tileType != TileID.Cactus && tileType != TileID.Bamboo;
        }

        /// <summary>Returns true when the tile places through TileOperations rather than FrameOperations.</summary>
        private static bool UsesTilePlacement(IReadOnlyList<PasteTile> tiles, in PasteTile origin)
        {
            // --- Keep one-tile and base-conversion plants on PlaceTile; route larger anchored frames through PlaceObject.
            if (!origin.PayloadCell.HasTile())
                return false;

            ushort tileType = origin.PayloadCell.Block.Type;
            if (!TileLoaderSupport.IsKnownTileType(tileType))
                return false;

            int style = origin.PayloadCell.ResolvePlacementStyle();
            if (RequiresGroundSupport(tileType) || UsesBaseTilePlacement(tileType, style))
                return true;

            if (tiles.Count != 1)
                return false;

            TileObjectData? data = TileObjectData.GetTileData(tileType, style);
            return data is null || (data.Width <= 1 && data.Height <= 1);
        }

        /// <summary>Returns true when placement requires an interaction-map support tile first.</summary>
        private static bool UsesBaseTilePlacement(ushort tileType, int style) =>
            InteractionMapProvider.GetRequiredBaseTile(tileType, style).HasValue;

        /// <summary>Checks whether the target tile currently has anything to clear.</summary>
        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context) =>
            Main.tile[tile.World.X, tile.World.Y].HasTile;

        /// <summary>Validates world-side placement constraints for this layer.</summary>
        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        /// <summary>Checks whether inventory has the required item for this tile when item checks are enabled.</summary>
        public override bool HasRequiredItems(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            // --- Skip checks when requirements are disabled; otherwise query interaction-map item ids for payload tile + style.
            if (!context.RequireItems || context.Items is null || context.IsSweepCheat) return true;
            if (!tile.PayloadCell.HasTile()) return true;
            int style = tile.PayloadCell.ResolvePlacementStyle();
            return TileExecutor.HasItemForTile((ushort)tile.PayloadCell.Block.Type, style, in context);
        }

        /// <summary>Checks whether this tile can consume an inventory item now.</summary>
        public override bool CanConsume(in PasteTile tile, in PasteContext context)
        {
            // --- Use the same tile+style item lookup as planning checks to keep queue behavior deterministic.
            if (!tile.PayloadCell.HasTile() || !context.RequireItems || context.Items is null) return true;
            int style = tile.PayloadCell.ResolvePlacementStyle();
            return TileExecutor.HasItemForTile((ushort)tile.PayloadCell.Block.Type, style, in context);
        }

        /// <summary>Consumes one item using the correct placement path for this tile.</summary>
        public override bool Consume(in PasteTile tile, in PasteContext context)
        {
            // --- Tile-based plants consume through TileOperations; framed plants consume through FrameOperations.
            if (!tile.PayloadCell.HasTile())
                return true;

            ushort tileType = tile.PayloadCell.Block.Type;
            int style = tile.PayloadCell.ResolvePlacementStyle();
            return RequiresGroundSupport(tileType) || UsesBaseTilePlacement(tileType, style)
                ? TileOperations.Consume(in tile, in context)
                : FrameOperations.Consume(in tile, in context);
        }

        /// <summary>Returns candidate inventory items that can place this payload tile.</summary>
        public override IReadOnlyList<int>? GetItem(in PasteTile tile)
        {
            // --- Resolve tile-item mappings from the interaction map using payload tile type + style.
            if (!tile.PayloadCell.HasTile()) return null;
            int style = tile.PayloadCell.ResolvePlacementStyle();
            return InteractionMapProvider.GetTileItemIds((ushort)tile.PayloadCell.Block.Type, style);
        }

        /// <summary>Clears plant tiles from the world before placement.</summary>
        public override LayerResult Clear(TileBatch batch, in PasteContext context) =>
            FrameOperations.Clear(batch.Tiles, in context);

        /// <summary>Places plants through tile or frame placement, then consumes inventory when successful.</summary>
        public override LayerResult Place(TileBatch batch, in PasteContext context)
        {
            // --- Choose placement backend per batch shape/type, then handle retries and consumption consistently.
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            PasteTile origin = tiles.Count > 0 ? tiles[0] : default;
            string message = origin.PayloadCell.HasTile()
                ? LayerLogFormat.FormatTile(origin.PayloadTile)
                : LayerLogFormat.FormatFromSubject(in origin, PasteSubject.Payload);
            bool useTilePlace = UsesTilePlacement(tiles, in origin);
            LayerResult result = useTilePlace
                ? TileOperations.Place(tiles, in context)
                : FrameOperations.Place(tiles, in context);
            if (result.Success)
            {
                if (result.RemainingTiles.Count > 0 || tiles.Count == 0)
                    return result;

                PasteTile placementOrigin = useTilePlace ? origin : FrameOperations.GetPlacementOriginTile(tiles);
                if (!context.IsSweepCheat && !Consume(in placementOrigin, in context))
                    return LayerResult.Fail(
                        LayerFailReason.MissingItems,
                        LayerLogFormat.FormatTile(placementOrigin.PayloadTile));

                return result;
            }

            if (result.FailReason == LayerFailReason.InvalidPlacement && tiles.Count > 0)
            {
                if (!context.RetryOnInvalidPlacement)
                    return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
                return LayerResult.OkWithRemaining(batch, message, LayerLogFormat.PlacedLabel(in context));
            }

            return result;
        }

        /// <summary>Returns true when this custom layer owns the given tile type.</summary>
        public override bool IsCustom(ushort tileType) => IsPlantTileType(tileType);
    }
}
