using Terraria.ID;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.Layers;

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
    public PasteProgressOutcome Outcome { get; init; }
    public bool Success { get; init; }
    public LayerFailReason FailReason { get; init; }
    public string? ResultDetail { get; init; }

    public PasteProgress WithTaskResult(
        in PasteTile tile,
        PasteProgressOutcome outcome,
        LayerFailReason failReason = LayerFailReason.None,
        string? detail = null) =>
        new()
        {
            CorrelationId = CorrelationId,
            OwnerPlayer = OwnerPlayer,
            OverlayId = OverlayId,
            Kind = PasteProgressKind.TaskResult,
            Phase = Phase,
            BatchIndex = BatchIndex,
            BatchTotal = BatchTotal,
            PhaseIndex = PhaseIndex,
            PhaseTotal = PhaseTotal,
            PhaseProcessed = PhaseProcessed,
            PhaseRemaining = PhaseRemaining,
            WorldX = tile.World.X,
            WorldY = tile.World.Y,
            ContentKind = ResolveContentKind(Phase, in tile),
            ContentType = ResolveContentType(Phase, in tile),
            Outcome = outcome,
            Success = outcome is PasteProgressOutcome.Ok or PasteProgressOutcome.Skipped,
            FailReason = failReason,
            ResultDetail = detail,
        };

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

        if (context.IgnoreMissingItems && res.FailReason == LayerFailReason.MissingItems)
            return PasteProgressOutcome.Skipped;

        if (context.IgnoreInvalidPlacement && res.FailReason == LayerFailReason.InvalidPlacement)
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
        }

        if (tile.Target.HasTile)
            return PasteProgressContentKind.Tile;
        if (tile.Target.WallType != WallID.None)
            return PasteProgressContentKind.Wall;
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
            return 0;
        }

        if (tile.Target.HasTile)
            return tile.Target.TileType;
        if (tile.Target.WallType != WallID.None)
            return tile.Target.WallType;
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
        PhaseTotal = PhaseTotal,
        PhaseProcessed = PhaseProcessed,
        PhaseRemaining = remaining,
    };
}
