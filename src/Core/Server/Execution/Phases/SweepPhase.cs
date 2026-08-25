using System.Collections.Generic;
using Microsoft.Xna.Framework;
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
                WorldEntityOps.PurgeHostsInSweepArea(Operation.Execution.TargetArea);
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

            for (int y = 0; y < Operation.Execution.GridHeight; y++)
            {
                for (int x = 0; x < Operation.Execution.GridWidth; x++)
                {
                    Point rel = new(x, y);
                    if (!Operation.Execution.IsIncluded(rel))
                        continue;
                    if (!Operation.TryGetLookupPasteTile(rel, out PasteTile pasteTile))
                        continue;
                    if (_queuedWorldTiles.Add(pasteTile.World))
                        _sweepTasks.Enqueue(pasteTile);
                }
            }

            TABLog.Debug($"Sweep: queued {_sweepTasks.Count} tiles (full footprint)", "Paste");
        }

        private void ProcessSweepQueue()
        {
            // --- Drain up to TickSize tiles into one TileBatch, stamp sync AABB, then apply each inline.

            Operation._currentStepPlaceTiles.Clear();
            Operation._currentStepClearTiles.Clear();
            if (_sweepTasks.Count == 0)
                return;

            // Collect this tick's sweep tiles into a batch so MP sync covers the full AABB.
            int batchTotal = System.Math.Min(TickSize, _sweepTasks.Count);
            var tickTiles = new List<PasteTile>(batchTotal);
            while (_sweepTasks.Count > 0 && tickTiles.Count < TickSize)
                tickTiles.Add(_sweepTasks.Dequeue());

            var syncBatch = new TileBatch(tickTiles);
            PasteContext ctx = Operation.Context;
            if (!syncBatch.IsEmpty)
                ctx.EffectSession?.BeginSyncBatch(syncBatch, syncBatch.Head);

            try
            {
                int applied = 0;
                for (int i = 0; i < tickTiles.Count; i++)
                {
                    PasteTile pt = tickTiles[i];
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
                    bool sweepApplied = TileExecutor.EnqueueSweep(pt, ctx);
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
            finally
            {
                if (!syncBatch.IsEmpty)
                    ctx.EffectSession?.EndSyncBatchPass();
            }
        }
    }
}
