using System.Collections.Generic;
using System.Text;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.Debug;
using Terraria;
using Terraria.ID;
using Terraria.ObjectData;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    /// <summary>
    /// Dependency-based frames: anchored tiles, multitiles, and multi-style 1×1 placement objects.
    /// Ordinary auto-framed blocks stay in BlockLayer; classification does not use solidity.
    /// </summary>
    internal sealed class FrameLayer : Layer
    {
        public static readonly FrameLayer Definition = new();

        private const string LogCategory = "FrameLayer";

        private FrameLayer() : base("Frame", LayerType.Frame) { }

        public override bool Match(in PasteTile tile) => TileComparison.MatchTileType(in tile);

        public override LayerResult Identify(in PasteTile tile, in PasteTileLookup lookup, PasteSubject identifySubject)
        {
            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            if (!IsFrameTile(subjectTile))
                return LayerResult.IdentifyNone();

            string message = LayerLogFormat.FormatTile(subjectTile);
            IReadOnlyList<Blueprint.BlueprintCell>? tiles = FrameOperations.GetFrameTilesAsBounded(tile.Relative, subjectTile, lookup);
            int footprint = tiles is { Count: > 0 } ? tiles.Count : 1;
            TABLog.Debug(
                $"Identify subject={identifySubject} rel=({tile.Relative.X},{tile.Relative.Y}) "
                + $"world=({tile.World.X},{tile.World.Y}) type={subjectTile.TileType} "
                + $"anchors={FormatAnchorKinds(subjectTile)} footprint={footprint}",
                LogCategory);

            if (tiles is { Count: > 0 })
                return LayerResult.Identified(TileBatch.FromCells(tiles), message);

            return LayerResult.Identified(
                TileBatch.FromCells([new Blueprint.BlueprintCell(tile.Relative, tile.World, true)]),
                message);
        }

        /// <summary>Returns true for dependency-anchored, multi-cell, or multi-style tiles that need Frame placement.</summary>
        internal static bool IsFrameTile(Tile tile) =>
            FrameOperations.IsFrameClassification(tile);

        /// <summary>Type-only Frame check for discovery and <see cref="CustomLayer"/> exclusion lists.</summary>
        internal static bool IsFrameTileType(ushort tileType) =>
            FrameOperations.IsFrameClassification(tileType);

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

            bool valid = FrameOperations.CanPlaceFrameCandidate(candidate, in context);
            TABLog.Debug(
                $"PlacementValid origin=({candidate.OriginWorld.X},{candidate.OriginWorld.Y}) "
                + $"type={tile.PayloadCell.Block.Type} style={tile.PayloadCell.ResolvePlacementStyle()} "
                + $"anchors={FormatAnchorKinds(tile.PayloadCell.ToTile())} valid={valid}",
                LogCategory);
            return valid;
        }

        public override bool HasRequiredItems(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            if (!context.RequireItems || context.Items is null || context.IsSweepCheat) return true;
            if (!tile.PayloadCell.HasTile()) return true;
            int style = tile.PayloadCell.ResolvePlacementStyle();
            return TileInteractionChecks.HasTilePlacementItems(
                (ushort)tile.PayloadCell.Block.Type,
                style,
                tile.World.X,
                tile.World.Y,
                in context);
        }

        public override bool CanConsume(in PasteTile tile, in PasteContext context)
        {
            if (!tile.PayloadCell.HasTile() || !context.RequireItems || context.Items is null) return true;
            int style = tile.PayloadCell.ResolvePlacementStyle();
            return TileInteractionChecks.HasTilePlacementItems(
                (ushort)tile.PayloadCell.Block.Type,
                style,
                tile.World.X,
                tile.World.Y,
                in context);
        }

        public override bool Consume(in PasteTile tile, in PasteContext context)
        {
            if (!tile.PayloadCell.HasTile())
                return true;

            ushort tileType = tile.PayloadCell.Block.Type;
            int style = tile.PayloadCell.ResolvePlacementStyle();
            return UsesBaseTilePlacement(tileType, style) || TileID.Sets.Grass[tileType]
                ? TileOperations.Consume(in tile, in context)
                : FrameOperations.Consume(in tile, in context);
        }

        public override IReadOnlyList<int>? GetItem(in PasteTile tile)
        {
            if (!tile.PayloadCell.HasTile()) return null;
            int style = tile.PayloadCell.ResolvePlacementStyle();
            return TileInteractionChecks.GetToolItemIds((ushort)tile.PayloadCell.Block.Type, style, tile.World.X, tile.World.Y);
        }

        public override LayerResult Clear(TileBatch batch, in PasteContext context)
        {
            // --- Clear one frame object per pass in support-safe order and requeue the remaining candidates.
            if (batch.IsEmpty)
                return LayerResult.Ok();

            List<FrameOperations.FrameCandidate> candidates = FrameOperations.GroupIntoCandidates(batch.Tiles, forClear: true);
            TABLog.Debug($"Clear begin batchTiles={batch.Tiles.Count} candidates={candidates.Count}", LogCategory);

            int index = FrameOperations.SelectNextClearCandidateIndex(candidates, in context);
            FrameOperations.FrameCandidate candidate = index >= 0
                ? candidates[index]
                : candidates[0];
            string message = FrameOperations.FormatCandidateMessage(candidate, forClear: true);
            string okLabel = LayerLogFormat.ClearedLabel;

            if (index < 0)
            {
                TABLog.Debug(
                    $"Clear deferred allBlocked=true {DescribeClearBlockers(candidates)}",
                    LogCategory);
                return LayerResult.OkWithRemaining(batch, message, okLabel);
            }

            TABLog.Debug(
                $"Clear selected index={index}/{candidates.Count} {FormatCandidateSummary(candidate, forClear: true)}",
                LogCategory);

            PasteTile origin = FrameOperations.ResolveOriginPasteTile(candidate, forClear: true);
            if (!Main.tile[origin.World.X, origin.World.Y].HasTile)
            {
                TABLog.Debug($"Clear skip originAlreadyEmpty origin=({origin.World.X},{origin.World.Y})", LogCategory);
                return FrameOperations.RemoveCandidateFromBatch(batch, candidate, message, okLabel);
            }

            LayerResult result = FrameOperations.ClearOrigin(in origin, in context, message);
            if (!result.Success)
            {
                TABLog.Debug($"Clear failed origin=({origin.World.X},{origin.World.Y}) reason={result.FailReason}", LogCategory);
                return result;
            }

            TABLog.Debug(
                $"Clear queued origin=({origin.World.X},{origin.World.Y}) remainingCandidates={candidates.Count - 1}",
                LogCategory);
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
            TABLog.Debug(
                $"Place begin batchTiles={tiles.Count} localCandidates={candidates.Count} "
                + $"pendingCandidates={allPending.Count} retryInvalid={context.RetryOnInvalidPlacement}",
                LogCategory);

            int index = FrameOperations.SelectNextPlaceCandidateIndex(candidates, allPending, in context);
            FrameOperations.FrameCandidate candidate = index >= 0
                ? candidates[index]
                : candidates[0];
            string message = FrameOperations.FormatCandidateMessage(candidate, forClear: false);

            if (index < 0)
            {
                TABLog.Debug(
                    $"Place deferred allBlocked=true {DescribePlaceBlockers(candidates, allPending, in context)}",
                    LogCategory);
                if (!context.RetryOnInvalidPlacement)
                    return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
                return LayerResult.OkWithRemaining(batch, message, okLabel);
            }

            TABLog.Debug(
                $"Place selected index={index}/{candidates.Count} {FormatCandidateSummary(candidate, forClear: false)}",
                LogCategory);

            if (FrameOperations.IsCandidateAlreadyPlaced(candidate))
            {
                TABLog.Debug($"Place skip alreadyPlaced {FormatCandidateSummary(candidate, forClear: false)}", LogCategory);
                return FrameOperations.RemoveCandidateFromBatch(batch, candidate, message, okLabel);
            }

            PasteTile origin = candidate.OriginTile;
            if (!context.IsSweepCheat && origin.PayloadCell.HasTile())
            {
                ushort tileType = (ushort)origin.PayloadCell.Block.Type;
                int style = origin.PayloadCell.ResolvePlacementStyle();
                TileItemAvailability availability = TileExecutor.GetTileItemAvailability(tileType, style, in context);
                if (availability != TileItemAvailability.Available)
                {
                    LayerFailReason failReason = TileExecutor.ToFailReason(availability);
                    string issueLabel = availability == TileItemAvailability.UnresolvedInteraction
                        ? "unresolvedInteraction"
                        : "missingItems";

                    if (context.RetryOnMissingItems && failReason == LayerFailReason.MissingItems)
                    {
                        TABLog.Debug(
                            $"Place deferred {issueLabel} origin=({origin.World.X},{origin.World.Y}) "
                            + $"type={tileType} style={style}",
                            LogCategory);
                        return LayerResult.OkWithRemaining(batch, message, okLabel);
                    }

                    TABLog.Debug(
                        $"Place failed {issueLabel} origin=({origin.World.X},{origin.World.Y}) "
                        + $"type={tileType} style={style}",
                        LogCategory);
                    return LayerResult.Fail(failReason, message);
                }
            }

            bool useTilePlacement = FrameOperations.UsesTilePlacement(candidate.Tiles, in origin);
            TABLog.Debug(
                $"Place enqueue mode={(useTilePlacement ? "PlaceTile" : "PlaceObject")} "
                + $"origin=({origin.World.X},{origin.World.Y}) footprint={candidate.Tiles.Count}",
                LogCategory);

            LayerResult res = useTilePlacement
                ? TileOperations.Place(candidate.Tiles, in context)
                : FrameOperations.Place(candidate.Tiles, in context);

            if (!res.Success)
            {
                TABLog.Debug($"Place failed origin=({origin.World.X},{origin.World.Y}) reason={res.FailReason}", LogCategory);
                if (res.FailReason == LayerFailReason.InvalidPlacement && context.RetryOnInvalidPlacement)
                    return LayerResult.OkWithRemaining(batch, message, okLabel);
                return res;
            }

            if (res.RemainingTiles.Count > 0)
            {
                TABLog.Debug(
                    $"Place partial origin=({origin.World.X},{origin.World.Y}) "
                    + $"remainingFootprint={res.RemainingTiles.Count}",
                    LogCategory);
                return FrameOperations.MergePlaceRemaining(batch, candidate, res);
            }

            if (!context.IsSweepCheat && !Consume(in origin, in context))
            {
                TABLog.Debug($"Place failed consume origin=({origin.World.X},{origin.World.Y})", LogCategory);
                return LayerResult.Fail(LayerFailReason.MissingItems, message);
            }

            TABLog.Debug($"Place queued origin=({origin.World.X},{origin.World.Y}) remainingCandidates={candidates.Count - 1}", LogCategory);
            return FrameOperations.RemoveCandidateFromBatch(
                batch,
                candidate,
                res.Message ?? message,
                res.OkLabel ?? okLabel);
        }

        private static string FormatAnchorKinds(Tile tile)
        {
            // --- Describe why this tile matched Frame: anchor kinds, else multi-style/multitile.
            if (!tile.HasTile)
                return "none";

            var kinds = new List<string>(3);
            if (FrameOperations.HasTileAnchor(tile))
                kinds.Add("tile");
            if (FrameOperations.HasWallAnchor(tile))
                kinds.Add("wall");
            if (FrameOperations.HasOtherAnchor(tile))
                kinds.Add("other");

            if (kinds.Count > 0)
                return string.Join('+', kinds);

            int style = 0, alternate = 0;
            TileObjectData.GetTileInfo(tile, ref style, ref alternate);
            if (InteractionMapProvider.HasMultipleTilePlacementStyles(tile.TileType))
                return "multi-style";
            if (FrameOperations.IsMultiCellObject(tile.TileType, style))
                return "multitile";
            return "frame";
        }

        private static string FormatCandidateSummary(FrameOperations.FrameCandidate candidate, bool forClear)
        {
            PasteTile origin = candidate.OriginTile;
            if (forClear)
            {
                return $"origin=({candidate.OriginWorld.X},{candidate.OriginWorld.Y}) "
                    + $"footprint={candidate.Tiles.Count} targetType={origin.Target.TileType}";
            }

            return $"origin=({candidate.OriginWorld.X},{candidate.OriginWorld.Y}) "
                + $"footprint={candidate.Tiles.Count} type={origin.PayloadCell.Block.Type} "
                + $"style={origin.PayloadCell.ResolvePlacementStyle()} anchors={FormatAnchorKinds(origin.PayloadCell.ToTile())}";
        }

        private static string DescribePlaceBlockers(
            IReadOnlyList<FrameOperations.FrameCandidate> localCandidates,
            IReadOnlyList<FrameOperations.FrameCandidate> allPending,
            in PasteContext context)
        {
            var details = new StringBuilder();
            for (int i = 0; i < localCandidates.Count; i++)
            {
                FrameOperations.FrameCandidate candidate = localCandidates[i];
                string? blocker = DescribePlaceBlocker(candidate, allPending, in context);
                if (blocker is null)
                    continue;

                if (details.Length > 0)
                    details.Append("; ");

                details.Append($"[{i}] origin=({candidate.OriginWorld.X},{candidate.OriginWorld.Y}): {blocker}");
            }

            return details.Length > 0 ? details.ToString() : "no local candidates";
        }

        private static string? DescribePlaceBlocker(
            FrameOperations.FrameCandidate candidate,
            IReadOnlyList<FrameOperations.FrameCandidate> pending,
            in PasteContext context)
        {
            for (int i = 0; i < pending.Count; i++)
            {
                FrameOperations.FrameCandidate other = pending[i];
                if (other.OriginWorld == candidate.OriginWorld)
                    continue;

                if (FrameOperations.IsSupportedByPlacement(candidate, other)
                    && !FrameOperations.IsCandidatePresentInWorld(other, in context))
                {
                    return $"waitingOnPending origin=({other.OriginWorld.X},{other.OriginWorld.Y}) "
                        + $"footprint={other.Tiles.Count} presentInWorld=false";
                }
            }

            if (!FrameOperations.CanPlaceFrameCandidate(candidate, in context, pending))
                return "CanPlaceFrameCandidate=false";

            if (context.RetryOnMissingItems
                && context.RequireItems
                && !context.IsSweepCheat
                && !FrameOperations.HasRequiredItemsForFrameCandidate(candidate, in context))
            {
                PasteTile origin = candidate.OriginTile;
                return $"missingItems type={origin.PayloadCell.Block.Type} style={origin.PayloadCell.ResolvePlacementStyle()}";
            }

            return null;
        }

        private static string DescribeClearBlockers(IReadOnlyList<FrameOperations.FrameCandidate> candidates)
        {
            var details = new StringBuilder();
            for (int i = 0; i < candidates.Count; i++)
            {
                FrameOperations.FrameCandidate candidate = candidates[i];
                string? blocker = DescribeClearBlocker(candidate, candidates);
                if (blocker is null)
                    continue;

                if (details.Length > 0)
                    details.Append("; ");

                details.Append($"[{i}] origin=({candidate.OriginWorld.X},{candidate.OriginWorld.Y}): {blocker}");
            }

            return details.Length > 0 ? details.ToString() : "no local candidates";
        }

        private static string? DescribeClearBlocker(
            FrameOperations.FrameCandidate candidate,
            IReadOnlyList<FrameOperations.FrameCandidate> pending)
        {
            for (int i = 0; i < pending.Count; i++)
            {
                FrameOperations.FrameCandidate other = pending[i];
                if (other.OriginWorld == candidate.OriginWorld)
                    continue;

                if (FrameOperations.IsSupportedByClear(other, candidate))
                {
                    return $"supportingPending origin=({other.OriginWorld.X},{other.OriginWorld.Y}) "
                        + $"footprint={other.Tiles.Count}";
                }
            }

            return null;
        }
    }
}
