using System.Collections.Generic;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    /// <summary>
    /// Tile-anchored frames and multitiles (doors, furniture, etc.).
    /// </summary>
    internal sealed class FrameLayer : Layer
    {
        public static readonly FrameLayer Definition = new();

        private FrameLayer() : base("Frame", LayerType.Frame) { }

        public override bool Match(in PasteTile tile) => TileComparison.MatchTileType(in tile);

        public override LayerResult Identify(in PasteTile tile, in PasteTileLookup lookup, PasteSubject identifySubject)
        {
            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            if (!IsFrameTile(subjectTile))
                return LayerResult.IdentifyNone();

            string message = LayerLogFormat.FormatTile(subjectTile);
            IReadOnlyList<Blueprint.BlueprintCell>? tiles = FrameOperations.GetFrameTilesAsBounded(tile.Relative, subjectTile, lookup);
            if (tiles is { Count: > 0 })
                return LayerResult.Identified(TileBatch.FromCells(tiles), message);

            return LayerResult.Identified(
                TileBatch.FromCells([new Blueprint.BlueprintCell(tile.Relative, tile.World, true)]),
                message);
        }

        /// <summary>Returns true for tile-anchored or multi-cell objects that must use frame placement.</summary>
        internal static bool IsFrameTile(Tile tile)
        {
            // --- Reject air, mod tiles, and falling blocks; then classify anchors and multi-cell footprints.
            if (!tile.HasTile || !TileLoaderSupport.IsKnownTileType(tile.TileType))
                return false;

            if (TileID.Sets.Falling[tile.TileType])
                return false;

            if (HasFrameAnchor(tile))
                return true;

            int style = 0, alternate = 0;
            TileObjectData.GetTileInfo(tile, ref style, ref alternate);
            if (HasFrameAnchor(tile.TileType, style))
                return true;

            TileObjectData? data = TileObjectData.GetTileData(tile.TileType, style);
            return data is not null && (data.Width > 1 || data.Height > 1);
        }

        /// <summary>Type-only frame check for <see cref="CustomLayer"/> tile-type exclusion lists.</summary>
        internal static bool IsFrameTileType(ushort tileType)
        {
            if (!TileLoaderSupport.IsKnownTileType(tileType) || TileID.Sets.Falling[tileType])
                return false;

            if (HasFrameAnchor(tileType, 0))
                return true;

            TileObjectData? data = TileObjectData.GetTileData(tileType, 0);
            return data is not null && (data.Width > 1 || data.Height > 1);
        }

        private static bool HasFrameAnchor(Tile tile) =>
            FrameOperations.HasTileAnchor(tile)
            || FrameOperations.HasWallAnchor(tile)
            || FrameOperations.HasOtherAnchor(tile);

        private static bool HasFrameAnchor(ushort tileType, int style) =>
            FrameOperations.HasTileAnchor(tileType, style)
            || FrameOperations.HasWallAnchor(tileType, style)
            || FrameOperations.HasOtherAnchor(tileType, style);

        private static bool UsesBaseTilePlacement(ushort tileType, int style) =>
            InteractionMapProvider.GetRequiredBaseTile(tileType, style).HasValue;

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context) =>
            Main.tile[tile.World.X, tile.World.Y].HasTile;

        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            // --- Planning-time gate: only queue work when the origin can be placed with current world support.
            if (!tile.PayloadCell.HasTile() || context.IsSweepCheat)
                return true;

            var candidate = new FrameOperations.FrameCandidate(
                FrameOperations.ResolveOriginWorld(in tile, forClear: false),
                tile,
                [tile]);

            return FrameOperations.CanPlaceFrameCandidate(candidate, in context);
        }

        public override bool HasRequiredItems(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            if (!context.RequireItems || context.Items is null || context.IsSweepCheat) return true;
            if (!tile.PayloadCell.HasTile()) return true;
            int style = tile.PayloadCell.Detail.Style;
            return TileExecutor.HasItemForTile((ushort)tile.PayloadCell.Block.Type, style, in context);
        }

        public override bool CanConsume(in PasteTile tile, in PasteContext context)
        {
            if (!tile.PayloadCell.HasTile() || !context.RequireItems || context.Items is null) return true;
            int style = tile.PayloadCell.Detail.Style;
            return TileExecutor.HasItemForTile((ushort)tile.PayloadCell.Block.Type, style, in context);
        }

        public override bool Consume(in PasteTile tile, in PasteContext context)
        {
            if (!tile.PayloadCell.HasTile())
                return true;

            ushort tileType = tile.PayloadCell.Block.Type;
            int style = tile.PayloadCell.Detail.Style;
            return UsesBaseTilePlacement(tileType, style) || TileID.Sets.Grass[tileType]
                ? TileOperations.Consume(in tile, in context)
                : FrameOperations.Consume(in tile, in context);
        }

        public override IReadOnlyList<int>? GetItem(in PasteTile tile)
        {
            if (!tile.PayloadCell.HasTile()) return null;
            int style = tile.PayloadCell.Detail.Style;
            return InteractionMapProvider.GetTileItemIds((ushort)tile.PayloadCell.Block.Type, style);
        }

        public override LayerResult Clear(TileBatch batch, in PasteContext context)
        {
            // --- Clear one frame object per pass in support-safe order and requeue the remaining candidates.
            if (batch.IsEmpty)
                return LayerResult.Ok();

            List<FrameOperations.FrameCandidate> candidates = FrameOperations.GroupIntoCandidates(batch.Tiles, forClear: true);
            int index = FrameOperations.SelectNextClearCandidateIndex(candidates, in context);
            FrameOperations.FrameCandidate candidate = index >= 0
                ? candidates[index]
                : candidates[0];
            string message = FrameOperations.FormatCandidateMessage(candidate, forClear: true);
            string okLabel = LayerLogFormat.ClearedLabel;

            if (index < 0)
                return LayerResult.OkWithRemaining(batch, message, okLabel);

            PasteTile origin = FrameOperations.ResolveOriginPasteTile(candidate, forClear: true);
            if (!Main.tile[origin.World.X, origin.World.Y].HasTile)
                return FrameOperations.RemoveCandidateFromBatch(batch, candidate, message, okLabel);

            LayerResult result = FrameOperations.ClearOrigin(in origin, in context, message);
            if (!result.Success)
                return result;

            return FrameOperations.RemoveCandidateFromBatch(batch, candidate, result.Message, result.OkLabel);
        }

        public override LayerResult Place(TileBatch batch, in PasteContext context)
        {
            // --- Place only the first currently valid frame candidate, then requeue any remaining objects.
            if (batch.IsEmpty)
                return LayerResult.Ok();

            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            List<FrameOperations.FrameCandidate> candidates = FrameOperations.GroupIntoCandidates(tiles, forClear: false);
            IReadOnlyList<FrameOperations.FrameCandidate> allPending =
                context.PendingFramePlaceCandidates ?? candidates;
            string okLabel = LayerLogFormat.PlacedLabel(in context);
            int index = FrameOperations.SelectNextPlaceCandidateIndex(candidates, allPending, in context);
            FrameOperations.FrameCandidate candidate = index >= 0
                ? candidates[index]
                : candidates[0];
            string message = FrameOperations.FormatCandidateMessage(candidate, forClear: false);

            if (index < 0)
            {
                if (!context.RetryOnInvalidPlacement)
                    return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
                return LayerResult.OkWithRemaining(batch, message, okLabel);
            }

            if (FrameOperations.IsCandidateAlreadyPlaced(candidate))
                return FrameOperations.RemoveCandidateFromBatch(batch, candidate, message, okLabel);

            PasteTile origin = candidate.OriginTile;
            if (!context.IsSweepCheat
                && origin.PayloadCell.HasTile()
                && !TileExecutor.HasItemForTile(
                    (ushort)origin.PayloadCell.Block.Type,
                    origin.PayloadCell.Detail.Style,
                    in context))
            {
                return LayerResult.Fail(LayerFailReason.MissingItems, message);
            }

            LayerResult res = FrameOperations.UsesTilePlacement(candidate.Tiles, in origin)
                ? TileOperations.Place(candidate.Tiles, in context)
                : FrameOperations.Place(candidate.Tiles, in context);

            if (!res.Success)
            {
                if (res.FailReason == LayerFailReason.InvalidPlacement && context.RetryOnInvalidPlacement)
                    return LayerResult.OkWithRemaining(batch, message, okLabel);
                return res;
            }

            if (res.RemainingTiles.Count > 0)
                return FrameOperations.MergePlaceRemaining(batch, candidate, res);

            if (!context.IsSweepCheat && !Consume(in origin, in context))
                return LayerResult.Fail(LayerFailReason.MissingItems, message);

            return FrameOperations.RemoveCandidateFromBatch(
                batch,
                candidate,
                res.Message ?? message,
                res.OkLabel ?? okLabel);
        }
    }
}
