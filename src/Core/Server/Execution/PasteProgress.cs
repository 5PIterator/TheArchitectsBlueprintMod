using System.Text;
using Terraria;
using Terraria.ID;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.Layers;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Debug;

/// <summary>
/// Progress event kind used by sinks (chat now, network/overlay later).
/// </summary>
internal enum PasteProgressKind
{
    PhaseStart,
    TaskResult,
    PhaseEnd,
}

/// <summary>
/// Compact content descriptor for transport-friendly progress payloads.
/// </summary>
internal enum PasteProgressContentKind
{
    Unknown,
    Air,
    Tile,
    Wall,
    Liquid,
    Wiring,
    Detail,
    Paint,
}

/// <summary>
/// Task-level result semantics for sinks (OK, deferred retry, policy skip, terminal failure).
/// </summary>
internal enum PasteProgressOutcome
{
    Ok,
    Retry,
    Skipped,
    Failed,
}

/// <summary>
/// High-level paste phases used by <see cref="PasteProgress"/> events and info diagnostics.
/// </summary>
internal enum PasteInfoPhase
{
    Plan,
    Clear,
    Cut,
    Place,
    Sweep,
}

/// <summary>
/// Central progress payload for chat sink, future net packets, and overlay feeds.
/// </summary>
internal readonly struct PasteProgress
{
    public int CorrelationId { get; init; }
    public int OwnerPlayer { get; init; }
    public int OverlayId { get; init; }
    public PasteProgressKind Kind { get; init; }
    public PasteInfoPhase Phase { get; init; }
    public string? LayerId { get; init; }
    public PlacePhaseStrategy PlaceStrategy { get; init; }
    public SweepPhaseStrategy SweepStrategy { get; init; }
    public int BatchIndex { get; init; }
    public int BatchTotal { get; init; }
    public int PhaseIndex { get; init; }
    public int PhaseTotal { get; init; }
    public int PhaseProcessed { get; init; }
    public int PhaseRemaining { get; init; }
    public int WorldX { get; init; }
    public int WorldY { get; init; }
    public PasteProgressContentKind ContentKind { get; init; }
    public ushort ContentType { get; init; }
    /// <summary>Layer-specific display text (e.g. wire colors for <see cref="PasteProgressContentKind.Wiring"/>).</summary>
    public string? ContentDetail { get; init; }
    public PasteProgressOutcome Outcome { get; init; }
    public bool Success { get; init; }
    public LayerFailReason FailReason { get; init; }
    public string? ResultDetail { get; init; }

    public PasteProgress WithTaskResult(
        in PasteTile tile,
        PasteProgressOutcome outcome,
        LayerFailReason failReason = LayerFailReason.None,
        string? detail = null,
        string? layerId = null,
        PasteProgressContentKind? contentKind = null,
        ushort? contentType = null) =>
        new()
        {
            CorrelationId = CorrelationId,
            OwnerPlayer = OwnerPlayer,
            OverlayId = OverlayId,
            Kind = PasteProgressKind.TaskResult,
            Phase = Phase,
            LayerId = layerId ?? LayerId,
            PlaceStrategy = PlaceStrategy,
            SweepStrategy = SweepStrategy,
            BatchIndex = BatchIndex,
            BatchTotal = BatchTotal,
            PhaseIndex = PhaseIndex,
            PhaseTotal = PhaseTotal,
            PhaseProcessed = PhaseProcessed,
            PhaseRemaining = PhaseRemaining,
            WorldX = tile.World.X,
            WorldY = tile.World.Y,
            ContentKind = ResolveTaskContentKind(Phase, in tile, layerId ?? LayerId, contentKind),
            ContentType = contentType ?? ResolveTaskContentType(Phase, in tile, layerId ?? LayerId, contentKind),
            ContentDetail = ResolveTaskContentDetail(Phase, in tile, layerId ?? LayerId, contentKind),
            Outcome = outcome,
            Success = outcome is PasteProgressOutcome.Ok or PasteProgressOutcome.Skipped,
            FailReason = failReason,
            ResultDetail = detail,
        };

    /// <summary>
    /// Resolves cleared content from <see cref="PasteTile.Target"/> before live world mutation.
    /// </summary>
    internal static PasteProgressContentKind ResolveTargetContentKind(in PasteTile tile)
    {
        if (tile.Target.HasTile)
            return PasteProgressContentKind.Tile;
        if (tile.Target.WallType != WallID.None)
            return PasteProgressContentKind.Wall;
        if (tile.Target.LiquidAmount > 0)
            return PasteProgressContentKind.Liquid;
        return PasteProgressContentKind.Unknown;
    }

    /// <summary>
    /// Resolves cleared content type from <see cref="PasteTile.Target"/> before live world mutation.
    /// </summary>
    internal static ushort ResolveTargetContentType(in PasteTile tile)
    {
        if (tile.Target.HasTile)
            return tile.Target.TileType;
        if (tile.Target.WallType != WallID.None)
            return tile.Target.WallType;
        if (tile.Target.LiquidAmount > 0)
            return (ushort)tile.Target.LiquidType;
        return 0;
    }

    /// <summary>
    /// Classifies a place attempt using paste policy and whether the task will leave the queue.
    /// </summary>
    internal static PasteProgressOutcome ClassifyPlaceAttempt(
        in LayerResult res,
        in PasteContext context,
        bool taskCompletes)
    {
        if (res.Success)
            return taskCompletes ? PasteProgressOutcome.Ok : PasteProgressOutcome.Retry;

        if (!context.RetryOnMissingItems && res.FailReason == LayerFailReason.MissingItems)
            return PasteProgressOutcome.Skipped;

        if (!context.RetryOnInvalidPlacement && res.FailReason == LayerFailReason.InvalidPlacement)
            return PasteProgressOutcome.Skipped;

        if (!taskCompletes)
            return PasteProgressOutcome.Retry;

        return PasteProgressOutcome.Failed;
    }

    /// <summary>
    /// Classifies a clear attempt using whether the task will leave the queue.
    /// </summary>
    internal static PasteProgressOutcome ClassifyClearAttempt(in LayerResult res, bool taskCompletes)
    {
        if (res.Success)
            return taskCompletes ? PasteProgressOutcome.Ok : PasteProgressOutcome.Retry;

        if (res.FailReason == LayerFailReason.InsufficientPickaxePower)
            return PasteProgressOutcome.Skipped;

        if (!taskCompletes)
            return PasteProgressOutcome.Retry;

        return PasteProgressOutcome.Failed;
    }

    /// <summary>
    /// Resolves retry detail for a partial batch pass, e.g. <c>Batch (1/2)</c>.
    /// </summary>
    internal static string? ResolveBatchRetryDetail(TileBatch batch, in LayerResult res, PasteProgressOutcome outcome)
    {
        if (outcome != PasteProgressOutcome.Retry || !res.Success || res.RemainingBatch is null || res.RemainingBatch.Count == 0)
            return null;

        return batch.FormatBatchRetryDetail(batch.Count, res.RemainingBatch.Count);
    }

    internal static bool IsBatchRetryDetail(string? detail) =>
        detail is not null && detail.StartsWith("Batch (");

    /// <summary>
    /// True when the task leaves the queue without applying place/sweep intent.
    /// </summary>
    internal bool IsTerminalOutcome() =>
        Outcome switch
        {
            PasteProgressOutcome.Failed => true,
            PasteProgressOutcome.Skipped when Phase is PasteInfoPhase.Place or PasteInfoPhase.Sweep => true,
            _ => false,
        };

    /// <summary>
    /// Returns the success label for this progress event based on phase and resolved paste policy.
    /// </summary>
    internal string ResolveOkLabel() =>
        ContentKind switch
        {
            PasteProgressContentKind.Wiring => Phase switch
            {
                PasteInfoPhase.Clear or PasteInfoPhase.Cut => "Cleared",
                PasteInfoPhase.Place => "Wired",
                _ => "OK",
            },
            PasteProgressContentKind.Detail => Phase switch
            {
                PasteInfoPhase.Clear or PasteInfoPhase.Cut => "Cleared",
                PasteInfoPhase.Place => "Detailed",
                _ => "OK",
            },
            PasteProgressContentKind.Paint => Phase switch
            {
                PasteInfoPhase.Clear or PasteInfoPhase.Cut => "Cleared",
                PasteInfoPhase.Place => "Painted",
                _ => "OK",
            },
            _ => Phase switch
            {
                PasteInfoPhase.Plan => "Planned",
                PasteInfoPhase.Clear => "Cleared",
                PasteInfoPhase.Cut => "Cleared",
                PasteInfoPhase.Place => PlaceStrategy == PlacePhaseStrategy.Replace ? "Replaced" : "Placed",
                PasteInfoPhase.Sweep => SweepStrategy == SweepPhaseStrategy.Cheat ? "Cheesed" : "Sweeped",
                _ => "OK",
            },
        };

    /// <summary>Formats wire colors and actuator state for progress and debug logs.</summary>
    internal static string FormatWireState(in PayloadCell.WireData wire)
    {
        if (!wire.RedWire && !wire.BlueWire && !wire.GreenWire && !wire.YellowWire && !wire.HasActuator)
            return "Wire:none";

        var parts = new StringBuilder(24);
        if (wire.RedWire) parts.Append(parts.Length > 0 ? "+R" : "R");
        if (wire.BlueWire) parts.Append(parts.Length > 0 ? "+B" : "B");
        if (wire.GreenWire) parts.Append(parts.Length > 0 ? "+G" : "G");
        if (wire.YellowWire) parts.Append(parts.Length > 0 ? "+Y" : "Y");
        if (wire.HasActuator) parts.Append(parts.Length > 0 ? "+Actuator" : "Actuator");
        return $"Wire:{parts}";
    }

    /// <summary>Formats wire colors and actuator state on a <see cref="Tile"/> snapshot.</summary>
    internal static string FormatWireState(in Tile tile)
    {
        if (!tile.RedWire && !tile.BlueWire && !tile.GreenWire && !tile.YellowWire && !tile.HasActuator)
            return "Wire:none";

        var parts = new StringBuilder(24);
        if (tile.RedWire) parts.Append(parts.Length > 0 ? "+R" : "R");
        if (tile.BlueWire) parts.Append(parts.Length > 0 ? "+B" : "B");
        if (tile.GreenWire) parts.Append(parts.Length > 0 ? "+G" : "G");
        if (tile.YellowWire) parts.Append(parts.Length > 0 ? "+Y" : "Y");
        if (tile.HasActuator) parts.Append(parts.Length > 0 ? "+Actuator" : "Actuator");
        return $"Wire:{parts}";
    }

    /// <summary>Formats live world wire state at <paramref name="x"/>, <paramref name="y"/>.</summary>
    internal static string FormatWireStateAt(int x, int y)
    {
        if (!WorldGen.InWorld(x, y))
            return "Wire:?";

        return FormatWireState(Main.tile[x, y]);
    }

    /// <summary>Formats slope, half-block, and actuation detail for progress and debug logs.</summary>
    internal static string FormatDetailState(in PayloadCell.DetailData detail)
    {
        var parts = new StringBuilder(32);
        AppendDetailParts(parts, detail.BlockType, detail.Slope, detail.IsHalfBlock, detail.IsActuated);
        return parts.Length == 0 ? "Detail:none" : $"Detail:{parts}";
    }

    /// <summary>Formats slope, half-block, and actuation detail on a <see cref="Tile"/> snapshot.</summary>
    internal static string FormatDetailState(in Tile tile)
    {
        var parts = new StringBuilder(32);
        AppendDetailParts(parts, tile.BlockType, tile.Slope, tile.IsHalfBlock, tile.IsActuated);
        return parts.Length == 0 ? "Detail:none" : $"Detail:{parts}";
    }

    /// <summary>Formats live world detail state at <paramref name="x"/>, <paramref name="y"/>.</summary>
    internal static string FormatDetailStateAt(int x, int y)
    {
        if (!WorldGen.InWorld(x, y))
            return "Detail:?";

        return FormatDetailState(Main.tile[x, y]);
    }

    /// <summary>Formats tile and wall paint for progress and debug logs.</summary>
    internal static string FormatPaintState(in PayloadCell cell)
    {
        var parts = new StringBuilder(64);
        AppendPaintSurface(parts, in cell);
        AppendPaintColors(parts, cell.Paint.TileColor, cell.Paint.WallColor);
        return parts.Length == 0 ? "Paint:none" : $"Paint:{parts}";
    }

    /// <summary>Formats tile and wall paint on a <see cref="Tile"/> snapshot.</summary>
    internal static string FormatPaintState(in Tile tile)
    {
        var parts = new StringBuilder(64);
        AppendPaintSurface(parts, in tile);
        AppendPaintColors(parts, tile.TileColor, tile.WallColor);
        return parts.Length == 0 ? "Paint:none" : $"Paint:{parts}";
    }

    /// <summary>Formats live world paint state at <paramref name="x"/>, <paramref name="y"/>.</summary>
    internal static string FormatPaintStateAt(int x, int y)
    {
        if (!WorldGen.InWorld(x, y))
            return "Paint:?";

        return FormatPaintState(Main.tile[x, y]);
    }

    private static void AppendDetailParts(
        StringBuilder parts,
        BlockType blockType,
        SlopeType slope,
        bool isHalfBlock,
        bool isActuated)
    {
        if (blockType != BlockType.Solid)
            AppendDetailLabel(parts, blockType.ToString());
        else if (slope != SlopeType.Solid)
            AppendDetailLabel(parts, slope.ToString());

        if (isHalfBlock && blockType != BlockType.HalfBlock)
            AppendDetailLabel(parts, "HalfBlock");

        if (isActuated)
            AppendDetailLabel(parts, "Actuated");
    }

    private static void AppendDetailLabel(StringBuilder parts, string label)
    {
        if (parts.Length > 0)
            parts.Append('+');
        parts.Append(label);
    }

    private static void AppendPaintSurface(StringBuilder parts, in PayloadCell cell)
    {
        if (cell.HasTile())
            AppendDetailLabel(parts, NormalizeSurfaceName(ContentNameResolver.GetTileName(cell.Block.Type)));
        else if (cell.Wall.Type != WallID.None)
            AppendDetailLabel(parts, NormalizeSurfaceName(ContentNameResolver.GetWallName(cell.Wall.Type)));
    }

    private static void AppendPaintSurface(StringBuilder parts, in Tile tile)
    {
        if (tile.HasTile)
            AppendDetailLabel(parts, NormalizeSurfaceName(ContentNameResolver.GetTileName(tile.TileType)));
        else if (tile.WallType != WallID.None)
            AppendDetailLabel(parts, NormalizeSurfaceName(ContentNameResolver.GetWallName(tile.WallType)));
    }

    private static void AppendPaintColors(StringBuilder parts, byte tileColor, byte wallColor)
    {
        if (tileColor != PaintID.None)
            AppendDetailLabel(parts, $"Tile={ContentNameResolver.GetPaintName(tileColor)}");
        if (wallColor != PaintID.None)
            AppendDetailLabel(parts, $"Wall={ContentNameResolver.GetPaintName(wallColor)}");
    }

    private static string NormalizeSurfaceName(string rawName) =>
        ContentNameResolver.FormatContentDisplayName(rawName);

    private static bool IsWallLayer(string? layerId) =>
        layerId == LayerSystem.Wall.Id;

    private static bool IsWiringLayer(string? layerId) =>
        layerId == LayerSystem.Wiring.Id;

    private static bool IsDetailLayer(string? layerId) =>
        layerId == LayerSystem.Detail.Id;

    private static bool IsPaintLayer(string? layerId) =>
        layerId == LayerSystem.Paint.Id;

    private static PasteProgressContentKind ResolveTaskContentKind(
        PasteInfoPhase phase,
        in PasteTile tile,
        string? layerId,
        PasteProgressContentKind? contentKindOverride)
    {
        if (IsWallLayer(layerId))
            return PasteProgressContentKind.Wall;

        if (IsWiringLayer(layerId))
            return PasteProgressContentKind.Wiring;

        if (IsDetailLayer(layerId))
            return PasteProgressContentKind.Detail;

        if (IsPaintLayer(layerId))
            return PasteProgressContentKind.Paint;

        if (contentKindOverride is PasteProgressContentKind kind)
            return kind;

        return ResolveContentKind(phase, in tile);
    }

    private static string? ResolveTaskContentDetail(
        PasteInfoPhase phase,
        in PasteTile tile,
        string? layerId,
        PasteProgressContentKind? contentKindOverride)
    {
        if (IsWiringLayer(layerId) || contentKindOverride is PasteProgressContentKind.Wiring)
            return phase is PasteInfoPhase.Place or PasteInfoPhase.Sweep
                ? FormatWireState(tile.PayloadCell.Wire)
                : FormatWireState(tile.Target);

        if (IsDetailLayer(layerId) || contentKindOverride is PasteProgressContentKind.Detail)
            return phase is PasteInfoPhase.Place or PasteInfoPhase.Sweep
                ? FormatDetailState(tile.PayloadCell.Detail)
                : FormatDetailState(tile.Target);

        if (IsPaintLayer(layerId) || contentKindOverride is PasteProgressContentKind.Paint)
            return phase is PasteInfoPhase.Place or PasteInfoPhase.Sweep
                ? FormatPaintState(tile.PayloadCell)
                : FormatPaintState(tile.Target);

        return null;
    }

    private static ushort ResolveTaskContentType(
        PasteInfoPhase phase,
        in PasteTile tile,
        string? layerId,
        PasteProgressContentKind? contentKindOverride)
    {
        if (IsWallLayer(layerId) || contentKindOverride is PasteProgressContentKind.Wall)
        {
            if (phase is PasteInfoPhase.Place or PasteInfoPhase.Sweep)
            {
                if (tile.PayloadCell.Wall.Type != WallID.None)
                    return tile.PayloadCell.Wall.Type;
            }
            else if (tile.Target.WallType != WallID.None)
            {
                return tile.Target.WallType;
            }

            return 0;
        }

        return ResolveContentType(phase, in tile);
    }

    private static PasteProgressContentKind ResolveContentKind(PasteInfoPhase phase, in PasteTile tile)
    {
        if (phase is PasteInfoPhase.Place or PasteInfoPhase.Sweep)
        {
            if (tile.PayloadCell.IsEmpty)
                return PasteProgressContentKind.Air;
            if (tile.PayloadCell.HasTile())
                return PasteProgressContentKind.Tile;
            if (tile.PayloadCell.Wall.Type != WallID.None)
                return PasteProgressContentKind.Wall;
            if (tile.PayloadCell.Liquid.Amount > 0)
                return PasteProgressContentKind.Liquid;
        }

        if (tile.Target.HasTile)
            return PasteProgressContentKind.Tile;
        if (tile.Target.WallType != WallID.None)
            return PasteProgressContentKind.Wall;
        if (tile.Target.LiquidAmount > 0)
            return PasteProgressContentKind.Liquid;
        return PasteProgressContentKind.Unknown;
    }

    private static ushort ResolveContentType(PasteInfoPhase phase, in PasteTile tile)
    {
        if (phase is PasteInfoPhase.Place or PasteInfoPhase.Sweep)
        {
            if (tile.PayloadCell.HasTile())
                return tile.PayloadCell.Block.Type;
            if (tile.PayloadCell.Wall.Type != WallID.None)
                return tile.PayloadCell.Wall.Type;
            if (tile.PayloadCell.Liquid.Amount > 0)
                return (ushort)tile.PayloadCell.Liquid.Type;
            return 0;
        }

        if (tile.Target.HasTile)
            return tile.Target.TileType;
        if (tile.Target.WallType != WallID.None)
            return tile.Target.WallType;
        if (tile.Target.LiquidAmount > 0)
            return (ushort)tile.Target.LiquidType;
        return 0;
    }
}

/// <summary>
/// Tracks cumulative phase progress and builds per-task progress snapshots.
/// </summary>
internal sealed class PasteProgressTracker
{
    private readonly int _ownerPlayer;
    public int PhaseTotal { get; private set; }
    public int PhaseProcessed { get; private set; }
    public PasteInfoPhase Phase { get; private set; }
    public PlacePhaseStrategy PlaceStrategy { get; set; } = PlacePhaseStrategy.Place;
    public SweepPhaseStrategy SweepStrategy { get; set; } = SweepPhaseStrategy.Detail;
    public int CorrelationId { get; set; }
    public int OverlayId { get; set; }

    public PasteProgressTracker(int ownerPlayer)
    {
        _ownerPlayer = ownerPlayer;
    }

    public PasteProgress BeginPhase(PasteInfoPhase phase, int phaseTotal)
    {
        Phase = phase;
        PhaseTotal = phaseTotal;
        PhaseProcessed = 0;

        return new PasteProgress
        {
            CorrelationId = CorrelationId,
            OwnerPlayer = _ownerPlayer,
            OverlayId = OverlayId,
            Kind = PasteProgressKind.PhaseStart,
            Phase = phase,
            PlaceStrategy = PlaceStrategy,
            SweepStrategy = SweepStrategy,
            PhaseTotal = phaseTotal,
        };
    }

    /// <summary>
    /// Builds a task snapshot for the current tick batch without advancing completed-task count.
    /// </summary>
    public PasteProgress ForBatch(int batchIndex, int batchTotal) => new()
    {
        CorrelationId = CorrelationId,
        OwnerPlayer = _ownerPlayer,
        OverlayId = OverlayId,
        Kind = PasteProgressKind.TaskResult,
        Phase = Phase,
        PlaceStrategy = PlaceStrategy,
        SweepStrategy = SweepStrategy,
        BatchIndex = batchIndex,
        BatchTotal = batchTotal,
        PhaseIndex = PhaseProcessed + 1,
        PhaseTotal = PhaseTotal,
        PhaseProcessed = PhaseProcessed,
    };

    /// <summary>
    /// Records one task leaving the queue (success, policy skip, or terminal outcome).
    /// </summary>
    public void RecordCompletion()
    {
        if (PhaseProcessed < PhaseTotal)
            PhaseProcessed++;
    }

    public PasteProgress EndPhase(int remaining) => new()
    {
        CorrelationId = CorrelationId,
        OwnerPlayer = _ownerPlayer,
        OverlayId = OverlayId,
        Kind = PasteProgressKind.PhaseEnd,
        Phase = Phase,
        PlaceStrategy = PlaceStrategy,
        SweepStrategy = SweepStrategy,
        PhaseTotal = PhaseTotal,
        PhaseProcessed = PhaseProcessed,
        PhaseRemaining = remaining,
    };
}
