using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core.Layers;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// A single paste operation. Fire-and-discard; each instance builds and executes its own task queues.
    /// </summary>
    internal sealed class PastingOperation
    {
        private enum Phase { Plan, Clear, Cut, Place, Sweep, Done }

        /// <summary>Sub-stages while <see cref="Phase.Plan"/> builds task queues incrementally.</summary>
        private enum PrepWorkStage
        {
            PlanPlace = 0,
            PlanClear = 1,
            PlanCut = 2,
            Done = 3,
        }

        private Phase _phase;
        private readonly PlanPhaseRunner _planPhase;
        private readonly ClearPhaseRunner _clearPhase;
        private readonly CutPhaseRunner _cutPhase;
        private readonly PlacePhaseRunner _placePhase;
        private readonly SweepPhaseRunner _sweepPhase;
        private PrepWorkStage _prepWork;
        private Point _prepIndex;
        private HashSet<Point>[]? _queuedClearByLayer;
        private HashSet<Point>[]? _queuedCutByLayer;
        private HashSet<Point>[]? _queuedPlaceByLayer;
        private HashSet<Point>? _allowedPlaceRelatives;
        private readonly HashSet<Point> _sweepQueuedWorldTiles = [];
        private readonly Queue<PasteTile> _sweepTasks = [];
        private bool _preparationComplete;

        public Projection Projection { get; }
        public LayerPipeline Pipeline { get; }

        /// <summary>Stable id for paste overlay registration on the client.</summary>
        internal int OverlayId { get; set; }

        private int _currentLayerIndex;
        private readonly PasteTileLookup Lookup;
        private readonly List<Inventory> _inventories = [];

        private readonly List<Queue<ClearingTask>> clearTasks = [];
        private readonly List<Queue<ClearingTask>> cutTasks = [];
        private readonly List<Queue<PlacingTask>> placeTasks = [];
        internal IReadOnlyList<Queue<ClearingTask>> ClearTasks => clearTasks;
        internal IReadOnlyList<Queue<ClearingTask>> CutTasks => cutTasks;
        internal IReadOnlyList<Queue<PlacingTask>> PlaceTasks => placeTasks;

        private readonly List<Point> _currentStepClearTiles = [];
        private readonly List<Point> _currentStepPlaceTiles = [];

        /// <summary>World tiles finished earlier on the current clear/place layer (overlay alpha 0.9).</summary>
        private readonly HashSet<Point> _overlayDoneThisLayer = [];

        private Phase _overlayDoneTrackedPhase = Phase.Plan;
        private int _overlayDoneTrackedLayer = -1;

        private readonly PasteProgressTracker _progress;

        public bool ApplySourceClear { get; }
        public PasteContext Context { get; private set; } = default!;
        public PastingConfig PasteConfig { get; private set; } = default!;
        public long NextStepTime { get; private set; }

        /// <summary>
        /// Sweep queues on <see cref="SweepPhaseRunner.OnEnter"/>; completion waits until the sweep queue is drained.
        /// </summary>
        public bool IsCompleted =>
            _phase == Phase.Done
            && _preparationComplete
            && (!_clearPhase.Active || !HasAnyTask(clearTasks))
            && (!_cutPhase.Active || !HasAnyTask(cutTasks))
            && (!_placePhase.Active || !HasAnyTask(placeTasks))
            && (!_sweepPhase.Active || _sweepTasks.Count == 0);

        private readonly EffectivePastePolicy _policy;

        /// <summary>
        /// Initializes a new instance of the <see cref="PastingOperation"/> class.
        /// </summary>
        /// <param name="projection"></param>
        /// <param name="config"></param>
        /// <param name="policy"></param>
        public PastingOperation(Projection projection, PastingConfig config, EffectivePastePolicy policy)
        {
            /// Validate projection ownership/payload, then initialize layer pipeline and tile lookup.

            // Validate the projection
            Projection = projection ?? throw new ArgumentNullException(nameof(projection));
            if (Projection.Owner < 0 || Projection.Owner >= Main.maxPlayers)
                throw new InvalidOperationException($"Projection has invalid owner index: {Projection.Owner}");
            if (!projection.HasPayload)
                throw new InvalidOperationException("Projection has no source payload. Recreate the projection.");

            // Initialize the paste configuration and client preferences.
            PasteConfig = config;
            _policy = policy;
            ApplySourceClear = (Projection.CutSource && !Projection.SourceArea.IsEmpty) || config.Phases.Place.EnableCutPasteProcess;
            // Compose the layer pipeline.
            Pipeline = LayerSystem.ComposePipeline();
            // Build the tile lookup.
            Lookup = BuildLookup();
            _planPhase = new PlanPhaseRunner(this, config.Phases.Plan);
            _clearPhase = new ClearPhaseRunner(this, config.Phases.Clear.ToPhaseSettings());
            _cutPhase = new CutPhaseRunner(this, config.Phases.Cut);
            _placePhase = new PlacePhaseRunner(this, config.Phases.Place.ToPhaseSettings());
            _sweepPhase = new SweepPhaseRunner(this, config.Phases.Sweep.ToPhaseSettings());
            _progress = new PasteProgressTracker(Projection.Owner)
            {
                CorrelationId = Projection.Id,
                OverlayId = OverlayId,
                PlaceStrategy = _policy.PlaceStrategy,
                SweepStrategy = _policy.SweepStrategy,
            };
            UpdateContext();
            TABLog.Debug(
                $"Paste started: sourceClear={ApplySourceClear} (projection={Projection.CutSource}, debug={config.Phases.Place.EnableCutPasteProcess}), targetArea=({Projection.Area.X},{Projection.Area.Y}) {Projection.Area.Width}x{Projection.Area.Height}",
                "Paste");
            TABLog.Debug(
                $"START | area=({Projection.Area.X},{Projection.Area.Y}) {Projection.Area.Width}x{Projection.Area.Height} | sourceClear={ApplySourceClear} | srcTiles={Projection.SourceTiles.Length} tgtTiles={Projection.TargetTiles.Length} | sweep={_policy.SweepStrategy} clear={_policy.ClearStrategy} retryOnMissingItems={_policy.RetryOnMissingItems} retryOnInvalidPlacement={_policy.RetryOnInvalidPlacement} requireItems={_policy.RequireItems}",
                "Paste");

            _phase = config.Phases.Plan.Active ? Phase.Plan : Phase.Clear;
            _prepWork = PrepWorkStage.PlanPlace;
            _prepIndex = new Point(0, 0);
            _preparationComplete = !config.Phases.Plan.Active;
            if (_phase == Phase.Plan)
            {
                TABLog.Debug("Stage: PLAN (building queues: PlaceSource, ClearTarget, SourceAreaClear)", "Paste");
            }
            else
            {
                InitializePlanningState();
                BuildBlindExecutionQueues();
                _phase = ResolveFirstEnabledExecutionPhase();
                GetRunner(_phase).OnEnter();
                TABLog.Debug($"Stage: {_phase.ToString().ToUpperInvariant()} (plan disabled — blind queue build complete)", "Paste");
            }
        }

        /// <summary>
        /// Executes one incremental queue step and returns whether additional work remains.
        /// </summary>
        public bool Step()
        {
            /// Process current phase queue and schedule the next allowed step time.

            // Validate the player.
            Player player = Context.Player;
            if (player is null || !player.active || player.dead)
                return false;

            if (_phase == Phase.Done)
                return false;

            SyncProgressIdentity();
            PhaseRunner runner = GetRunner(_phase);
            runner.ExecuteTick();

            if (_phase == Phase.Plan || _phase == Phase.Done || (_phase == Phase.Sweep && runner.TickDelayMs == 0))
                NextStepTime = 0;
            else
                NextStepTime = Environment.TickCount64 + GetRunner(_phase).TickDelayMs;

            return !IsCompleted;
        }

        private void SyncProgressIdentity()
        {
            _progress.CorrelationId = Projection.Id;
            _progress.OverlayId = OverlayId;
            _progress.PlaceStrategy = _policy.PlaceStrategy;
            _progress.SweepStrategy = _policy.SweepStrategy;
        }

        private PhaseRunner GetRunner(Phase phase) =>
            phase switch
            {
                Phase.Plan => _planPhase,
                Phase.Clear => _clearPhase,
                Phase.Cut => _cutPhase,
                Phase.Place => _placePhase,
                Phase.Sweep => _sweepPhase,
                _ => _planPhase,
            };

        /// <summary>
        /// Called when this operation leaves <see cref="PastingSystem"/> (completion, cancel, or invalid projection).
        /// Unlocks <see cref="Projection"/>.
        /// </summary>
        internal void Teardown()
        {
            Projection.Id = 0;
            Projection.Unlock();
        }

        /// <summary>
        /// Refreshes paste context from the owner player and inventory immediately before a step or sweep runs.
        /// </summary>
        public void UpdateContext()
        {
            // --- Rebuild context right before a step/sweep so slots and player reference match current world state.


            Context = new PasteContext(
                playerIndex: Projection.Owner,
                items: Projection.Inventories.Count > 0 ? InventoryManager.GetContentsFromInventories(_inventories) : null,
                inventories: Projection.Inventories.Count > 0 ? _inventories : null,
                policy: _policy);
        }

        /// <summary>
        /// Drives incremental preparation by dispatching to the active planning stage within a tile budget.
        /// </summary>
        /// <returns><c>true</c> if preparation is still in progress; <c>false</c> once all queues are finalized.</returns>
        private bool Init()
        {
            // --- Refresh context, lazily allocate queue state, then iterate plan stages until budget or stages exhaust.

            // Refresh paste context so inventory and player references match current world state.
            UpdateContext();

            int budget = _planPhase.TickSize;
            int prepUnitsThisCall = 0;

            // Load the inventories.
            _inventories.Clear();
            foreach (Rectangle inventory in Projection.Inventories)
            {
                _inventories.Add(Inventory.FromRectangle(inventory));
            }

            // Lazily allocate per-layer queues and dedup sets on the first prep call.
            if (_queuedClearByLayer is null)
                InitializePlanningState();

            // Build the contexts used by identify/planning for this batch.
            var identifyContext = new PasteContext(Projection.Owner, null, null, _policy);
            var planningContext = new PasteContext(
                Projection.Owner,
                InventoryManager.GetContentsFromInventories(_inventories),
                _inventories,
                _policy);

            // Consume the prep budget by advancing the active plan stage one tile at a time.
            while (budget > 0 && _prepWork < PrepWorkStage.Done)
            {
                PrepWorkStage current = _prepWork;
                bool hasMore;
                switch (current)
                {
                    case PrepWorkStage.PlanPlace:
                        hasMore = PlanPlaceTarget(ref budget, ref prepUnitsThisCall, in planningContext);
                        break;
                    case PrepWorkStage.PlanClear:
                        hasMore = PlanClearTarget(ref budget, ref prepUnitsThisCall, in identifyContext);
                        break;
                    case PrepWorkStage.PlanCut:
                        hasMore = PlanCutSource(ref budget, ref prepUnitsThisCall, in identifyContext);
                        break;
                    default:
                        _prepWork = PrepWorkStage.Done;
                        continue;
                }

                // A stage reports "no more" to hand off to the next one without consuming budget.
                if (!hasMore)
                    AdvancePrepStage(current);
            }

            // Emit a progress log and yield the frame if the stages are not yet exhausted.
            if (_prepWork < PrepWorkStage.Done)
            {
                TABLog.Debug(
                    $"Prep: batch stage={_prepWork} progress={FormatPrepProgress()} unitsThisFrame={prepUnitsThisCall} remainingBudget={budget}",
                    "Paste");
                return true;
            }

            // All stages reported done: finalize the operation's queue state.
            FinalizePlanning();
            return false;
        }

        /// <summary>
        /// Allocates the per-layer task queues, dedup sets, and allowed-relative set used during planning.
        /// </summary>
        private void InitializePlanningState()
        {
            // --- Size queue/dedup storage to the pipeline's layer counts and reset the prep cursor.

            // Reset the clear/place queues and rebuild one queue per layer.
            clearTasks.Clear();
            cutTasks.Clear();
            placeTasks.Clear();
            for (int i = 0; i < Pipeline.ClearOrder.Count; i++)
            {
                clearTasks.Add(new Queue<ClearingTask>());
                cutTasks.Add(new Queue<ClearingTask>());
            }
            for (int i = 0; i < Pipeline.PlaceOrder.Count; i++)
                placeTasks.Add(new Queue<PlacingTask>());

            // Allocate per-layer dedup sets matching the layer counts.
            _queuedClearByLayer = new HashSet<Point>[Pipeline.ClearOrder.Count];
            _queuedCutByLayer = new HashSet<Point>[Pipeline.ClearOrder.Count];
            _queuedPlaceByLayer = new HashSet<Point>[Pipeline.PlaceOrder.Count];
            for (int i = 0; i < _queuedClearByLayer.Length; i++)
            {
                _queuedClearByLayer[i] = [];
                _queuedCutByLayer[i] = [];
            }
            for (int i = 0; i < _queuedPlaceByLayer.Length; i++)
                _queuedPlaceByLayer[i] = [];

            // Start at PlaceSource with a fresh allowed-relatives accumulator.
            _allowedPlaceRelatives = [];
            _prepWork = PrepWorkStage.PlanPlace;
            _prepIndex = new Point(0, 0);

            TABLog.Debug("Prep: PlaceSource — queuing place tasks from source tiles", "Paste");
            TABLog.Debug(
                $"Prep: begin prepTileBudget={_planPhase.TickSize} sourceTiles={Projection.SourceTiles.Length} lookupTiles={Lookup.Count} targetTiles={Projection.TargetTiles.Length} clearStrategy={_policy.ClearStrategy} sourceAreaClear={ApplySourceClear}",
                "Paste");
            TABLog.Info("--- [Plan] START ---", chatColor: Color.LightSkyBlue);
        }

        /// <summary>
        /// Queues place tasks for a single source tile mapped into the target area.
        /// </summary>
        /// <returns><c>true</c> while source tiles remain to process; <c>false</c> once the PlaceSource stage is exhausted.</returns>
        private bool PlanPlaceTarget(ref int budget, ref int prepUnits, in PasteContext planningContext)
        {
            // --- Walk the source-tile list once, projecting each into place tasks at its target location.

            // Signal stage completion when the target-tile cursor is exhausted.
            if (_prepIndex.X >= Projection.TargetTiles.GetLength(0) || _prepIndex.Y >= Projection.TargetTiles.GetLength(1))
                return false;

            // Fetch the next target cell, advance the cursor, and respect the projection’s inclusion (not the live selection).
            Blueprint.BlueprintCell targetCell = Projection.TargetTiles[_prepIndex.X, _prepIndex.Y];
            _prepIndex.X++;
            if (_prepIndex.X >= Projection.TargetTiles.GetLength(0))
            {
                _prepIndex.X = 0;
                _prepIndex.Y++;
            }
            if (!targetCell.Included)
                return true;

            Point rel = targetCell.Relative;

            // Skip relatives that are not represented in the payload lookup.
            if (!Lookup.TryGetPasteTile(rel, out PasteTile pasteTile))
                return true;

            // Vacant payload cells are cleared at destination, not placed.
            if (pasteTile.PayloadCell.IsEmpty)
                return true;

            // Fan the tile out across every applicable place layer, recording allowed relatives for later stages.
            QueuePlaceLayersForTile(
                rel,
                in pasteTile,
                in Lookup,
                in planningContext,
                _queuedPlaceByLayer!,
                _allowedPlaceRelatives!);
            budget--;
            prepUnits++;
            return true;
        }

        /// <summary>
        /// Queues clear tasks at the destination, using transparent paste to restrict clears to the allowed place footprint.
        /// </summary>
        /// <returns><c>true</c> while destination tiles remain to process; <c>false</c> once the ClearTarget stage is exhausted.</returns>
        private bool PlanClearTarget(ref int budget, ref int prepUnits, in PasteContext identifyContext)
        {
            // --- Strategy routing for planned clears.
            bool isTransparent = _policy.ClearStrategy == ClearPhaseStrategy.Transparent;
            bool isNegative = _policy.ClearStrategy == ClearPhaseStrategy.Negative;
            bool useTargetCursor = isTransparent || isNegative;

            if (useTargetCursor)
            {
                if (_prepIndex.X >= Projection.TargetTiles.GetLength(0) || _prepIndex.Y >= Projection.TargetTiles.GetLength(1))
                    return false;

                var clearCell = Projection.TargetTiles[_prepIndex.X, _prepIndex.Y];
                Point rel = clearCell.Relative;
                _prepIndex.X++;
                if (_prepIndex.X >= Projection.TargetTiles.GetLength(0))
                {
                    _prepIndex.X = 0;
                    _prepIndex.Y++;
                }

                if (!clearCell.Included)
                    return true;

                bool inAllowed = _allowedPlaceRelatives!.Contains(rel);
                if (isTransparent && !inAllowed)
                    return true;
                if (isNegative && inAllowed)
                    return true;
                if (!Lookup.TryGetPasteTile(rel, out PasteTile pasteTile))
                    return true;

                QueueClearLayersForTile(
                    rel,
                    in pasteTile,
                    in Lookup,
                    in identifyContext,
                    _queuedClearByLayer!,
                    clearTasks,
                    _allowedPlaceRelatives!,
                    useSourceAreaWorld: false);
                budget--;
                prepUnits++;
                return true;
            }

            // Clear-all clears every lookup tile; still skip cells the projection has excluded.
            if (_prepIndex.X >= Lookup.PasteTiles.GetLength(0) || _prepIndex.Y >= Lookup.PasteTiles.GetLength(1))
                return false;

            PasteTile opaqueTile = Lookup.PasteTiles[_prepIndex.X, _prepIndex.Y];
            _prepIndex.X++;
            if (_prepIndex.X >= Lookup.PasteTiles.GetLength(0))
            {
                _prepIndex.X = 0;
                _prepIndex.Y++;
            }

            if (Projection.TryGetTileByRelative(opaqueTile.Relative, out var projCell) && !projCell.Included)
                return true;

            // Identify clear layers without any restriction — the full lookup footprint, minus excluded relatives.
            QueueClearLayersForTile(
                opaqueTile.Relative,
                in opaqueTile,
                in Lookup,
                in identifyContext,
                _queuedClearByLayer!,
                clearTasks,
                restrictClearRelatives: null,
                useSourceAreaWorld: false);
            budget--;
            prepUnits++;
            return true;
        }

        /// <summary>
        /// Queues clear tasks at the original source area for cut-paste; no-ops when source clear is disabled.
        /// </summary>
        /// <returns><c>true</c> while source-area tiles remain to process; <c>false</c> once the stage is skipped or exhausted.</returns>
        private bool PlanCutSource(ref int budget, ref int prepUnits, in PasteContext identifyContext)
        {
            // --- Only runs when cut-paste is active; mirrors the allowed footprint back onto the source area.

            // Skip the stage entirely when cut-paste is not in effect.
            if (!ApplySourceClear)
                return false;

            // One row/column at a time on the same relative grid as the target/payload; inclusion comes from the projection.
            if (_prepIndex.X >= Projection.TargetTiles.GetLength(0) || _prepIndex.Y >= Projection.TargetTiles.GetLength(1))
                return false;

            var cutCell = Projection.TargetTiles[_prepIndex.X, _prepIndex.Y];
            Point rel = cutCell.Relative;
            _prepIndex.X++;
            if (_prepIndex.X >= Projection.TargetTiles.GetLength(0))
            {
                _prepIndex.X = 0;
                _prepIndex.Y++;
            }

            if (!cutCell.Included)
                return true;

            // Restrict source-area clears to the allowed place footprint — we only cut what we would paste.
            if (!_allowedPlaceRelatives!.Contains(rel))
                return true;

            // Resolve the paste tile against the source area's world coordinates for the cut pass.
            if (!TryGetSourceAreaPasteTile(rel, out PasteTile pasteTile))
                return true;

            // Identify clear layers against the source-area world state and enqueue them.
            QueueClearLayersForTile(
                rel,
                in pasteTile,
                in Lookup,
                in identifyContext,
                _queuedCutByLayer!,
                cutTasks,
                _allowedPlaceRelatives!,
                useSourceAreaWorld: true);
            budget--;
            prepUnits++;
            return true;
        }

        /// <summary>
        /// Advances the prep cursor to the stage following <paramref name="from"/> and logs the transition.
        /// </summary>
        private void AdvancePrepStage(PrepWorkStage from)
        {
            // --- Bump the stage, reset the per-stage tile cursor, and emit the appropriate transition log.

            PrepWorkStage to = (PrepWorkStage)((int)from + 1);
            _prepWork = to;
            _prepIndex = new Point(0, 0);

            switch (to)
            {
                // Entering ClearTarget: announce the new stage.
                case PrepWorkStage.PlanClear:
                    TABLog.Debug("Prep: ClearTarget — queuing clears at destination", "Paste");
                    TABLog.Debug("Prep: stage PlaceSource -> ClearTarget", "Paste");
                    break;

                // Entering SourceAreaClear: log transparency mode and whether a cut pass will run.
                case PrepWorkStage.PlanCut:
                {
                    string mode = _policy.ClearStrategy.ToString();
                    if (ApplySourceClear)
                        TABLog.Debug($"Prep: SourceAreaClear — queuing cuts at source ({mode})", "Paste");
                    else
                        TABLog.Debug("Prep: no source cut — finalizing queues", "Paste");
                    TABLog.Debug($"Prep: stage ClearTarget -> SourceAreaClear ({mode})", "Paste");
                    break;
                }

                // Entering Done: distinguish skipped cut passes from completed ones in the log.
                case PrepWorkStage.Done:
                    if (ApplySourceClear)
                    {
                        TABLog.Debug("Prep: done — source-area clear pass finished", "Paste");
                        TABLog.Debug("Prep: stage SourceAreaClear -> Done", "Paste");
                    }
                    else
                    {
                        TABLog.Debug("Prep: stage SourceAreaClear skipped (no source clear) -> Done", "Paste");
                    }
                    break;
            }
        }

        /// <summary>
        /// Marks preparation complete, releases planning scratch state, and writes the debug task map.
        /// </summary>
        private void FinalizePlanning()
        {
            // --- Report queue sizes, flag preparation done, release scratch state, and dump the task map for debugging.

            // Tally final queue depth for diagnostic reporting.
            int placeBatches = 0;
            for (int i = 0; i < placeTasks.Count; i++)
                placeBatches += placeTasks[i].Count;
            int clearBatches = 0;
            for (int i = 0; i < clearTasks.Count; i++)
                clearBatches += clearTasks[i].Count;
            TABLog.Debug($"Prep: queues ready | placeBatches={placeBatches} clearBatches={clearBatches}", "Paste");
            TABLog.Debug($"Prep: complete placeBatches={placeBatches} clearBatches={clearBatches}", "Paste");
            TABLog.Info(
                $"--- [Plan] END (placeBatches={placeBatches}, clearBatches={clearBatches}) ---",
                chatColor: Color.LightGreen);
            if (placeBatches == 0)
            {
                TABLog.Warn(
                    $"Prep: no place tasks queued (retryOnMissingItems={Context.RetryOnMissingItems} retryOnInvalidPlacement={Context.RetryOnInvalidPlacement} requireItems={PasteConfig.Phases.Place.RequireItems}); layer place phases will be no-ops",
                    "Paste");
            }

            // Release planning scratch state — execution phases no longer need dedup sets.
            _preparationComplete = true;
            _queuedClearByLayer = null;
            _queuedCutByLayer = null;
            _queuedPlaceByLayer = null;
            _allowedPlaceRelatives = null;
            PasteTaskMapDebug.WriteTaskMap(Projection, ApplySourceClear, Pipeline, clearTasks, placeTasks);
        }

        private string FormatPrepProgress()
        {
            return _prepWork switch
            {
                PrepWorkStage.PlanPlace =>
                    $"{Math.Min(_prepIndex.X, Projection.TargetTiles.GetLength(0))}x{Math.Min(_prepIndex.Y, Projection.TargetTiles.GetLength(1))}",
                PrepWorkStage.PlanClear => (_policy.ClearStrategy == ClearPhaseStrategy.Transparent || _policy.ClearStrategy == ClearPhaseStrategy.Negative)
                    ? $"{Math.Min(_prepIndex.X, Projection.TargetTiles.GetLength(0))}x{Math.Min(_prepIndex.Y, Projection.TargetTiles.GetLength(1))}"
                    : $"{Math.Min(_prepIndex.X, Lookup.PasteTiles.GetLength(0))}x{Math.Min(_prepIndex.Y, Lookup.PasteTiles.GetLength(1))}",
                PrepWorkStage.PlanCut =>
                    $"{Math.Min(_prepIndex.X, Projection.TargetTiles.GetLength(0))}x{Math.Min(_prepIndex.Y, Projection.TargetTiles.GetLength(1))}",
                PrepWorkStage.Done => "complete",
                _ => "?"
            };
        }

        /// <summary>
        /// Builds highlight tiles for the client overlay (prepare phase has no overlay).
        /// </summary>
        internal bool TryBuildOverlaySnapshot(out PasteOverlaySnapshot snapshot)
        {
            snapshot = default;
            if (_phase == Phase.Plan || _phase == Phase.Done)
                return false;

            SyncOverlayDoneTrackingIfNeeded();

            int maxTiles = PasteConfig.Limits.Server.MaxTilesPerCategory;
            bool isPlacePhase = _phase == Phase.Place || _phase == Phase.Sweep;
            int remaining = maxTiles;
            var pendingList = new List<Point>();

            if (_phase == Phase.Place)
            {
                int idx = _currentLayerIndex;
                while (idx < placeTasks.Count && placeTasks[idx].Count == 0) idx++;
                if (idx < placeTasks.Count)
                {
                    foreach (var task in placeTasks[idx])
                    {
                        if (remaining <= 0) break;
                        remaining -= task.CollectOverlayPoints(pendingList, remaining);
                    }
                }
            }
            else if (_phase == Phase.Sweep)
            {
                int added = 0;
                foreach (PasteTile tile in _sweepTasks)
                {
                    if (remaining <= 0 || added >= maxTiles)
                        break;
                    pendingList.Add(tile.World);
                    remaining--;
                    added++;
                }
            }
            else
            {
                var layerQueues = _phase == Phase.Cut ? cutTasks : clearTasks;
                int idx = _currentLayerIndex;
                while (idx < layerQueues.Count && layerQueues[idx].Count == 0) idx++;
                if (idx < layerQueues.Count)
                {
                    foreach (var task in layerQueues[idx])
                    {
                        if (remaining <= 0) break;
                        remaining -= task.CollectOverlayPoints(pendingList, remaining);
                    }
                }
            }

            var doneList = new List<Point>(Math.Min(maxTiles, _overlayDoneThisLayer.Count));
            foreach (Point p in _overlayDoneThisLayer)
            {
                if (doneList.Count >= maxTiles)
                    break;
                doneList.Add(p);
            }

            snapshot = new PasteOverlaySnapshot
            {
                OverlayId = OverlayId,
                IsPlacePhase = isPlacePhase,
                Pending = pendingList.Count > 0 ? pendingList.ToArray() : [],
                Clear = _currentStepClearTiles.Count > 0 ? _currentStepClearTiles.ToArray() : [],
                Place = _currentStepPlaceTiles.Count > 0 ? _currentStepPlaceTiles.ToArray() : [],
                Done = doneList.Count > 0 ? doneList.ToArray() : []
            };
            return true;
        }

        /// <summary>Clears per-layer “done” overlay tiles when the active clear/place layer or phase changes.</summary>
        private void SyncOverlayDoneTrackingIfNeeded()
        {
            if (_phase != Phase.Clear && _phase != Phase.Cut && _phase != Phase.Place)
                return;

            if (_phase != _overlayDoneTrackedPhase || _currentLayerIndex != _overlayDoneTrackedLayer)
            {
                _overlayDoneThisLayer.Clear();
                _overlayDoneTrackedPhase = _phase;
                _overlayDoneTrackedLayer = _currentLayerIndex;
            }
        }

        /// <summary>
        /// Creates a lookup that pairs each source-relative tile with its source and target world state.
        /// </summary>
        private PasteTileLookup BuildLookup()
        {
            /// Full WxH grid: opaque clears walk every relative index. Sparse payload only fills some cells;
            /// each populated slot must set <see cref="PasteTile.Relative"/> to its grid index (see <see cref="PasteTileLookup"/>).

            Point size = Projection.PayloadSize;
            int w = size.X;
            int h = size.Y;
            var list = new PasteTile[w, h];

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    Point rel = new(x, y);
                    if (!Projection.TryGetTargetTile(rel, out Tile targetTile))
                        continue;

                    Projection.TryGetSourceTile(rel, out Tile sourceTile);
                    Point world = Projection.RelativeToWorld(rel);

                    PayloadCell cell = default;
                    InteractionEntry interaction;
                    if (Projection.Payload.TryGetCell(rel, out PayloadCell payloadCell) && !payloadCell.IsEmpty)
                    {
                        cell = payloadCell;
                        interaction = ResolveInteraction(cell.ToTile());
                    }
                    else
                    {
                        interaction = InteractionEntry.Create("Tile", mainId: -1);
                    }

                    list[x, y] = new PasteTile(
                        payloadCell: cell,
                        interaction: interaction,
                        source: sourceTile,
                        target: targetTile,
                        relative: rel,
                        world: world);
                }
            }

            return new PasteTileLookup(list);
        }

        /// <summary>
        /// Resolves the interaction category and main ID for a source tile payload.
        /// </summary>
        private static InteractionEntry ResolveInteraction(in Tile sourceTile)
        {
            // --- Prefer tile interaction metadata, then fall back to wall/liquid/default markers.

            if (sourceTile.HasTile)
            {
                int style = 0, alternate = 0;
                TileObjectData.GetTileInfo(sourceTile, ref style, ref alternate);
                return InteractionMapProvider.GetFirstTileInteraction(sourceTile.TileType, style)
                    ?? InteractionEntry.Create("Tile", mainId: sourceTile.TileType);
            }

            if (sourceTile.WallType != WallID.None)
                return InteractionEntry.Create("Wall", mainId: sourceTile.WallType);

            if (sourceTile.LiquidAmount > 0)
                return InteractionEntry.Create("Liquid", mainId: sourceTile.LiquidType);

            return InteractionEntry.Create("Tile", mainId: -1);
        }

        /// <summary>
        /// Resolves a paste tile against source-area world coordinates for source-clear passes.
        /// </summary>
        private bool TryGetSourceAreaPasteTile(Point rel, out PasteTile tile)
        {
            // --- Reuse payload/interaction from lookup; world/target = live source area. Source = empty “after cut”
            // intent so Layer.Match (source vs target) is false while content remains, and clear tasks enqueue.

            tile = default;
            if (!Lookup.TryGetPasteTile(rel, out PasteTile baseTile))
                return false;
            if (!Projection.TryGetSourceTile(rel, out Tile sourceAreaTile))
                return false;

            Point world = new(Projection.SourceArea.X + rel.X, Projection.SourceArea.Y + rel.Y);
            Tile clearedIntent = default;
            tile = new PasteTile(
                payloadCell: baseTile.PayloadCell,
                interaction: baseTile.Interaction,
                source: clearedIntent,
                target: sourceAreaTile,
                relative: rel,
                world: world);
            return true;
        }

        /// <summary>
        /// Queues every clear layer that applies to the given tile.
        /// </summary>
        private void QueueClearLayersForTile(
            Point rel,
            in PasteTile tile,
            in PasteTileLookup lookup,
            in PasteContext identifyContext,
            HashSet<Point>[] queuedClearByLayer,
            List<Queue<ClearingTask>> taskTargets,
            HashSet<Point> restrictClearRelatives,
            bool useSourceAreaWorld)
        {
            // --- Identify clear candidates from the live world, not the blueprint payload. Always use Target: for
            // cut-source, TryGetSourceAreaPasteTile sets Source to empty "after cut" intent and Target to the world at the source.
            // useSourceAreaWorld only switches CreatePasteTiles resolution (source-area vs destination world coords).

            var identifiedLayers = LayerSystem.Identify(in tile, in lookup, Pipeline, identifyContext, PasteConfig, PasteSubject.Target);
            for (int i = 0; i < identifiedLayers.Count; i++)
            {
                var identified = identifiedLayers[i];
                TABLog.Debug($"Identify clear layer={identified.Layer.Id} rel=({rel.X},{rel.Y}) tiles={identified.Tiles.Count}", "Paste");
            }

            for (int i = 0; i < identifiedLayers.Count; i++)
            {
                var identified = identifiedLayers[i];
                QueueClearLayer(identified.Layer, identified.Tiles, lookup, queuedClearByLayer, taskTargets, restrictClearRelatives, useSourceAreaWorld, in identifyContext);
            }
        }

        /// <summary>
        /// Queues every place layer that applies to the given tile.
        /// </summary>
        private void QueuePlaceLayersForTile(
            Point rel,
            in PasteTile tile,
            in PasteTileLookup lookup,
            in PasteContext planningContext,
            HashSet<Point>[] queuedPlaceByLayer,
            HashSet<Point> allowedPlaceRelatives)
        {
            // --- Vacant payload intent is handled by clear (and sweep when enabled), not place layers.
            if (tile.PayloadCell.IsEmpty)
                return;

            // --- Sweep phase applies payload directly; layer place identification is unnecessary when sweep runs.
            if (_sweepPhase.Active)
                return;

            // --- Identify place candidates from the payload intent.

            var identifiedLayers = LayerSystem.Identify(in tile, in lookup, Pipeline, planningContext, PasteConfig, PasteSubject.Payload);
            for (int i = 0; i < identifiedLayers.Count; i++)
            {
                var identified = identifiedLayers[i];
                TABLog.Debug($"Identify place layer={identified.Layer.Id} rel=({rel.X},{rel.Y}) tiles={identified.Tiles.Count} placementValid={identified.PlacementValid} hasItems={identified.HasRequiredItems}", "Paste");
            }

            for (int i = 0; i < identifiedLayers.Count; i++)
            {
                var identified = identifiedLayers[i];
                var clamped = ClampIdentified(identified.Tiles);
                if (clamped.Count == 0)
                    continue;

                if (!Context.IsSweepCheat
                    && ((!Context.RetryOnMissingItems && !identified.HasRequiredItems)
                        || (!Context.RetryOnInvalidPlacement && !identified.PlacementValid)))
                    continue;

                for (int j = 0; j < clamped.Count; j++)
                    allowedPlaceRelatives.Add(clamped[j].Relative);

                QueuePlaceLayer(identified.Layer, clamped, lookup, queuedPlaceByLayer, in planningContext);
            }
        }

        /// <summary>
        /// Queues a clear task from an already identified tile set.
        /// </summary>
        private void QueueClearLayer(
            Layer layer,
            IReadOnlyList<Blueprint.BlueprintCell> identified,
            in PasteTileLookup lookup,
            HashSet<Point>[] queuedClearByLayer,
            List<Queue<ClearingTask>> taskTargets,
            HashSet<Point> restrictClearRelatives,
            bool useSourceAreaWorld,
            in PasteContext identifyContext)
        {
            // --- Convert identified relatives into world tiles, skip no-op matches, then enqueue clear work.

            int layerIndex = Pipeline.ToClearIndex(layer);
            if (layerIndex < 0)
                return;

            var clamped = ClampIdentified(identified, restrictClearRelatives);
            if (clamped.Count == 0)
                return;

            PasteTileResolver resolver = useSourceAreaWorld ? TryGetSourceAreaPasteTile : lookup.TryGetPasteTile;
            bool skipMatchedClear = _policy.ClearStrategy != ClearPhaseStrategy.All;
            TileBatch? batch = CreateTileBatch(
                layer,
                clamped,
                queuedClearByLayer[layerIndex],
                resolver,
                forClearQueue: true,
                skipMatchForSourceCut: useSourceAreaWorld,
                skipMatchedClear: skipMatchedClear,
                in lookup,
                in identifyContext);
            if (batch is not null)
            {
                taskTargets[layerIndex].Enqueue(new ClearingTask(layer, batch));
                TABLog.Debug($"Queued clear task layer={layer.Id} tiles={batch.Count} queue={taskTargets[layerIndex].Count}", "Paste");
                Point world = batch.Head.World;
                TABLog.Info(
                    $"[Plan] [Clear] [{layer.Id}] [{world.X}, {world.Y}] Queued batch ({batch.Count} tiles)",
                    chatColor: Color.LightSkyBlue);
            }
        }

        /// <summary>
        /// Queues a place task from an already identified tile set.
        /// </summary>
        private void QueuePlaceLayer(
            Layer layer,
            IReadOnlyList<Blueprint.BlueprintCell> identified,
            in PasteTileLookup lookup,
            HashSet<Point>[] queuedPlaceByLayer,
            in PasteContext schedulingContext)
        {
            // --- Convert identified relatives into world tiles, skip no-op matches, then enqueue place work.

            int layerIndex = Pipeline.ToPlaceIndex(layer);
            if (layerIndex < 0)
                return;

            // All-strategy clears every footprint tile before place; do not skip "already correct" matches during planning.
            bool skipMatchedPlace = _policy.ClearStrategy != ClearPhaseStrategy.All;
            TileBatch? batch = CreateTileBatch(
                layer,
                identified,
                queuedPlaceByLayer[layerIndex],
                lookup.TryGetPasteTile,
                forClearQueue: false,
                skipMatchForSourceCut: false,
                skipMatchedClear: skipMatchedPlace,
                in lookup,
                in schedulingContext);
            if (batch is not null)
            {
                placeTasks[layerIndex].Enqueue(new PlacingTask(layer, batch));
                TABLog.Debug($"Queued place task layer={layer.Id} tiles={batch.Count} queue={placeTasks[layerIndex].Count}", "Paste");
                Point world = batch.Head.World;
                TABLog.Info(
                    $"[Plan] [Place] [{layer.Id}] [{world.X}, {world.Y}] Queued batch ({batch.Count} tiles)",
                    chatColor: Color.LightSkyBlue);
            }
        }

        /// <summary>
        /// Filters identified relatives and returns a detached bounded-tile list.
        /// </summary>
        private static List<Blueprint.BlueprintCell> ClampIdentified(
            IReadOnlyList<Blueprint.BlueprintCell> identified,
            HashSet<Point> allowedRelatives = null)
        {
            // --- Keep only allowed relatives (when provided) and copy selected flags into a compact list.

            var clamped = new List<Blueprint.BlueprintCell>(identified.Count);
            for (int i = 0; i < identified.Count; i++)
            {
                Point relative = identified[i].Relative;
                if (allowedRelatives is not null && !allowedRelatives.Contains(relative))
                    continue;

                clamped.Add(new Blueprint.BlueprintCell(relative, identified[i].World, identified[i].Included));
            }

            return clamped;
        }

        /// <summary>
        /// Builds the paste-tile payload for a single task while deduplicating world positions per layer.
        /// </summary>
        private delegate bool PasteTileResolver(Point relative, out PasteTile pasteTile);

        /// <summary>
        /// Resolves identified relatives into unique paste tiles that still require layer work.
        /// </summary>
        private static TileBatch? CreateTileBatch(
            Layer layer,
            IReadOnlyList<Blueprint.BlueprintCell> identified,
            HashSet<Point> queuedWorldTiles,
            PasteTileResolver resolver,
            bool forClearQueue,
            bool skipMatchForSourceCut,
            bool skipMatchedClear,
            in PasteTileLookup lookup,
            in PasteContext context)
        {
            // --- Deduplicate by world point and include only entries that match layer clear/place requirements.

            var pasteTiles = new List<PasteTile>(identified.Count);
            for (int i = 0; i < identified.Count; i++)
            {
                Point frameRel = identified[i].Relative;
                if (!resolver(frameRel, out PasteTile pasteTile))
                    continue;
                // Source-area cut: payload still describes the block, so payload-vs-target Match would skip the clear.
                if (!skipMatchForSourceCut && skipMatchedClear && layer.Match(in pasteTile))
                    continue;
                if (forClearQueue && !layer.CanClear(in pasteTile, in lookup, in context))
                    continue;
                if (!queuedWorldTiles.Add(pasteTile.World))
                    continue;
                pasteTiles.Add(pasteTile);
            }

            return pasteTiles.Count > 0 ? new TileBatch(pasteTiles) : null;
        }

        /// <summary>
        /// Executes up to one step-sized batch of clear tasks.
        /// </summary>
        private int ProcessClearQueue(Queue<ClearingTask> q, in PasteContext context, int tickSize, List<Point> currentStepTiles, HashSet<Point>? overlayDoneThisLayer)
        {
            // --- Dequeue a bounded batch, execute each task, and requeue incomplete work for future ticks.

            int success = 0;
            int batchTotal = Math.Min(tickSize, q.Count);
            for (int i = 0; i < batchTotal; i++)
            {
                if (q.Count == 0) break;
                var task = q.Dequeue();
                PasteProgress progress = _progress.ForBatch(i + 1, batchTotal);
                task.AddTilesTo(currentStepTiles, PasteConfig.Limits.Server.MaxTilesPerCategory);
                if (task.Execute(in context, in progress))
                {
                    _progress.RecordCompletion();
                    success++;
                    if (overlayDoneThisLayer is not null)
                    {
                        for (int ti = 0; ti < task.Tiles.Count; ti++)
                            overlayDoneThisLayer.Add(task.Tiles[ti].World);
                    }
                }
                else
                {
                    q.Enqueue(task);
                    if (task.Tiles.Count > 0)
                        TABLog.Debug($"Requeue clear task world=({task.Tiles[0].World.X},{task.Tiles[0].World.Y}) remainingTiles={task.Tiles.Count}", "Paste");
                }
            }

            return success;
        }

        /// <summary>
        /// Executes up to one step-sized batch of place tasks.
        /// </summary>
        private int ProcessPlaceQueue(
            Queue<PlacingTask> q,
            in PasteContext context,
            int tickSize,
            List<Point> currentStepTiles,
            HashSet<Point>? overlayDoneThisLayer,
            HashSet<Point>? successfulPlacedWorldTiles)
        {
            // --- Dequeue a bounded batch, execute each task, and requeue incomplete work for future ticks.

            int success = 0;
            int batchTotal = Math.Min(tickSize, q.Count);
            for (int i = 0; i < batchTotal; i++)
            {
                if (q.Count == 0) break;
                var task = q.Dequeue();
                PasteProgress progress = _progress.ForBatch(i + 1, batchTotal);
                task.AddTilesTo(currentStepTiles, PasteConfig.Limits.Server.MaxTilesPerCategory);
                if (task.Execute(in context, in progress))
                {
                    _progress.RecordCompletion();
                    success++;
                    if (overlayDoneThisLayer is not null)
                    {
                        for (int ti = 0; ti < task.Tiles.Count; ti++)
                            overlayDoneThisLayer.Add(task.Tiles[ti].World);
                    }
                    if (successfulPlacedWorldTiles is not null)
                    {
                        for (int ti = 0; ti < task.Tiles.Count; ti++)
                            successfulPlacedWorldTiles.Add(task.Tiles[ti].World);
                    }
                }
                else
                {
                    q.Enqueue(task);
                    if (task.Tiles.Count > 0)
                        TABLog.Debug($"Requeue place task world=({task.Tiles[0].World.X},{task.Tiles[0].World.Y}) remainingTiles={task.Tiles.Count}", "Paste");
                }
            }

            return success;
        }

        /// <summary>
        /// Advances to the next enabled phase and prepares phase-local queue state.
        /// </summary>
        private void AdvancePhase()
        {
            EndInfoPhaseFor(_phase);

            _currentLayerIndex = 0;
            _overlayDoneThisLayer.Clear();
            _overlayDoneTrackedLayer = -1;
            _overlayDoneTrackedPhase = _phase;

            _phase = _phase switch
            {
                Phase.Plan => NextEnabledPhaseAfter(Phase.Plan),
                Phase.Clear => NextEnabledPhaseAfter(Phase.Clear),
                Phase.Cut => NextEnabledPhaseAfter(Phase.Cut),
                Phase.Place => NextEnabledPhaseAfter(Phase.Place),
                Phase.Sweep => Phase.Done,
                _ => Phase.Done,
            };

            GetRunner(_phase).OnEnter();

            TABLog.Debug($"Stage: EXEC {_phase.ToString().ToUpperInvariant()}", "Paste");
        }

        /// <summary>
        /// Opens info logging for an execution phase and records its total task count.
        /// </summary>
        private void BeginInfoPhase(PasteInfoPhase infoPhase)
        {
            SyncProgressIdentity();
            int phaseTotal = CountRemainingInPhase(_phase);
            PasteProgress progress = _progress.BeginPhase(infoPhase, phaseTotal);
            PastingInfoLog.Write(in progress);
        }

        /// <summary>
        /// Closes info logging for the phase being left.
        /// </summary>
        private void EndInfoPhaseFor(Phase phase)
        {
            PasteInfoPhase? infoPhase = ToInfoPhase(phase);
            if (infoPhase is null)
                return;

            int remaining = CountRemainingInPhase(phase);
            PasteProgress progress = _progress.EndPhase(remaining);
            PastingInfoLog.Write(in progress);
        }

        private static PasteInfoPhase? ToInfoPhase(Phase phase) =>
            phase switch
            {
                Phase.Clear => PasteInfoPhase.Clear,
                Phase.Cut => PasteInfoPhase.Cut,
                Phase.Place => PasteInfoPhase.Place,
                Phase.Sweep => PasteInfoPhase.Sweep,
                _ => null,
            };

        private static int CountQueueTasks<T>(List<Queue<T>> queues)
        {
            int total = 0;
            for (int i = 0; i < queues.Count; i++)
                total += queues[i].Count;
            return total;
        }

        private int CountRemainingInPhase(Phase phase) =>
            phase switch
            {
                Phase.Clear => CountQueueTasks(clearTasks),
                Phase.Cut => CountQueueTasks(cutTasks),
                Phase.Place => CountQueueTasks(placeTasks),
                Phase.Sweep => _sweepTasks.Count,
                _ => 0,
            };

        private Phase ResolveFirstEnabledExecutionPhase()
        {
            if (IsPhaseEnabled(Phase.Clear))
                return Phase.Clear;
            if (IsPhaseEnabled(Phase.Cut))
                return Phase.Cut;
            if (IsPhaseEnabled(Phase.Place))
                return Phase.Place;
            if (IsPhaseEnabled(Phase.Sweep))
                return Phase.Sweep;
            return Phase.Done;
        }

        private Phase NextEnabledPhaseAfter(Phase phase)
        {
            if (phase == Phase.Plan)
                return ResolveFirstEnabledExecutionPhase();

            if (phase == Phase.Clear)
            {
                if (IsPhaseEnabled(Phase.Cut))
                    return Phase.Cut;
                if (IsPhaseEnabled(Phase.Place))
                    return Phase.Place;
                if (IsPhaseEnabled(Phase.Sweep))
                    return Phase.Sweep;
                return Phase.Done;
            }

            if (phase == Phase.Cut)
            {
                if (IsPhaseEnabled(Phase.Place))
                    return Phase.Place;
                if (IsPhaseEnabled(Phase.Sweep))
                    return Phase.Sweep;
                return Phase.Done;
            }

            if (phase == Phase.Place)
                return IsPhaseEnabled(Phase.Sweep) ? Phase.Sweep : Phase.Done;

            return Phase.Done;
        }

        private bool IsPhaseEnabled(Phase phase) => GetRunner(phase).Active;

        /// <summary>
        /// Moves the current layer pointer forward until it reaches a non-empty queue.
        /// </summary>
        private void AdvanceToNextNonEmptyLayer<T>(List<Queue<T>> queues)
        {
            // --- Skip exhausted layers so each step executes the next remaining work queue.

            while (_currentLayerIndex < queues.Count && queues[_currentLayerIndex].Count == 0)
                _currentLayerIndex++;
        }

        private void BuildBlindExecutionQueues()
        {
            // --- Build clear/cut/place queues without plan-derived filtering.
            PasteContext identifyContext = Context;
            for (int y = 0; y < Projection.TargetTiles.GetLength(1); y++)
            {
                for (int x = 0; x < Projection.TargetTiles.GetLength(0); x++)
                {
                    Blueprint.BlueprintCell targetCell = Projection.TargetTiles[x, y];
                    if (!targetCell.Included)
                        continue;

                    Point rel = targetCell.Relative;
                    if (!Lookup.TryGetPasteTile(rel, out PasteTile pasteTile))
                        continue;

                    bool overlap = !pasteTile.PayloadCell.IsEmpty;
                    bool queueClear = _policy.ClearStrategy switch
                    {
                        ClearPhaseStrategy.All => true,
                        ClearPhaseStrategy.Transparent => overlap,
                        ClearPhaseStrategy.Negative => !overlap,
                        _ => true,
                    };
                    if (queueClear)
                    {
                        QueueClearLayersForTile(
                            rel,
                            in pasteTile,
                            in Lookup,
                            in identifyContext,
                            _queuedClearByLayer!,
                            clearTasks,
                            restrictClearRelatives: null,
                            useSourceAreaWorld: false);
                    }

                    if (!pasteTile.PayloadCell.IsEmpty)
                    {
                        QueuePlaceLayersForTile(
                            rel,
                            in pasteTile,
                            in Lookup,
                            in identifyContext,
                            _queuedPlaceByLayer!,
                            _allowedPlaceRelatives!);
                    }

                    if (ApplySourceClear && TryGetSourceAreaPasteTile(rel, out PasteTile sourceAreaPaste))
                    {
                        QueueClearLayersForTile(
                            rel,
                            in sourceAreaPaste,
                            in Lookup,
                            in identifyContext,
                            _queuedCutByLayer!,
                            cutTasks,
                            restrictClearRelatives: null,
                            useSourceAreaWorld: true);
                    }
                }
            }
        }

        private void BuildSweepQueue()
        {
            _sweepTasks.Clear();
            _sweepQueuedWorldTiles.Clear();

            if (!_sweepPhase.Active)
                return;

            // --- Queue every included footprint tile; sweep applies payload intent directly (no layer identification).
            for (int y = 0; y < Projection.TargetTiles.GetLength(1); y++)
            {
                for (int x = 0; x < Projection.TargetTiles.GetLength(0); x++)
                {
                    Blueprint.BlueprintCell targetCell = Projection.TargetTiles[x, y];
                    if (!targetCell.Included)
                        continue;
                    if (!Lookup.TryGetPasteTile(targetCell.Relative, out PasteTile pasteTile))
                        continue;
                    if (_sweepQueuedWorldTiles.Add(pasteTile.World))
                        _sweepTasks.Enqueue(pasteTile);
                }
            }

            TABLog.Debug($"Sweep: queued {_sweepTasks.Count} tiles (full footprint)", "Paste");
        }

        private void ProcessSweepQueue()
        {
            _currentStepPlaceTiles.Clear();
            _currentStepClearTiles.Clear();
            if (_sweepTasks.Count == 0)
            {
                TABLog.Warn("Sweep: queue empty — no tiles to apply", "Paste");
                return;
            }

            int tickSize = _sweepPhase.TickSize;
            int batchTotal = Math.Min(tickSize, _sweepTasks.Count);

            int applied = 0;
            while (_sweepTasks.Count > 0 && applied < tickSize)
            {
                PasteTile pt = _sweepTasks.Dequeue();
                int wx = pt.World.X, wy = pt.World.Y;
                PasteProgress progress = _progress.ForBatch(applied + 1, batchTotal);
                if (!WorldGen.InWorld(wx, wy))
                {
                    PasteProgress progressEvent = progress.WithTaskResult(
                        in pt,
                        PasteProgressOutcome.Failed,
                        LayerFailReason.None,
                        "target tile outside world");
                    PastingInfoLog.Write(in progressEvent);
                    _progress.RecordCompletion();
                    applied++;
                    continue;
                }

                pt.PayloadCell.CopyToWorldTile(wx, wy);
                LayerSync.SyncTileSquare(wx, wy);
                bool sweepOk = PastingInfoLog.IsSweepSatisfied(in pt);
                PasteProgressOutcome outcome = sweepOk ? PasteProgressOutcome.Ok : PasteProgressOutcome.Failed;
                string? detail = sweepOk ? null : PastingInfoLog.ExplainSweepFailure(in pt);
                PasteProgress progressEvent2 = progress.WithTaskResult(in pt, outcome, LayerFailReason.None, detail);
                PastingInfoLog.Write(in progressEvent2);
                _progress.RecordCompletion();

                if (_currentStepPlaceTiles.Count < PasteConfig.Limits.Server.MaxTilesPerCategory)
                    _currentStepPlaceTiles.Add(pt.World);
                applied++;
            }

            TABLog.Debug($"Sweep: applied {applied} tiles, remaining={_sweepTasks.Count}", "Paste");
        }

        /// <summary>
        /// Clears all queued clear/place work for this operation.
        /// </summary>
        private void ClearAllTaskQueues()
        {
            // --- Reset all queues when operation exits or becomes invalid.

            for (int i = 0; i < clearTasks.Count; i++)
                clearTasks[i].Clear();
            for (int i = 0; i < cutTasks.Count; i++)
                cutTasks[i].Clear();
            for (int i = 0; i < placeTasks.Count; i++)
                placeTasks[i].Clear();
            _sweepTasks.Clear();
        }

        /// <summary>
        /// Returns whether any queue in the list still has pending tasks.
        /// </summary>
        private static bool HasAnyTask<T>(List<Queue<T>> queues)
        {
            // --- Fast scan used by completion checks and phase transitions.

            for (int i = 0; i < queues.Count; i++)
            {
                if (queues[i].Count > 0)
                    return true;
            }

            return false;
        }

        /// <summary>Shared per-phase settings and tick pacing for a paste operation stage.</summary>
        private abstract class PhaseRunner
        {
            protected readonly PastingOperation Operation;
            protected readonly PastePhaseSettings Settings;

            protected PhaseRunner(PastingOperation operation, PastePhaseSettings settings)
            {
                Operation = operation;
                Settings = settings;
            }

            public bool Active => Settings.Active;

            public int TickSize => Math.Max(1, Math.Min(Settings.TickSize, Operation.PasteConfig.Limits.Server.MaxPhaseTickSize));

            public int TickDelayMs => Math.Max(0, Settings.TickDelayMs);

            public abstract Phase Kind { get; }

            public virtual void OnEnter() { }

            public abstract void ExecuteTick();
        }

        /// <summary>Incrementally builds clear/cut/place task queues.</summary>
        private sealed class PlanPhaseRunner : PhaseRunner
        {
            public PlanPhaseRunner(PastingOperation operation, PastePhaseSettings settings) : base(operation, settings) { }

            public override Phase Kind => Phase.Plan;

            public override void ExecuteTick()
            {
                bool morePrep = Operation.Init();
                if (!morePrep)
                {
                    Operation._phase = Operation.ResolveFirstEnabledExecutionPhase();
                    Operation._currentLayerIndex = 0;
                    Operation.GetRunner(Operation._phase).OnEnter();
                    TABLog.Debug($"Stage: EXEC {Operation._phase.ToString().ToUpperInvariant()}", "Paste");
                }
            }
        }

        /// <summary>Executes destination clear tasks layer-by-layer.</summary>
        private sealed class ClearPhaseRunner : PhaseRunner
        {
            public ClearPhaseRunner(PastingOperation operation, PastePhaseSettings settings) : base(operation, settings) { }

            public override Phase Kind => Phase.Clear;

            public override void OnEnter() => Operation.BeginInfoPhase(PasteInfoPhase.Clear);

            public override void ExecuteTick()
            {
                Operation.AdvanceToNextNonEmptyLayer(Operation.clearTasks);
                if (Operation._currentLayerIndex >= Operation.clearTasks.Count)
                {
                    Operation.AdvancePhase();
                    return;
                }

                Operation.SyncOverlayDoneTrackingIfNeeded();
                var qClear = Operation.clearTasks[Operation._currentLayerIndex];
                var layerClear = Operation.Pipeline.ClearOrder[Operation._currentLayerIndex];
                TABLog.Debug($"Clear layer {layerClear.Id} (idx={Operation._currentLayerIndex}) tasks={qClear.Count}", "Paste");
                Operation._currentStepClearTiles.Clear();
                Operation._currentStepPlaceTiles.Clear();
                Operation.ProcessClearQueue(qClear, Operation.Context, TickSize, Operation._currentStepClearTiles, Operation._overlayDoneThisLayer);
            }
        }

        /// <summary>Executes source-area cut clears layer-by-layer.</summary>
        private sealed class CutPhaseRunner : PhaseRunner
        {
            public CutPhaseRunner(PastingOperation operation, PastePhaseSettings settings) : base(operation, settings) { }

            public override Phase Kind => Phase.Cut;

            public override void OnEnter() => Operation.BeginInfoPhase(PasteInfoPhase.Cut);

            public override void ExecuteTick()
            {
                Operation.AdvanceToNextNonEmptyLayer(Operation.cutTasks);
                if (Operation._currentLayerIndex >= Operation.cutTasks.Count)
                {
                    Operation.AdvancePhase();
                    return;
                }

                Operation.SyncOverlayDoneTrackingIfNeeded();
                var qCut = Operation.cutTasks[Operation._currentLayerIndex];
                var layerCut = Operation.Pipeline.ClearOrder[Operation._currentLayerIndex];
                TABLog.Debug($"Cut layer {layerCut.Id} (idx={Operation._currentLayerIndex}) tasks={qCut.Count}", "Paste");
                Operation._currentStepClearTiles.Clear();
                Operation._currentStepPlaceTiles.Clear();
                Operation.ProcessClearQueue(qCut, Operation.Context, TickSize, Operation._currentStepClearTiles, Operation._overlayDoneThisLayer);
            }
        }

        /// <summary>Executes place tasks layer-by-layer.</summary>
        private sealed class PlacePhaseRunner : PhaseRunner
        {
            public PlacePhaseRunner(PastingOperation operation, PastePhaseSettings settings) : base(operation, settings) { }

            public override Phase Kind => Phase.Place;

            public override void OnEnter() => Operation.BeginInfoPhase(PasteInfoPhase.Place);

            public override void ExecuteTick()
            {
                Operation.AdvanceToNextNonEmptyLayer(Operation.placeTasks);
                if (Operation._currentLayerIndex >= Operation.placeTasks.Count)
                {
                    Operation.AdvancePhase();
                    return;
                }

                Operation.SyncOverlayDoneTrackingIfNeeded();
                var qPlace = Operation.placeTasks[Operation._currentLayerIndex];
                var layerPlace = Operation.Pipeline.PlaceOrder[Operation._currentLayerIndex];
                TABLog.Debug($"Place layer {layerPlace.Id} (idx={Operation._currentLayerIndex}) tasks={qPlace.Count}", "Paste");
                Operation._currentStepPlaceTiles.Clear();
                Operation._currentStepClearTiles.Clear();
                Operation.ProcessPlaceQueue(
                    qPlace,
                    Operation.Context,
                    TickSize,
                    Operation._currentStepPlaceTiles,
                    Operation._overlayDoneThisLayer,
                    successfulPlacedWorldTiles: null);
            }
        }

        /// <summary>Applies sweep for the full queue; delay 0 completes the phase in one tick.</summary>
        private sealed class SweepPhaseRunner : PhaseRunner
        {
            public SweepPhaseRunner(PastingOperation operation, PastePhaseSettings settings) : base(operation, settings) { }

            public override Phase Kind => Phase.Sweep;

            public override void OnEnter()
            {
                Operation.BuildSweepQueue();
                Operation.BeginInfoPhase(PasteInfoPhase.Sweep);
            }

            public override void ExecuteTick()
            {
                Operation.ProcessSweepQueue();
                if (Operation._sweepTasks.Count == 0)
                    Operation.AdvancePhase();
            }
        }
    }
}
