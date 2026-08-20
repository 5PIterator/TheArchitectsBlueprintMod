using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.Layers;

namespace TheArchitectsBlueprintMod.Debug;

/// <summary>
/// Emits concise info diagnostics for paste phase execution.
/// </summary>
internal static class PastingInfoLog
{
    /// <summary>
    /// Emits one transport-friendly progress payload to the chat/info sink.
    /// </summary>
    internal static void Write(in PasteProgress progress)
    {
        switch (progress.Kind)
        {
            case PasteProgressKind.PhaseStart:
            {
                string suffix = progress.PhaseTotal > 0 ? $" (tasks={progress.PhaseTotal})" : string.Empty;
                TABLog.Info(
                    $"--- [{progress.Phase}] START{suffix} ---",
                    TABLog.GenericCategory,
                    chatColor: Color.LightSkyBlue,
                    ownerPlayer: progress.OwnerPlayer);
                return;
            }
            case PasteProgressKind.PhaseEnd:
            {
                Color color = progress.FailedCount > 0 || progress.PhaseRemaining > 0
                    ? Color.LightYellow
                    : Color.LightGreen;
                TABLog.Info(
                    FormatPhaseEnd(in progress),
                    TABLog.GenericCategory,
                    chatColor: color,
                    ownerPlayer: progress.OwnerPlayer);
                return;
            }
            case PasteProgressKind.TaskResult:
            {
                string contentName = progress.ContentMessage ?? LayerLogFormat.FormatContentDisplayName(null);
                string result = ResolveResult(in progress);
                Color color = ResolveResultColor(in progress);
                string layer = string.IsNullOrEmpty(progress.LayerId) ? "-" : progress.LayerId;
                string message =
                    $"[{progress.Phase}] [{layer}] [{progress.BatchIndex}/{progress.BatchTotal}] [{progress.PhaseIndex}/{progress.PhaseTotal}] [{progress.WorldX}, {progress.WorldY}] {contentName} - {result}";
                TABLog.Info(message, TABLog.GenericCategory, chatColor: color, ownerPlayer: progress.OwnerPlayer);
                return;
            }
            default:
                return;
        }
    }

    /// <summary>
    /// Formats a Plan END banner with queued place and clear batch counts.
    /// </summary>
    internal static string FormatPlanEnd(int placeBatches, int clearBatches)
    {
        // --- Plan only reports queue depth; Place owns missing-item and invalid-placement failures.
        return $"--- [Plan] END (placeBatches={placeBatches}, clearBatches={clearBatches}) ---";
    }

    /// <summary>
    /// Returns whether sweep applied the payload intent at the destination world tile.
    /// </summary>
    internal static bool IsSweepSatisfied(in PasteTile tile)
    {
        // --- Require structural match, then paint/coating facets when the payload carries them.

        int x = tile.World.X, y = tile.World.Y;
        if (!WorldGen.InWorld(x, y))
            return false;

        Tile world = Main.tile[x, y];
        if (tile.PayloadCell.IsEmpty)
            return !world.HasTile && world.WallType == WallID.None;

        if (tile.PayloadCell.HasTile())
        {
            if (!world.HasTile || !SweepTileTypeMatches(tile.PayloadCell.Block.Type, world.TileType))
                return false;
            if (TreeLayer.IsTreeType((ushort)tile.PayloadCell.Block.Type)
                && !TreeFramesMatch(tile.PayloadCell, world))
                return false;
            return PaintLayer.PaintStateMatches(tile.PayloadCell.Paint, world);
        }

        if (tile.PayloadCell.Wall.Type != WallID.None)
        {
            if (!SweepWallTypeMatches(tile.PayloadCell.Wall.Type, world.WallType))
                return false;
            return PaintLayer.PaintStateMatches(tile.PayloadCell.Paint, world);
        }

        if (tile.PayloadCell.Liquid.Amount > 0)
            return world.LiquidAmount == tile.PayloadCell.Liquid.Amount && world.LiquidType == tile.PayloadCell.Liquid.Type;

        if (HasPaintOrCoating(tile.PayloadCell.Paint))
            return PaintLayer.PaintStateMatches(tile.PayloadCell.Paint, world);

        return true;
    }

    /// <summary>
    /// Describes why <see cref="IsSweepSatisfied"/> failed for a sweep tile.
    /// </summary>
    internal static string ExplainSweepFailure(in PasteTile tile)
    {
        int x = tile.World.X, y = tile.World.Y;
        if (!WorldGen.InWorld(x, y))
            return "target tile outside world";

        Tile world = Main.tile[x, y];
        if (tile.PayloadCell.IsEmpty)
        {
            if (world.HasTile)
                return "tile not cleared";
            if (world.WallType != WallID.None)
                return "wall not cleared";
            return "vacant intent not applied";
        }

        if (tile.PayloadCell.HasTile() && (!world.HasTile || !SweepTileTypeMatches(tile.PayloadCell.Block.Type, world.TileType)))
            return "tile not placed";

        if (tile.PayloadCell.HasTile()
            && world.HasTile
            && SweepTileTypeMatches(tile.PayloadCell.Block.Type, world.TileType)
            && TreeLayer.IsTreeType((ushort)tile.PayloadCell.Block.Type)
            && !TreeFramesMatch(tile.PayloadCell, world))
            return "tree frame mismatch";

        if (tile.PayloadCell.Wall.Type != WallID.None && !SweepWallTypeMatches(tile.PayloadCell.Wall.Type, world.WallType))
            return "wall not placed";

        if (tile.PayloadCell.Liquid.Amount > 0)
        {
            if (world.LiquidAmount != tile.PayloadCell.Liquid.Amount)
                return "liquid amount mismatch";
            if (world.LiquidType != tile.PayloadCell.Liquid.Type)
                return "liquid type mismatch";
        }

        if (HasPaintOrCoating(tile.PayloadCell.Paint)
            && !PaintLayer.PaintStateMatches(tile.PayloadCell.Paint, world))
            return "paint or coating mismatch";

        return "intent not applied";
    }

    private static bool TreeFramesMatch(in PayloadCell payload, in Tile world) =>
        world.TileFrameX == payload.Block.FrameX && world.TileFrameY == payload.Block.FrameY;

    private static bool HasPaintOrCoating(in PayloadCell.PaintData paint) =>
        paint.TileColor != PaintID.None
        || paint.WallColor != PaintID.None
        || paint.TileInvisible
        || paint.WallInvisible
        || paint.TileFullbright
        || paint.WallFullbright;

    /// <summary>
    /// Explains a fail reason for Generic paste messages.
    /// </summary>
    internal static string ExplainFailReason(LayerFailReason reason, string? detail)
    {
        // --- Prefer explicit detail so combined planning reasons stay visible.

        if (!string.IsNullOrWhiteSpace(detail))
            return detail;

        if (reason != LayerFailReason.None)
            return ExplainFailReason(reason);

        return "unknown issue";
    }

    private static string FormatPhaseEnd(in PasteProgress progress)
    {
        string baseText =
            $"--- [{progress.Phase}] END (processed={progress.PhaseProcessed}, remaining={progress.PhaseRemaining}, ok={progress.SuccessCount}, errors={progress.FailedCount}";
        if (progress.FailedCount <= 0 || string.IsNullOrWhiteSpace(progress.FailureSummary))
            return $"{baseText}) ---";

        return $"{baseText}: {progress.FailureSummary}) ---";
    }

    private static string ResolveResult(in PasteProgress progress)
    {
        return progress.Outcome switch
        {
            PasteProgressOutcome.Ok => progress.OkLabel ?? progress.ResolveGenericOkLabel(),
            PasteProgressOutcome.InProgress => FormatInProgressResult(in progress),
            PasteProgressOutcome.Retry => FormatRetryResult(in progress),
            PasteProgressOutcome.Failed =>
                $"Error: {ExplainFailReason(progress.FailReason, progress.ResultDetail)}",
            _ => "Error: unknown issue",
        };
    }

    private static string FormatInProgressResult(in PasteProgress progress)
    {
        if (PasteProgress.HasBatchProgressSuffix(progress.ContentMessage))
            return "Progress";

        if (!string.IsNullOrWhiteSpace(progress.OkLabel))
            return progress.OkLabel;

        return "Progress";
    }

    private static string FormatRetryResult(in PasteProgress progress)
    {
        if (progress.FailReason == LayerFailReason.None && string.IsNullOrWhiteSpace(progress.ResultDetail))
            return "Retry";

        return $"Retry: {ExplainFailReason(progress.FailReason, progress.ResultDetail)}";
    }

    private static Color ResolveResultColor(in PasteProgress progress)
    {
        if (progress.IsTerminalOutcome())
            return Color.Red;

        return progress.Outcome switch
        {
            PasteProgressOutcome.Ok => Color.LightGreen,
            PasteProgressOutcome.InProgress => Color.MediumPurple,
            PasteProgressOutcome.Retry => Color.Khaki,
            PasteProgressOutcome.Failed => Color.Red,
            _ => Color.Red,
        };
    }

    private static string ExplainFailReason(LayerFailReason reason) =>
        reason switch
        {
            LayerFailReason.None => "unknown issue",
            LayerFailReason.MissingItems => "missing items",
            LayerFailReason.UnresolvedInteraction => "unresolved interaction",
            LayerFailReason.InvalidPlacement => "invalid placement",
            LayerFailReason.InsufficientPickaxePower => "insufficient pickaxe power",
            LayerFailReason.ClearBlocked => "clear blocked",
            LayerFailReason.Unknown => "unknown block",
            _ => "unknown issue",
        };

    /// <summary>True when the world tile matches the payload type or its Unloaded remap.</summary>
    private static bool SweepTileTypeMatches(ushort payloadType, ushort worldType)
    {
        // --- Exact match first; otherwise accept sweep remapping of unknown ids.
        if (worldType == payloadType)
            return true;
        return TileLoaderSupport.IsUnknownTileType(payloadType)
            && TileLoaderSupport.ResolveUnloadableTileType(payloadType) == worldType;
    }

    /// <summary>True when the world wall matches the payload type or its Unloaded remap.</summary>
    private static bool SweepWallTypeMatches(ushort payloadType, ushort worldType)
    {
        // --- Exact match first; otherwise accept sweep remapping of unknown walls.
        if (worldType == payloadType)
            return true;
        return TileLoaderSupport.IsUnknownWallType(payloadType)
            && TileLoaderSupport.ResolveUnloadableWallType(payloadType) == worldType;
    }
}
