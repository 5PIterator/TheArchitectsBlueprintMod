using System.Collections.Generic;
using Terraria;
using TheArchitectsBlueprintMod.Core.Layers;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core.Phases
{
    /// <summary>Placeholder strategy for the server-only sweep phase.</summary>
    public readonly struct SweepPhaseStrategy
    {
    }

    /// <summary>Applies sweep for the full queue; delay 0 completes the phase in one tick.</summary>
    internal sealed class SweepPhase : Phase<SweepPhaseStrategy>
    {
        private readonly HashSet<Microsoft.Xna.Framework.Point> _queuedWorldTiles = [];
        private readonly Queue<PasteTile> _sweepTasks = [];
        private bool _entitiesApplied;

        public SweepPhase(PastingOperation operation, PastePhasePolicy<SweepPhaseStrategy> policy) : base(operation, policy) { }

        public override string StageName => "SWEEP";

        public override PasteInfoPhase? InfoPhase => PasteInfoPhase.Sweep;

        public override Phase? Next(PastingOperation op) => null;

        public override int CountRemaining() => _sweepTasks.Count;

        internal bool HasPendingTasks => _sweepTasks.Count > 0;

        internal IEnumerable<PasteTile> PendingTasks => _sweepTasks;

        internal void ClearQueue()
        {
            _sweepTasks.Clear();
            _queuedWorldTiles.Clear();
            _entitiesApplied = false;
        }

        public override void OnEnter()
        {
            // --- Tile step only: queue blits. Entity purge/apply runs after the queue drains.

            _entitiesApplied = false;
            BuildSweepQueue();
            Operation.BeginInfoPhase(PasteInfoPhase.Sweep);
        }

        public override void ExecuteTick()
        {
            // --- Step 1: blit payload tiles. Step 2 (same tick when queue empties): purge then apply entities.

            ProcessSweepQueue();
            if (_sweepTasks.Count > 0)
                return;

            if (!_entitiesApplied)
            {
                WorldEntityOps.PurgeHostsInSweepArea(Operation.Projection.TargetArea);
                PasteContext ctx = Operation.Context;
                WorldEntityOps.ApplyAllPayloadEntitiesForSweep(in ctx);
                _entitiesApplied = true;
            }

            Operation.AdvancePhase();
        }

        private void BuildSweepQueue()
        {
            _sweepTasks.Clear();
            _queuedWorldTiles.Clear();
            if (!Active)
                return;

            for (int y = 0; y < Operation.Projection.TargetTiles.GetLength(1); y++)
            {
                for (int x = 0; x < Operation.Projection.TargetTiles.GetLength(0); x++)
                {
                    Blueprint.BlueprintCell targetCell = Operation.Projection.TargetTiles[x, y];
                    if (!targetCell.Included)
                        continue;
                    if (!Operation.TryGetLookupPasteTile(targetCell.Relative, out PasteTile pasteTile))
                        continue;
                    if (_queuedWorldTiles.Add(pasteTile.World))
                        _sweepTasks.Enqueue(pasteTile);
                }
            }

            TABLog.Debug($"Sweep: queued {_sweepTasks.Count} tiles (full footprint)", "Paste");
        }

        private void ProcessSweepQueue()
        {
            Operation._currentStepPlaceTiles.Clear();
            Operation._currentStepClearTiles.Clear();
            if (_sweepTasks.Count == 0)
                return;

            int batchTotal = System.Math.Min(TickSize, _sweepTasks.Count);
            int applied = 0;
            while (_sweepTasks.Count > 0 && applied < TickSize)
            {
                PasteTile pt = _sweepTasks.Dequeue();
                int wx = pt.World.X;
                int wy = pt.World.Y;
                PasteProgress progress = Operation.CreateBatchProgress(applied + 1, batchTotal);
                if (!WorldGen.InWorld(wx, wy))
                {
                    const string outsideDetail = "target tile outside world";
                    PasteProgress progressEvent = progress.WithTaskResult(
                        pt.World,
                        PasteProgressOutcome.Failed,
                        contentMessage: LayerLogFormat.FormatSweepPayload(in pt),
                        okLabel: "Error",
                        detail: outsideDetail);
                    PastingInfoLog.Write(in progressEvent);
                    Operation.RecordTaskCompletion(PasteProgressOutcome.Failed, detail: outsideDetail);
                    applied++;
                    continue;
                }

                // Apply first, then verify — deferred enqueue used to log Error before the tile existed.
                bool sweepApplied = TileExecutor.EnqueueSweep(pt, Operation.Context);
                bool sweepOk = sweepApplied && PastingInfoLog.IsSweepSatisfied(in pt);
                PasteProgressOutcome outcome = sweepOk ? PasteProgressOutcome.Ok : PasteProgressOutcome.Failed;
                string? detail = sweepOk ? null : PastingInfoLog.ExplainSweepFailure(in pt);
                string sweepOkLabel = sweepOk ? "Sweeped" : "Error";
                PasteProgress progressEvent2 = progress.WithTaskResult(
                    pt.World,
                    outcome,
                    contentMessage: LayerLogFormat.FormatSweepPayload(in pt),
                    okLabel: sweepOkLabel,
                    detail: detail);
                PastingInfoLog.Write(in progressEvent2);
                Operation.RecordTaskCompletion(outcome, detail: detail);

                if (Operation._currentStepPlaceTiles.Count < Operation.PasteConfig.Limits.Server.MaxTilesPerCategory)
                    Operation._currentStepPlaceTiles.Add(pt.World);
                applied++;
            }

            TABLog.Debug($"Sweep: applied {applied} tiles, remaining={_sweepTasks.Count}", "Paste");
        }
    }
}
