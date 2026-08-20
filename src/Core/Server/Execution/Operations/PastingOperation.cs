using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core.Layers;
using TheArchitectsBlueprintMod.Core.Phases;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// A single paste operation. Fire-and-discard; each instance builds and executes its own task queues.
    /// </summary>
    internal sealed class PastingOperation
    {
        /// <summary>Sub-stages while <see cref="PlanPhase"/> builds task queues incrementally.</summary>
        private enum PrepWorkStage
        {
            PlanPlace = 0,
            PlanClear = 1,
            PlanCut = 2,
            Done = 3,
        }

        internal Phase? _currentPhase;
        internal readonly PlanPhase _planPhase;
        internal readonly ClearPhase _clearPhase;
        internal readonly CutPhase _cutPhase;
        internal readonly PlacePhase _placePhase;
        internal readonly SweepPhase _sweepPhase;
        private PrepWorkStage _prepWork;
        private Point _prepIndex;
        private HashSet<Point>[]? _queuedClearByLayer;
        private HashSet<Point>[]? _queuedCutByLayer;
        private HashSet<Point>[]? _queuedPlaceByLayer;
        private HashSet<Point>? _allowedPlaceRelatives;
        private bool _preparationComplete;

        public Projection Projection { get; }
        public LayerPipeline Pipeline { get; }
        internal TileEffectSession EffectSession { get; }

        /// <summary>Stable id for paste overlay registration on the client.</summary>
        internal int OverlayId { get; set; }

        internal int _currentLayerIndex;
        private readonly PasteTileLookup Lookup;
        private readonly List<Inventory> _inventories = [];
        private readonly InventoryLookup _inventoryLookup = new();
        private readonly InteractionBudget _interactionBudget = new();

        internal readonly List<Queue<ClearingTask>> clearTasks = [];
        internal readonly List<Queue<ClearingTask>> cutTasks = [];
        internal readonly List<Queue<PlacingTask>> placeTasks = [];
        internal IReadOnlyList<Queue<ClearingTask>> ClearTasks => clearTasks;
        internal IReadOnlyList<Queue<ClearingTask>> CutTasks => cutTasks;
        internal IReadOnlyList<Queue<PlacingTask>> PlaceTasks => placeTasks;

        internal readonly List<Point> _currentStepClearTiles = [];
        internal readonly List<Point> _currentStepPlaceTiles = [];

        /// <summary>World tiles finished earlier on the current clear/place layer (overlay alpha 0.9).</summary>
        internal readonly HashSet<Point> _overlayDoneThisLayer = [];

        private Phase? _overlayDoneTrackedPhase;
        private int _overlayDoneTrackedLayer = -1;

        private readonly PasteProgressTracker _progress;

        public bool ApplySourceClear { get; }
        public PasteContext Context { get; private set; } = default!;
        public PastingConfig PasteConfig { get; private set; } = default!;
        public long NextStepTime { get; private set; }

        /// <summary>
        /// Sweep queues on <see cref="SweepPhase.OnEnter"/>; completion waits until the sweep queue is drained.
        /// Also waits until <see cref="TileEffectSession"/> has no queued or in-flight tile mutations.
        /// </summary>
        public bool IsCompleted =>
            _currentPhase is null
            && _preparationComplete
            && (!_clearPhase.Active || !HasAnyTask(clearTasks))
            && (!_cutPhase.Active || !HasAnyTask(cutTasks))
            && (!_placePhase.Active || !HasAnyTask(placeTasks))
            && (!_sweepPhase.Active || !_sweepPhase.HasPendingTasks)
            && EffectSession.IsIdle;

        /// <summary>True while tile mutations are still queued or projectiles are in flight for this operation.</summary>
        internal bool IsWaitingForTileEffects => !EffectSession.IsIdle;

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
            ApplySourceClear = (Projection.CutSource && !Projection.SourceArea.IsEmpty) || _policy.CutPhase.Strategy.EnableCutPasteProcess;
            // Compose the layer pipeline.
            Pipeline = LayerSystem.ComposePipeline();
            // Build the tile lookup.
            Lookup = PastePlanningLookup.Build(Projection);
            _planPhase = new PlanPhase(this, _policy.PlanPhase);
            _clearPhase = new ClearPhase(this, _policy.ClearPhase);
            _cutPhase = new CutPhase(this, _policy.CutPhase);
            _placePhase = new PlacePhase(this, _policy.PlacePhase);
            _sweepPhase = new SweepPhase(this, _policy.SweepPhase);
            EffectSession = new TileEffectSession(TileEffectSession.AllocateOperationId(), () => Context);
            _progress = new PasteProgressTracker(Projection.Owner)
            {
                CorrelationId = Projection.Id,
                OverlayId = OverlayId,
                PlacementStyle = _placePhase.Strategy.Style,
            };
            UpdateContext();
            TABLog.Debug(
                $"Paste started: sourceClear={ApplySourceClear} (projection={Projection.CutSource}, debug={_cutPhase.Strategy.EnableCutPasteProcess}), targetArea=({Projection.Area.X},{Projection.Area.Y}) {Projection.Area.Width}x{Projection.Area.Height}",
                "Paste");
            TABLog.Debug(
                $"START | area=({Projection.Area.X},{Projection.Area.Y}) {Projection.Area.Width}x{Projection.Area.Height} | sourceClear={ApplySourceClear} | srcTiles={Projection.SourceTiles.Length} tgtTiles={Projection.TargetTiles.Length} | sweep={_sweepPhase.Active} planClear={_planPhase.Strategy.Style} clearItemDrop={_clearPhase.Strategy.ItemDrop} retryOnMissingItems={_placePhase.Strategy.RetryOnMissingItems} retryOnInvalidPlacement={_placePhase.Strategy.RetryOnInvalidPlacement} requireItems={_placePhase.Strategy.RequireItems}",
                "Paste");

            _prepWork = PrepWorkStage.PlanPlace;
            _prepIndex = new Point(0, 0);
            _preparationComplete = !_planPhase.Active;
            if (_planPhase.Active)
            {
                _currentPhase = _planPhase;
                TABLog.Debug("Stage: PLAN (building queues: PlaceSource, ClearTarget, SourceAreaClear)", "Paste");
            }
            else
            {
                InitializePlanningState();
                BuildBlindExecutionQueues();
                _currentPhase = ResolveFirstEnabledExecutionPhase();
                _currentPhase?.OnEnter();
                TABLog.Debug($"Stage: {_currentPhase?.StageName ?? "DONE"} (plan disabled — blind queue build complete)", "Paste");
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

            if (_currentPhase is null)
            {
                NextStepTime = 0;
                return !IsCompleted;
            }

            SyncProgressIdentity();
            Phase phase = _currentPhase;
            phase.ExecuteTick();

            Phase? activePhase = _currentPhase;
            if (activePhase is PlanPhase || activePhase is null || (activePhase is SweepPhase && activePhase.TickSpeed == 0))
                NextStepTime = 0;
            else
                NextStepTime = Environment.TickCount64 + activePhase.TickSpeed;

            return !IsCompleted;
        }

        private void SyncProgressIdentity()
        {
            _progress.CorrelationId = Projection.Id;
            _progress.OverlayId = OverlayId;
            _progress.PlacementStyle = _placePhase.Strategy.Style;
        }

        internal bool TryGetLookupPasteTile(Point relative, out PasteTile pasteTile) =>
            Lookup.TryGetPasteTile(relative, out pasteTile);

        internal PasteProgress CreateBatchProgress(int batchNumber, int batchTotal) =>
            _progress.ForBatch(batchNumber, batchTotal);

        internal void RecordTaskCompletion(
            PasteProgressOutcome outcome,
            LayerFailReason failReason = LayerFailReason.None,
            string? detail = null) =>
            _progress.RecordCompletion(outcome, failReason, detail);

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

            RefreshInventories();
            _inventoryLookup.Refresh(_inventories);

            Context = new PasteContext(
                playerIndex: Projection.Owner,
                inventoryLookup: _inventoryLookup,
                policy: _policy,
                lookup: Lookup,
                effectSession: EffectSession,
                interactionBudget: _interactionBudget,
                payload: Projection.Payload);
        }

        /// <summary>Builds place context with every pending frame candidate from the frame layer queue.</summary>
        internal PasteContext BuildFramePlaceContext(int frameLayerIndex)
        {
            // --- Snapshot queued frame tasks so placement can defer objects waiting on lower support.
            return new PasteContext(
                playerIndex: Projection.Owner,
                inventoryLookup: _inventoryLookup,
                policy: _policy,
                lookup: Lookup,
                effectSession: EffectSession,
                pendingFramePlaceCandidates: CollectPendingFramePlaceCandidates(frameLayerIndex),
                interactionBudget: _interactionBudget,
                payload: Projection.Payload);
        }

        /// <summary>Collects logical frame objects still waiting in the frame place queue.</summary>
        internal List<FrameOperations.FrameCandidate> CollectPendingFramePlaceCandidates(int frameLayerIndex)
        {
            // --- Flatten every queued task footprint, then regroup by placement origin.
            var pendingTiles = new List<PasteTile>();
            Queue<PlacingTask> queue = placeTasks[frameLayerIndex];
            if (queue.Count == 0)
                return [];

            PlacingTask[] snapshot = [.. queue];
            for (int i = 0; i < snapshot.Length; i++)
            {
                IReadOnlyList<PasteTile> tiles = snapshot[i].Tiles;
                for (int j = 0; j < tiles.Count; j++)
                    pendingTiles.Add(tiles[j]);
            }

            return FrameOperations.GroupIntoCandidates(pendingTiles, forClear: false);
        }

        /// <summary>
        /// Reloads nearby chest inventories and optionally the in-range player inventory fallback.
        /// </summary>
        private void RefreshInventories()
        {
            // --- Rescan chests, assign distance priorities, then append in-range player as priority 0.
            _inventories.Clear();

            // Refresh chest footprints from world state so paste does not rely on a stale client cache.
            Projection.RefreshInventories();
            Rectangle targetArea = Projection.TargetArea;

            int discoveryIndex = 0;
            foreach (Rectangle footprint in Projection.Inventories)
            {
                int priority = InventoryManager.ComputeChestPriority(footprint, targetArea);
                _inventories.Add(Inventory.FromRectangle(footprint, priority, discoveryIndex));
                discoveryIndex++;
            }

            // Player inventory is only eligible inside the same reach window used for chests.
            Player player = Main.player[Projection.Owner];
            if (InventoryManager.IsPlayerInInventoryRange(player, targetArea))
                _inventories.Add(Inventory.FromPlayer(player, Inventory.PlayerPriority, discoveryIndex));

            // Prefer nearer chests first for store iteration; keep discovery order for equal distances.
            _inventories.Sort(CompareInventoryPriority);
        }

        /// <summary>Orders inventory sources by descending priority, then ascending discovery index.</summary>
        private static int CompareInventoryPriority(Inventory a, Inventory b)
        {
            // --- Higher priority first; equal priorities keep discovery order.
            int byPriority = b.Priority.CompareTo(a.Priority);
            if (byPriority != 0)
                return byPriority;
            return a.DiscoveryIndex.CompareTo(b.DiscoveryIndex);
        }

        /// <summary>
        /// Drives incremental preparation by dispatching to the active planning stage within a tile budget.
        /// </summary>
        /// <returns><c>true</c> if preparation is still in progress; <c>false</c> once all queues are finalized.</returns>
        internal bool Init()
        {
            // --- Refresh context, lazily allocate queue state, then iterate plan stages until budget or stages exhaust.

            UpdateContext();

            int budget = _planPhase.TickSize;
            int prepUnitsThisCall = 0;

            // Lazily allocate per-layer queues and dedup sets on the first prep call.
            if (_queuedClearByLayer is null)
                InitializePlanningState();

            // Clear identification ignores inventory; place planning uses <see cref="Context"/>.
            var identifyContext = new PasteContext(Projection.Owner, null, _policy, Lookup, null, payload: Projection.Payload);
            PasteContext planningContext = Context;

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
                $"Prep: begin prepTileBudget={_planPhase.TickSize} sourceTiles={Projection.SourceTiles.Length} lookupTiles={Lookup.Count} targetTiles={Projection.TargetTiles.Length} clearStrategy={_planPhase.Strategy.Style} sourceAreaClear={ApplySourceClear}",
                "Paste");
            TABLog.Info(
                "--- [Plan] START ---",
                TABLog.GenericCategory,
                chatColor: Color.LightSkyBlue,
                ownerPlayer: Projection.Owner);
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
            bool isTransparent = _planPhase.Strategy.Style == ClearStyle.Transparent;
            bool isNegative = _planPhase.Strategy.Style == ClearStyle.Negative;
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

            // Clear-all walks the lookup grid; skip unpopulated (excluded) slots.
            if (_prepIndex.X >= Lookup.PasteTiles.GetLength(0) || _prepIndex.Y >= Lookup.PasteTiles.GetLength(1))
                return false;

            Point opaqueRel = new(_prepIndex.X, _prepIndex.Y);
            _prepIndex.X++;
            if (_prepIndex.X >= Lookup.PasteTiles.GetLength(0))
            {
                _prepIndex.X = 0;
                _prepIndex.Y++;
            }

            if (!Lookup.TryGetPasteTile(opaqueRel, out PasteTile opaqueTile))
                return true;

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
                    string mode = _planPhase.Strategy.Style.ToString();
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
                PastingInfoLog.FormatPlanEnd(placeBatches, clearBatches),
                TABLog.GenericCategory,
                chatColor: Color.LightGreen,
                ownerPlayer: Projection.Owner);
            if (placeBatches == 0)
            {
                TABLog.Warn(
                    $"Prep: no place tasks queued (retryOnMissingItems={Context.RetryOnMissingItems} requireItems={_placePhase.Strategy.RequireItems}); layer place phases will be no-ops",
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
                PrepWorkStage.PlanClear => (_planPhase.Strategy.Style == ClearStyle.Transparent || _planPhase.Strategy.Style == ClearStyle.Negative)
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
            if (_currentPhase is PlanPhase || _currentPhase is null)
                return false;

            SyncOverlayDoneTrackingIfNeeded();

            int maxTiles = PasteConfig.Limits.Server.MaxTilesPerCategory;
            bool isPlacePhase = _currentPhase is PlacePhase or SweepPhase;
            int remaining = maxTiles;
            var pendingList = new List<Point>();

            if (_currentPhase is PlacePhase)
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
            else if (_currentPhase is SweepPhase)
            {
                int added = 0;
                foreach (PasteTile tile in _sweepPhase.PendingTasks)
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
                var layerQueues = _currentPhase is CutPhase ? cutTasks : clearTasks;
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
        internal void SyncOverlayDoneTrackingIfNeeded()
        {
            if (_currentPhase is not ClearPhase and not CutPhase and not PlacePhase)
                return;

            if (_currentPhase != _overlayDoneTrackedPhase || _currentLayerIndex != _overlayDoneTrackedLayer)
            {
                _overlayDoneThisLayer.Clear();
                _overlayDoneTrackedPhase = _currentPhase;
                _overlayDoneTrackedLayer = _currentLayerIndex;
            }
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
                QueueClearLayer(
                    identified.Layer,
                    identified.Tiles,
                    identified.Result.TileBatch?.ClusterContext,
                    identified.Message,
                    lookup,
                    queuedClearByLayer,
                    taskTargets,
                    restrictClearRelatives,
                    useSourceAreaWorld,
                    in identifyContext);
            }
        }

        /// <summary>
        /// Identifies and queues every place layer for the given tile.
        /// </summary>
        private void QueuePlaceLayersForTile(
            Point rel,
            in PasteTile tile,
            in PasteTileLookup lookup,
            in PasteContext planningContext,
            HashSet<Point>[] queuedPlaceByLayer,
            HashSet<Point> allowedPlaceRelatives)
        {
            // --- Identify payload layers and enqueue place work; Place owns item/validity failures.

            // Vacant payload intent is handled by clear (and sweep when enabled), not tile place layers.
            // Entity sidecar can still identify at an origin whose payload cell is empty.
            if (tile.PayloadCell.IsEmpty
                && (planningContext.Payload is null || !planningContext.Payload.TryGetEntity(rel, out _)))
                return;

            // Sweep phase applies payload directly; layer place identification is unnecessary when sweep runs.
            if (_sweepPhase.Active)
                return;

            // Identify place candidates from the payload intent.
            var identifiedLayers = LayerSystem.Identify(in tile, in lookup, Pipeline, planningContext, PasteConfig, PasteSubject.Payload);
            for (int i = 0; i < identifiedLayers.Count; i++)
            {
                var identified = identifiedLayers[i];
                TABLog.Debug(
                    $"Identify place layer={identified.Layer.Id} rel=({rel.X},{rel.Y}) tiles={identified.Tiles.Count} "
                    + $"placementValid={identified.PlacementValid} hasItems={identified.HasRequiredItems}",
                    "Paste");

                // Note pre-clear inventory gaps; Place still decides after Store clears return items.
                if (planningContext.RequireItems && !identified.HasRequiredItems)
                {
                    TABLog.Debug(
                        $"Plan queues place despite missing items layer={identified.Layer.Id} "
                        + $"rel=({rel.X},{rel.Y}) — Place/TileExecutor will recheck after Clear",
                        "Paste");
                }
            }

            // Queue every identified place batch; Place/TileExecutor own item and validity failures.
            for (int i = 0; i < identifiedLayers.Count; i++)
            {
                var identified = identifiedLayers[i];
                var clamped = ClampIdentified(identified.Tiles);
                if (clamped.Count == 0)
                    continue;

                for (int j = 0; j < clamped.Count; j++)
                    allowedPlaceRelatives.Add(clamped[j].Relative);

                QueuePlaceLayer(
                    identified.Layer,
                    clamped,
                    identified.Result.TileBatch?.ClusterContext,
                    identified.Message,
                    lookup,
                    queuedPlaceByLayer,
                    in planningContext);
            }
        }

        /// <summary>
        /// Queues a clear task from an already identified tile set.
        /// </summary>
        private void QueueClearLayer(
            Layer layer,
            IReadOnlyList<Blueprint.BlueprintCell> identified,
            TileBatchClusterContext? clusterContext,
            string? identifyMessage,
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
            bool skipMatchedClear = _planPhase.Strategy.Style != ClearStyle.All;
            TileBatch? batch = CreateTileBatch(
                layer,
                clamped,
                clusterContext,
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
                PastingInfoLog.Write(_progress.ForPlanTask().WithTaskResult(
                    world,
                    PasteProgressOutcome.Ok,
                    contentMessage: identifyMessage,
                    okLabel: $"Queued clear batch ({batch.Count} tiles)",
                    layerId: layer.Id));
            }
        }

        /// <summary>
        /// Queues a place task from an already identified tile set.
        /// </summary>
        private void QueuePlaceLayer(
            Layer layer,
            IReadOnlyList<Blueprint.BlueprintCell> identified,
            TileBatchClusterContext? clusterContext,
            string? identifyMessage,
            in PasteTileLookup lookup,
            HashSet<Point>[] queuedPlaceByLayer,
            in PasteContext schedulingContext)
        {
            // --- Convert identified relatives into world tiles, skip no-op matches, then enqueue place work.

            int layerIndex = Pipeline.ToPlaceIndex(layer);
            if (layerIndex < 0)
                return;

            // All-strategy clears every footprint tile before place; do not skip "already correct" matches during planning.
            bool skipMatchedPlace = _planPhase.Strategy.Style != ClearStyle.All;
            TileBatch? batch = CreateTileBatch(
                layer,
                identified,
                clusterContext,
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
                PastingInfoLog.Write(_progress.ForPlanTask().WithTaskResult(
                    world,
                    PasteProgressOutcome.Ok,
                    contentMessage: identifyMessage,
                    okLabel: $"Queued place batch ({batch.Count} tiles)",
                    layerId: layer.Id));
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
            TileBatchClusterContext? clusterContext,
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

            return pasteTiles.Count > 0 ? new TileBatch(pasteTiles, clusterContext) : null;
        }

        /// <summary>
        /// Advances to the next enabled phase and prepares phase-local queue state.
        /// Defers while tile-effect mutations from the current phase are still queued or in flight.
        /// </summary>
        internal void AdvancePhase()
        {
            if (!EffectSession.IsIdle)
            {
                TABLog.Debug(
                    $"Waiting for tile effects before phase advance queued={EffectSession.PendingMutationCount} inFlight={EffectSession.PendingImpactCount}",
                    "Paste");
                return;
            }

            EndInfoPhaseFor(_currentPhase);

            _currentLayerIndex = 0;
            _overlayDoneThisLayer.Clear();
            _overlayDoneTrackedLayer = -1;
            _overlayDoneTrackedPhase = _currentPhase;

            _currentPhase = _currentPhase?.Next(this);
            _currentPhase?.OnEnter();

            TABLog.Debug($"Stage: EXEC {_currentPhase?.StageName ?? "DONE"}", "Paste");
        }

        /// <summary>
        /// Opens info logging for an execution phase and records its total task count.
        /// </summary>
        internal void BeginInfoPhase(PasteInfoPhase infoPhase)
        {
            SyncProgressIdentity();
            int phaseTotal = _currentPhase?.CountRemaining() ?? 0;
            PasteProgress progress = _progress.BeginPhase(infoPhase, phaseTotal);
            PastingInfoLog.Write(in progress);
        }

        /// <summary>
        /// Closes info logging for the phase being left.
        /// </summary>
        private void EndInfoPhaseFor(Phase? phase)
        {
            PasteInfoPhase? infoPhase = phase?.InfoPhase;
            if (infoPhase is null)
                return;

            int remaining = phase.CountRemaining();
            PasteProgress progress = _progress.EndPhase(remaining);
            PastingInfoLog.Write(in progress);
        }

        internal static int CountQueueTasks<T>(List<Queue<T>> queues)
        {
            int total = 0;
            for (int i = 0; i < queues.Count; i++)
                total += queues[i].Count;
            return total;
        }

        internal Phase? ResolveFirstEnabledExecutionPhase()
        {
            if (_clearPhase.Active)
                return _clearPhase;
            if (_cutPhase.Active)
                return _cutPhase;
            if (_placePhase.Active)
                return _placePhase;
            if (_sweepPhase.Active)
                return _sweepPhase;
            return null;
        }

        /// <summary>
        /// Moves the current layer pointer forward until it reaches a non-empty queue.
        /// </summary>
        internal void AdvanceToNextNonEmptyLayer<T>(List<Queue<T>> queues)
        {
            // --- Skip exhausted layers so each step executes the next remaining work queue.

            while (_currentLayerIndex < queues.Count && queues[_currentLayerIndex].Count == 0)
                _currentLayerIndex++;
        }

        /// <summary>
        /// True when the current layer queue is empty but tile effects from that layer are still in flight.
        /// </summary>
        internal bool TryWaitForCurrentLayerEffects<T>(List<Queue<T>> queues)
        {
            // --- Keep Wall (and earlier) place arrows finished before Paint observes live walls.

            if (_currentLayerIndex < 0 || _currentLayerIndex >= queues.Count)
                return false;

            if (queues[_currentLayerIndex].Count > 0 || EffectSession.IsIdle)
                return false;

            string layerId = _currentPhase == _placePhase
                ? Pipeline.PlaceOrder[_currentLayerIndex].Id
                : _currentPhase == _clearPhase
                    ? Pipeline.ClearOrder[_currentLayerIndex].Id
                    : "?";
            TABLog.Debug(
                $"Waiting for tile effects before leaving layer={layerId} "
                + $"queued={EffectSession.PendingMutationCount} inFlight={EffectSession.PendingImpactCount}",
                "Paste");
            return true;
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
                    bool queueClear = _planPhase.Strategy.Style switch
                    {
                        ClearStyle.All => true,
                        ClearStyle.Transparent => overlap,
                        ClearStyle.Negative => !overlap,
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
            _sweepPhase.ClearQueue();
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
    }
}
