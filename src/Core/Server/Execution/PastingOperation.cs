using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;
using ArchitectsStencilMod.Config;
using ArchitectsStencilMod.Core.Layers;
using ArchitectsStencilMod.Core.TileMapping;
using ArchitectsStencilMod.Debug;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// A single paste operation. Fire-and-discard; each instance builds and executes its own task queues.
    /// </summary>
    internal sealed class PastingOperation
    {
        private enum Phase { Prepare, Clear, Cut, Place }

        /// <summary>Sub-stages while <see cref="Phase.Prepare"/> builds task queues incrementally.</summary>
        private enum PrepWorkStage
        {
            PlanPlace = 0,
            PlanClear = 1,
            PlanCut = 2,
            Done = 3,
        }

        private Phase _phase;
        private PrepWorkStage _prepWork;
        private int _prepIndex;
        private HashSet<Point>[]? _queuedClearByLayer;
        private HashSet<Point>[]? _queuedPlaceByLayer;
        private HashSet<Point>? _allowedPlaceRelatives;
        private bool _preparationComplete;

        public Projection Projection { get; }
        public LayerPipeline Pipeline { get; }

        /// <summary>Stable id for paste overlay registration on the client.</summary>
        internal int OverlayId { get; set; }

        private int _currentLayerIndex;
        private readonly PasteTileLookup Lookup;
        private const int MaxCurrentStepTiles = 128;
        /// <summary>Tiles of queue-building work per <see cref="Step"/> while in <see cref="Phase.Prepare"/>.</summary>
        private const int PreparePhaseTileBudget = 50;

        private readonly List<Inventory> _inventories = [];

        private readonly List<Queue<ClearingTask>> clearTasks = [];
        private readonly List<Queue<PlacingTask>> placeTasks = [];
        internal IReadOnlyList<Queue<ClearingTask>> ClearTasks => clearTasks;
        internal IReadOnlyList<Queue<PlacingTask>> PlaceTasks => placeTasks;

        private readonly List<Point> _currentStepClearTiles = [];
        private readonly List<Point> _currentStepPlaceTiles = [];

        /// <summary>World tiles finished earlier on the current clear/place layer (overlay alpha 0.9).</summary>
        private readonly HashSet<Point> _overlayDoneThisLayer = new HashSet<Point>();

        private Phase _overlayDoneTrackedPhase = Phase.Prepare;
        private int _overlayDoneTrackedLayer = -1;

        public bool ApplySourceClear { get; }
        public PasteContext Context { get; private set; } = default!;
        public PastingConfig PasteConfig { get; private set; } = default!;
        public long NextStepTime { get; private set; }

        /// <summary>
        /// Cheat-sweep runs without queued tasks until <see cref="Sweep"/> applies; do not treat empty queues as done while cheat is enabled.
        /// </summary>
        public bool IsCompleted =>
            !PasteConfig.CheatSweep
            && _preparationComplete
            && !HasAnyTask(clearTasks)
            && !HasAnyTask(placeTasks);

        private readonly ClientPreferences _clientPaste;

        /// <summary>
        /// Initializes a new instance of the <see cref="PastingOperation"/> class.
        /// </summary>
        /// <param name="projection"></param>
        /// <param name="config"></param>
        /// <param name="clientPaste"></param>
        public PastingOperation(Projection projection, PastingConfig config, ClientPreferences clientPaste)
        {
            /// Validate projection ownership/payload, then initialize layer pipeline and tile lookup.

            // Validate the projection
            Projection = projection ?? throw new ArgumentNullException(nameof(projection));
            if (Projection.OwnerPlayerIndex < 0 || Projection.OwnerPlayerIndex >= Main.maxPlayers)
                throw new InvalidOperationException($"Projection has invalid owner index: {Projection.OwnerPlayerIndex}");
            if (!projection.HasPayload)
                throw new InvalidOperationException("Projection has no source payload. Recreate the projection.");

            // Initialize the paste configuration and client preferences.
            PasteConfig = config;
            _clientPaste = clientPaste;
            ApplySourceClear = (Projection.CutSource && !Projection.SourceArea.IsEmpty) || config.EnableCutPasteProcess;
            // Compose the layer pipeline.
            Pipeline = LayerSystem.ComposePipeline();
            // Build the tile lookup.
            Lookup = BuildLookup();
            UpdateContext();
            ASMLog.Debug(
                $"Paste started: sourceClear={ApplySourceClear} (projection={Projection.CutSource}, debug={config.EnableCutPasteProcess}), targetArea=({Projection.Bounds.X},{Projection.Bounds.Y}) {Projection.Bounds.Width}x{Projection.Bounds.Height}",
                "Paste");
            ASMLog.Info(
                $"START | area=({Projection.Bounds.X},{Projection.Bounds.Y}) {Projection.Bounds.Width}x{Projection.Bounds.Height} | sourceClear={ApplySourceClear} | srcTiles={Projection.SourceTiles.Count} tgtTiles={Projection.TargetTiles.Count}",
                "Paste");

            _phase = config.CheatSweep ? Phase.Clear : Phase.Prepare;
            _prepWork = PrepWorkStage.PlanPlace;
            _prepIndex = 0;
            _preparationComplete = config.CheatSweep;
            if (_phase == Phase.Prepare)
                ASMLog.Info("Stage: PREP (building queues: PlaceSource, then ClearTarget, then optional SourceAreaClear)", "Paste");
            else
                ASMLog.Info("Stage: CLEAR (cheat sweep — preparation skipped)", "Paste");
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

            // Process the current phase.
            switch (_phase)
            {
                // Prepare phase: build task queues.
                case Phase.Prepare:
                    bool morePrep = Init();
                    if (!morePrep)
                    {
                        _phase = Phase.Clear;
                        _currentLayerIndex = 0;
                        ASMLog.Info("Stage: EXEC CLEAR — running queued clear tasks", "Paste");
                    }

                    // --- Throttling (PasteDelayMs) applies only to clear/place execution, not task preparation.
                    NextStepTime = 0;
                    return !IsCompleted;
                // Clear phase: execute queued clear tasks.
                case Phase.Clear:
                    AdvanceToNextNonEmptyLayer(clearTasks);
                    if (_currentLayerIndex >= clearTasks.Count)
                    {
                        bool keep = AdvancePhase();
                        return keep;
                    }

                    SyncOverlayDoneTrackingIfNeeded();
                    var qClear = clearTasks[_currentLayerIndex];
                    var layerClear = Pipeline.ClearOrder[_currentLayerIndex];
                    ASMLog.Debug($"Clear layer {layerClear.Id} (idx={_currentLayerIndex}) tasks={qClear.Count}", "Paste");
                    _currentStepClearTiles.Clear();
                    _currentStepPlaceTiles.Clear();
                    ProcessClearQueue(qClear, Context, _currentStepClearTiles, _overlayDoneThisLayer);
                    break;
                // Cut phase: execute queued cut tasks.
                case Phase.Cut:
                    break;
                // Place phase: execute queued place tasks.
                case Phase.Place:
                    AdvanceToNextNonEmptyLayer(placeTasks);
                    if (_currentLayerIndex >= placeTasks.Count)
                    {
                        bool keep = AdvancePhase();
                        return keep;
                    }

                    SyncOverlayDoneTrackingIfNeeded();
                    var qPlace = placeTasks[_currentLayerIndex];
                    var layerPlace = Pipeline.PlaceOrder[_currentLayerIndex];
                    ASMLog.Debug($"Place layer {layerPlace.Id} (idx={_currentLayerIndex}) tasks={qPlace.Count}", "Paste");
                    _currentStepPlaceTiles.Clear();
                    _currentStepClearTiles.Clear();
                    ProcessPlaceQueue(qPlace, Context, _currentStepPlaceTiles, _overlayDoneThisLayer);
                    break;
                default:
                    break;
            }

            NextStepTime = Environment.TickCount64 + Context.PasteDelayMs;
            return !IsCompleted;
        }

        /// <summary>
        /// Executes cheat-sweep placement in one pass and always completes the operation.
        /// </summary>
        public bool Sweep()
        {
            // --- Identify the full sweep footprint, place once, and then clear queue state for operation teardown.

            Player player = Context.Player;
            if (player is null || !player.active || player.dead)
                return false;

            Layer sweep = LayerSystem.Sweep;
            if (Lookup.PasteTiles.Count == 0)
            {
                ClearAllTaskQueues();
                NextStepTime = 0;
                return false;
            }

            PasteTile anchorPasteTile = Lookup.PasteTiles[0];
            IReadOnlyList<Selection.BoundedTile> identified = sweep.Identify(in anchorPasteTile, in Lookup);
            if (identified.Count == 0)
            {
                ClearAllTaskQueues();
                NextStepTime = 0;
                return false;
            }

            List<PasteTile> pasteTiles = CreateSweepCheatPasteTiles(identified, in Lookup);
            if (pasteTiles.Count == 0)
            {
                ClearAllTaskQueues();
                NextStepTime = 0;
                return false;
            }

            _currentStepPlaceTiles.Clear();
            _currentStepClearTiles.Clear();
            int overlayCap = Math.Min(MaxCurrentStepTiles, pasteTiles.Count);
            for (int i = 0; i < overlayCap; i++)
                _currentStepPlaceTiles.Add(pasteTiles[i].World);

            PasteContext placeContext = Context;
            LayerResult placeResult = sweep.Place(pasteTiles, in placeContext);
            if (!placeResult.Success)
                ASMLog.Debug($"CheatSweep place incomplete: layer={sweep.Id} remaining={placeResult.RemainingTiles.Count}", "Paste");

            ClearAllTaskQueues();
            NextStepTime = 0;
            return false;
        }

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
                playerIndex: Projection.OwnerPlayerIndex,
                items: Projection.Inventories.Count > 0 ? InventoryManager.GetContentsFromInventories(_inventories) : null,
                serverConfig: PasteConfig,
                clientPrefs: _clientPaste);
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

            int budget = PreparePhaseTileBudget;
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
            var identifyContext = new PasteContext(Projection.OwnerPlayerIndex, null, PasteConfig, _clientPaste);
            var planningContext = new PasteContext(
                Projection.OwnerPlayerIndex,
                InventoryManager.GetContentsFromInventories(_inventories),
                PasteConfig,
                _clientPaste);

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
                ASMLog.Debug(
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
            placeTasks.Clear();
            for (int i = 0; i < Pipeline.ClearOrder.Count; i++)
                clearTasks.Add(new Queue<ClearingTask>());
            for (int i = 0; i < Pipeline.PlaceOrder.Count; i++)
                placeTasks.Add(new Queue<PlacingTask>());

            // Allocate per-layer dedup sets matching the layer counts.
            _queuedClearByLayer = new HashSet<Point>[Pipeline.ClearOrder.Count];
            _queuedPlaceByLayer = new HashSet<Point>[Pipeline.PlaceOrder.Count];
            for (int i = 0; i < _queuedClearByLayer.Length; i++)
                _queuedClearByLayer[i] = [];
            for (int i = 0; i < _queuedPlaceByLayer.Length; i++)
                _queuedPlaceByLayer[i] = [];

            // Start at PlaceSource with a fresh allowed-relatives accumulator.
            _allowedPlaceRelatives = [];
            _prepWork = PrepWorkStage.PlanPlace;
            _prepIndex = 0;

            ASMLog.Info("Prep: PlaceSource — queuing place tasks from source tiles", "Paste");
            ASMLog.Debug(
                $"Prep: begin prepTileBudget={PreparePhaseTileBudget} sourceTiles={Projection.SourceTiles.Count} lookupTiles={Lookup.Count} targetTiles={Projection.TargetTiles.Count} transparent={_clientPaste.TransparentPaste} sourceAreaClear={ApplySourceClear}",
                "Paste");
        }

        /// <summary>
        /// Queues place tasks for a single source tile mapped into the target area.
        /// </summary>
        /// <returns><c>true</c> while source tiles remain to process; <c>false</c> once the PlaceSource stage is exhausted.</returns>
        private bool PlanPlaceTarget(ref int budget, ref int prepUnits, in PasteContext planningContext)
        {
            // --- Walk the source-tile list once, projecting each into place tasks at its target location.

            // Signal stage completion when the source-tile cursor is exhausted.
            if (_prepIndex >= Projection.SourceTiles.Count)
                return false;

            // Fetch the next source tile, advance the cursor, and respect inclusion (excluded = no place/clear footprint).
            Selection.BoundedTile sourceCell = Projection.SourceTiles[_prepIndex];
            _prepIndex++;
            if (!sourceCell.Included)
                return true;

            Point rel = sourceCell.Relative;

            // Skip relatives that are not represented in the payload lookup.
            if (!Lookup.TryGetPasteTile(rel, out PasteTile pasteTile))
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
            // --- Transparent paste only clears tiles intersecting the place footprint; opaque paste clears the full lookup.

            bool transparent = _clientPaste.TransparentPaste;

            // Transparent paste restricts clears to target tiles that the place planner has already approved.
            if (transparent)
            {
                // Signal stage completion when the target-tile cursor is exhausted.
                if (_prepIndex >= Projection.TargetTiles.Count)
                    return false;

                Point rel = Projection.TargetTiles[_prepIndex].Relative;
                _prepIndex++;

                // Skip target tiles outside the allowed place footprint or missing from the lookup.
                if (!_allowedPlaceRelatives!.Contains(rel))
                    return true;
                if (!Lookup.TryGetPasteTile(rel, out PasteTile pasteTile))
                    return true;

                // Identify every clear layer that applies to the destination tile and restrict to the allowed footprint.
                QueueClearLayersForTile(
                    rel,
                    in pasteTile,
                    in Lookup,
                    in identifyContext,
                    _queuedClearByLayer!,
                    _allowedPlaceRelatives!,
                    useSourceAreaWorld: false);
                budget--;
                prepUnits++;
                return true;
            }

            // Opaque paste clears every lookup tile regardless of whether a place task was queued there.
            if (_prepIndex >= Lookup.Count)
                return false;

            PasteTile opaqueTile = Lookup.PasteTiles[_prepIndex];
            _prepIndex++;

            // Identify clear layers without any restriction — the full footprint is fair game.
            QueueClearLayersForTile(
                opaqueTile.Relative,
                in opaqueTile,
                in Lookup,
                in identifyContext,
                _queuedClearByLayer!,
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

            // Signal stage completion when every source tile has been considered.
            if (_prepIndex >= Projection.SourceTiles.Count)
                return false;

            Point rel = Projection.SourceTiles[_prepIndex].Relative;
            _prepIndex++;

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
                _queuedClearByLayer!,
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
            _prepIndex = 0;

            switch (to)
            {
                // Entering ClearTarget: announce the new stage.
                case PrepWorkStage.PlanClear:
                    ASMLog.Info("Prep: ClearTarget — queuing clears at destination", "Paste");
                    ASMLog.Debug("Prep: stage PlaceSource -> ClearTarget", "Paste");
                    break;

                // Entering SourceAreaClear: log transparency mode and whether a cut pass will run.
                case PrepWorkStage.PlanCut:
                {
                    string mode = _clientPaste.TransparentPaste ? "transparent" : "opaque";
                    if (ApplySourceClear)
                        ASMLog.Info($"Prep: SourceAreaClear — queuing cuts at source ({mode})", "Paste");
                    else
                        ASMLog.Info("Prep: no source cut — finalizing queues", "Paste");
                    ASMLog.Debug($"Prep: stage ClearTarget -> SourceAreaClear ({mode})", "Paste");
                    break;
                }

                // Entering Done: distinguish skipped cut passes from completed ones in the log.
                case PrepWorkStage.Done:
                    if (ApplySourceClear)
                    {
                        ASMLog.Info("Prep: done — source-area clear pass finished", "Paste");
                        ASMLog.Debug("Prep: stage SourceAreaClear -> Done", "Paste");
                    }
                    else
                    {
                        ASMLog.Debug("Prep: stage SourceAreaClear skipped (no source clear) -> Done", "Paste");
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
            ASMLog.Info($"Prep: queues ready | placeBatches={placeBatches} clearBatches={clearBatches}", "Paste");
            ASMLog.Debug($"Prep: complete placeBatches={placeBatches} clearBatches={clearBatches}", "Paste");

            // Release planning scratch state — execution phases no longer need dedup sets.
            _preparationComplete = true;
            _queuedClearByLayer = null;
            _queuedPlaceByLayer = null;
            _allowedPlaceRelatives = null;
            PasteTaskMapDebug.WriteTaskMap(Projection, ApplySourceClear, Pipeline, clearTasks, placeTasks);
        }

        private string FormatPrepProgress()
        {
            return _prepWork switch
            {
                PrepWorkStage.PlanPlace =>
                    $"{Math.Min(_prepIndex, Projection.SourceTiles.Count)}/{Projection.SourceTiles.Count}",
                PrepWorkStage.PlanClear => _clientPaste.TransparentPaste
                    ? $"{Math.Min(_prepIndex, Projection.TargetTiles.Count)}/{Projection.TargetTiles.Count}"
                    : $"{Math.Min(_prepIndex, Lookup.Count)}/{Lookup.Count}",
                PrepWorkStage.PlanCut =>
                    $"{Math.Min(_prepIndex, Projection.SourceTiles.Count)}/{Projection.SourceTiles.Count}",
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
            if (_phase == Phase.Prepare)
                return false;

            SyncOverlayDoneTrackingIfNeeded();

            const int MaxOverlayTiles = 128;
            bool isPlacePhase = _phase == Phase.Place;
            int remaining = MaxOverlayTiles;
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
            else
            {
                int idx = _currentLayerIndex;
                while (idx < clearTasks.Count && clearTasks[idx].Count == 0) idx++;
                if (idx < clearTasks.Count)
                {
                    foreach (var task in clearTasks[idx])
                    {
                        if (remaining <= 0) break;
                        remaining -= task.CollectOverlayPoints(pendingList, remaining);
                    }
                }
            }

            var doneList = new List<Point>(Math.Min(MaxOverlayTiles, _overlayDoneThisLayer.Count));
            foreach (Point p in _overlayDoneThisLayer)
            {
                if (doneList.Count >= MaxOverlayTiles)
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
            if (_phase != Phase.Clear && _phase != Phase.Place)
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
            /// Full source payload grid (WxH): opaque clears and layer identify need deselected / empty cells too.

            // Get the size of the payload.
            Point size = Projection.PayloadSize;
            int w = size.X;
            int h = size.Y;
            var list = new List<PasteTile>(Math.Max(0, w * h));

            // For each source-relative tile, resolve the payload, source intent, and target world tile, along with interaction metadata for layer logic.
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Point rel = new(x, y);
                    // Validate payload
                    if (!Projection.Payload.TryGetCell(rel, out PayloadCell cell) || cell.IsEmpty)
                        continue;
                    // Validate source
                    if (!Projection.TryGetSourceTile(rel, out Tile sourceTile))
                        continue;

                    if (!Projection.TryGetTargetTile(rel, out Tile targetTile))
                        continue;

                    Point world = Projection.RelativeToWorld(rel);
                    InteractionEntry interaction = ResolveInteraction(cell.ToTile());
                    list.Add(new PasteTile(
                        payloadCell: cell,
                        interaction: interaction,
                        source: sourceTile,
                        target: targetTile,
                        relative: rel,
                        world: world));
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
            Tile clearedIntent = new Tile();
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
            HashSet<Point> restrictClearRelatives,
            bool useSourceAreaWorld)
        {
            // --- Identify clear candidates from the destination (or cut source) world, not the blueprint payload.

            PasteTile identifyTile = tile.WithSubject(PasteSubject.Target);
            var identifiedLayers = LayerSystem.Identify(in identifyTile, in lookup, Pipeline, identifyContext, PasteConfig);
            for (int i = 0; i < identifiedLayers.Count; i++)
            {
                var identified = identifiedLayers[i];
                ASMLog.Debug($"Identify clear layer={identified.Layer.Id} rel=({rel.X},{rel.Y}) tiles={identified.Tiles.Count}", "Paste");
            }

            for (int i = 0; i < identifiedLayers.Count; i++)
            {
                var identified = identifiedLayers[i];
                QueueClearLayer(identified.Layer, identified.Tiles, lookup, queuedClearByLayer, restrictClearRelatives, useSourceAreaWorld, in identifyContext);
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
            // --- Identify place candidates from the source payload / selection intent.

            PasteTile identifyTile = tile.WithSubject(PasteSubject.Source);
            var identifiedLayers = LayerSystem.Identify(in identifyTile, in lookup, Pipeline, planningContext, PasteConfig);
            for (int i = 0; i < identifiedLayers.Count; i++)
            {
                var identified = identifiedLayers[i];
                ASMLog.Debug($"Identify place layer={identified.Layer.Id} rel=({rel.X},{rel.Y}) tiles={identified.Tiles.Count} canPlace={identified.CanPlace}", "Paste");
            }

            for (int i = 0; i < identifiedLayers.Count; i++)
            {
                var identified = identifiedLayers[i];
                var clamped = ClampIdentified(identified.Tiles);
                if (clamped.Count == 0)
                    continue;

                if (!identified.CanPlace && Context.ForceTiles)
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
            IReadOnlyList<Selection.BoundedTile> identified,
            in PasteTileLookup lookup,
            HashSet<Point>[] queuedClearByLayer,
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
            var tiles = CreatePasteTiles(layer, clamped, queuedClearByLayer[layerIndex], resolver, forClearQueue: true, in lookup, in identifyContext);
            if (tiles.Count > 0)
            {
                clearTasks[layerIndex].Enqueue(new ClearingTask(layer, tiles));
                ASMLog.Debug($"Queued clear task layer={layer.Id} tiles={tiles.Count} queue={clearTasks[layerIndex].Count}", "Paste");
            }
        }

        /// <summary>
        /// Queues a place task from an already identified tile set.
        /// </summary>
        private void QueuePlaceLayer(
            Layer layer,
            IReadOnlyList<Selection.BoundedTile> identified,
            in PasteTileLookup lookup,
            HashSet<Point>[] queuedPlaceByLayer,
            in PasteContext schedulingContext)
        {
            // --- Convert identified relatives into world tiles, skip no-op matches, then enqueue place work.

            int layerIndex = Pipeline.ToPlaceIndex(layer);
            if (layerIndex < 0)
                return;

            var tiles = CreatePasteTiles(layer, identified, queuedPlaceByLayer[layerIndex], lookup.TryGetPasteTile, forClearQueue: false, in lookup, in schedulingContext);
            if (tiles.Count > 0)
            {
                placeTasks[layerIndex].Enqueue(new PlacingTask(layer, tiles));
                ASMLog.Debug($"Queued place task layer={layer.Id} tiles={tiles.Count} queue={placeTasks[layerIndex].Count}", "Paste");
            }
        }

        /// <summary>
        /// Filters identified relatives and returns a detached bounded-tile list.
        /// </summary>
        private static List<Selection.BoundedTile> ClampIdentified(
            IReadOnlyList<Selection.BoundedTile> identified,
            HashSet<Point> allowedRelatives = null)
        {
            // --- Keep only allowed relatives (when provided) and copy selected flags into a compact list.

            var clamped = new List<Selection.BoundedTile>(identified.Count);
            for (int i = 0; i < identified.Count; i++)
            {
                Point relative = identified[i].Relative;
                if (allowedRelatives is not null && !allowedRelatives.Contains(relative))
                    continue;

                clamped.Add(new Selection.BoundedTile(relative, identified[i].Included));
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
        private static List<PasteTile> CreatePasteTiles(
            Layer layer,
            IReadOnlyList<Selection.BoundedTile> identified,
            HashSet<Point> queuedWorldTiles,
            PasteTileResolver resolver,
            bool forClearQueue,
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
                if (layer.Match(in pasteTile))
                    continue;
                if (forClearQueue && !layer.CanClear(in pasteTile, in lookup, in context))
                    continue;
                if (!queuedWorldTiles.Add(pasteTile.World))
                    continue;
                pasteTiles.Add(pasteTile);
            }

            return pasteTiles;
        }

        /// <summary>
        /// Executes up to one step-sized batch of clear tasks.
        /// </summary>
        private static int ProcessClearQueue(Queue<ClearingTask> q, in PasteContext context, List<Point> currentStepTiles, HashSet<Point>? overlayDoneThisLayer)
        {
            // --- Dequeue a bounded batch, execute each task, and requeue incomplete work for future ticks.

            int success = 0;
            int n = Math.Min(context.PasteStepSize, q.Count);
            for (int i = 0; i < n; i++)
            {
                if (q.Count == 0) break;
                var task = q.Dequeue();
                task.AddTilesTo(currentStepTiles, MaxCurrentStepTiles);
                if (task.Execute(in context))
                {
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
                        ASMLog.Debug($"Requeue clear task world=({task.Tiles[0].World.X},{task.Tiles[0].World.Y}) remainingTiles={task.Tiles.Count}", "Paste");
                }
            }

            return success;
        }

        /// <summary>
        /// Executes up to one step-sized batch of place tasks.
        /// </summary>
        private static int ProcessPlaceQueue(Queue<PlacingTask> q, in PasteContext context, List<Point> currentStepTiles, HashSet<Point>? overlayDoneThisLayer)
        {
            // --- Dequeue a bounded batch, execute each task, and requeue incomplete work for future ticks.

            int success = 0;
            int n = Math.Min(context.PasteStepSize, q.Count);
            for (int i = 0; i < n; i++)
            {
                if (q.Count == 0) break;
                var task = q.Dequeue();
                task.AddTilesTo(currentStepTiles, MaxCurrentStepTiles);
                if (task.Execute(in context))
                {
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
                        ASMLog.Debug($"Requeue place task world=({task.Tiles[0].World.X},{task.Tiles[0].World.Y}) remainingTiles={task.Tiles.Count}", "Paste");
                }
            }

            return success;
        }

        /// <summary>
        /// Advances from clear to place phase and returns whether further queue work exists.
        /// </summary>
        private bool AdvancePhase()
        {
            // --- Reset layer index at phase boundaries and report whether the next phase has pending tasks.

            _currentLayerIndex = 0;
            if (_phase == Phase.Clear)
            {
                _phase = Phase.Place;
                ASMLog.Info("Stage: EXEC PLACE — running queued place tasks", "Paste");
                return HasAnyTask(placeTasks);
            }

            return false;
        }

        /// <summary>
        /// Moves the current layer pointer forward until it reaches a non-empty queue.
        /// </summary>
        private void AdvanceToNextNonEmptyLayer<T>(List<Queue<T>> queues)
        {
            // --- Skip exhausted layers so each step executes the next remaining work queue.

            while (_currentLayerIndex < queues.Count && queues[_currentLayerIndex].Count == 0)
                _currentLayerIndex++;
        }

        /// <summary>
        /// Clears all queued clear/place work for this operation.
        /// </summary>
        private void ClearAllTaskQueues()
        {
            // --- Reset both queue sets when operation exits via cheat sweep or invalid operation state.

            for (int i = 0; i < clearTasks.Count; i++)
                clearTasks[i].Clear();
            for (int i = 0; i < placeTasks.Count; i++)
                placeTasks[i].Clear();
        }

        /// <summary>
        /// Converts sweep-identified relatives into paste tiles that can be applied in one cheat pass.
        /// </summary>
        private static List<PasteTile> CreateSweepCheatPasteTiles(
            IReadOnlyList<Selection.BoundedTile> identified,
            in PasteTileLookup lookup)
        {
            // --- Resolve each identified relative through lookup and keep only resolvable paste tiles.

            var list = new List<PasteTile>(identified.Count);
            for (int i = 0; i < identified.Count; i++)
            {
                if (!lookup.TryGetPasteTile(identified[i].Relative, out PasteTile pasteTile))
                    continue;
                list.Add(pasteTile);
            }

            return list;
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
