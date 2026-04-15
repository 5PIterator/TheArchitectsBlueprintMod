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
        private enum Phase { Prepare, Clear, Place }

        /// <summary>Sub-stages while <see cref="Phase.Prepare"/> builds task queues incrementally.</summary>
        private enum PrepWorkStage
        {
            PlaceSource = 0,
            ClearTarget = 1,
            SourceAreaClear = 2,
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
        private readonly PasteTileLookup _lookup;
        private const int MaxCurrentStepTiles = 128;
        /// <summary>Tiles of queue-building work per <see cref="Step"/> while in <see cref="Phase.Prepare"/>.</summary>
        private const int PreparePhaseTileBudget = 50;

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

        public PastingOperation(Projection projection, PastingConfig config, ClientPreferences clientPaste)
        {
            // --- Validate projection ownership/payload, then initialize layer pipeline and tile lookup.

            Projection = projection ?? throw new ArgumentNullException(nameof(projection));
            if (Projection.OwnerPlayerIndex < 0 || Projection.OwnerPlayerIndex >= Main.maxPlayers)
                throw new InvalidOperationException($"Projection has invalid owner index: {Projection.OwnerPlayerIndex}");
            if (!projection.HasPayload)
                throw new InvalidOperationException("Projection has no source payload. Recreate the projection.");

            PasteConfig = config;
            _clientPaste = clientPaste;
            ApplySourceClear = (Projection.CutSource && !Projection.SourceArea.IsEmpty) || config.EnableCutPasteProcess;
            Pipeline = LayerSystem.ComposePipeline();
            _lookup = BuildLookup();
            UpdateContext();
            ASMLog.Debug(
                $"Paste started: sourceClear={ApplySourceClear} (projection={Projection.CutSource}, debug={config.EnableCutPasteProcess}), targetArea=({Projection.Bounds.X},{Projection.Bounds.Y}) {Projection.Bounds.Width}x{Projection.Bounds.Height}",
                "Paste");
            ASMLog.Info(
                $"START | area=({Projection.Bounds.X},{Projection.Bounds.Y}) {Projection.Bounds.Width}x{Projection.Bounds.Height} | sourceClear={ApplySourceClear} | srcTiles={Projection.SourceTiles.Count} tgtTiles={Projection.TargetTiles.Count}",
                "Paste");

            _phase = config.CheatSweep ? Phase.Clear : Phase.Prepare;
            _prepWork = PrepWorkStage.PlaceSource;
            _prepIndex = 0;
            _preparationComplete = config.CheatSweep;
            if (_phase == Phase.Prepare)
                ASMLog.Info("Stage: PREP (building queues: PlaceSource, then ClearTarget, then optional SourceAreaClear)", "Paste");
            else
                ASMLog.Info("Stage: CLEAR (cheat sweep — preparation skipped)", "Paste");
        }

        /// <summary>
        /// Refreshes paste context from the owner player and inventory immediately before a step or sweep runs.
        /// </summary>
        public void UpdateContext()
        {
            // --- Rebuild context right before a step/sweep so slots and player reference match current world state.

            Context = new PasteContext(
                playerIndex: Projection.OwnerPlayerIndex,
                slots: PastingInventory.GetPasteInventorySlots(Main.player[Projection.OwnerPlayerIndex]),
                serverConfig: PasteConfig,
                clientPrefs: _clientPaste);
        }

        /// <summary>
        /// Initializes this operation’s task queues and per-layer dedup sets.
        /// </summary>
        private bool Init()
        {
            // --- Match legacy BuildTasks ordering: place planning, target clears, optional source-area clears.

            UpdateContext();
            int budget = PreparePhaseTileBudget;
            int prepUnitsThisCall = 0;

            if (_queuedClearByLayer is null)
            {
                clearTasks.Clear();
                placeTasks.Clear();
                for (int i = 0; i < Pipeline.ClearOrder.Count; i++)
                    clearTasks.Add(new Queue<ClearingTask>());
                for (int i = 0; i < Pipeline.PlaceOrder.Count; i++)
                    placeTasks.Add(new Queue<PlacingTask>());

                _queuedClearByLayer = new HashSet<Point>[Pipeline.ClearOrder.Count];
                _queuedPlaceByLayer = new HashSet<Point>[Pipeline.PlaceOrder.Count];
                for (int i = 0; i < _queuedClearByLayer.Length; i++)
                    _queuedClearByLayer[i] = [];
                for (int i = 0; i < _queuedPlaceByLayer.Length; i++)
                    _queuedPlaceByLayer[i] = [];
                _allowedPlaceRelatives = [];
                _prepWork = PrepWorkStage.PlaceSource;
                _prepIndex = 0;
                ASMLog.Info("Prep: PlaceSource — queuing place tasks from source tiles", "Paste");
                ASMLog.Debug(
                    $"Prep: begin prepTileBudget={PreparePhaseTileBudget} sourceTiles={Projection.SourceTiles.Count} lookupTiles={_lookup.Count} targetTiles={Projection.TargetTiles.Count} transparent={_clientPaste.TransparentPaste} sourceAreaClear={ApplySourceClear}",
                    "Paste");
            }

            var identifyContext = new PasteContext(Projection.OwnerPlayerIndex, null, PasteConfig, _clientPaste);
            var planningContext = new PasteContext(
                Projection.OwnerPlayerIndex,
                PastingInventory.GetPlanningSlots(Main.player[Projection.OwnerPlayerIndex]),
                PasteConfig,
                _clientPaste);

            var qClear = _queuedClearByLayer!;
            var qPlace = _queuedPlaceByLayer!;
            var allowed = _allowedPlaceRelatives!;

            while (budget > 0 && _prepWork < PrepWorkStage.Done)
            {
                switch (_prepWork)
                {
                    case PrepWorkStage.PlaceSource:
                        if (_prepIndex >= Projection.SourceTiles.Count)
                        {
                            _prepWork = PrepWorkStage.ClearTarget;
                            _prepIndex = 0;
                            ASMLog.Info("Prep: ClearTarget — queuing clears at destination", "Paste");
                            ASMLog.Debug("Prep: stage PlaceSource -> ClearTarget", "Paste");
                            continue;
                        }

                        Point relPlace = Projection.SourceTiles[_prepIndex].Relative;
                        _prepIndex++;
                        if (!_lookup.TryGetPasteTile(relPlace, out PasteTile pasteTilePlace))
                            continue;

                        QueuePlaceLayersForTile(relPlace, in pasteTilePlace, in _lookup, in planningContext, qPlace, allowed);
                        budget--;
                        prepUnitsThisCall++;
                        break;

                    case PrepWorkStage.ClearTarget:
                        if (_clientPaste.TransparentPaste)
                        {
                            if (_prepIndex >= Projection.TargetTiles.Count)
                            {
                                _prepWork = PrepWorkStage.SourceAreaClear;
                                _prepIndex = 0;
                                if (ApplySourceClear)
                                    ASMLog.Info("Prep: SourceAreaClear — queuing cuts at source (transparent)", "Paste");
                                else
                                    ASMLog.Info("Prep: no source cut — finalizing queues", "Paste");
                                ASMLog.Debug("Prep: stage ClearTarget -> SourceAreaClear (transparent)", "Paste");
                                continue;
                            }

                            Point relT = Projection.TargetTiles[_prepIndex].Relative;
                            _prepIndex++;
                            if (!allowed.Contains(relT))
                                continue;
                            if (!_lookup.TryGetPasteTile(relT, out PasteTile pasteTileT))
                                continue;

                            QueueClearLayersForTile(
                                relT,
                                in pasteTileT,
                                in _lookup,
                                in identifyContext,
                                qClear,
                                allowed,
                                useSourceAreaWorld: false);
                            budget--;
                            prepUnitsThisCall++;
                        }
                        else
                        {
                            if (_prepIndex >= _lookup.Count)
                            {
                                _prepWork = PrepWorkStage.SourceAreaClear;
                                _prepIndex = 0;
                                if (ApplySourceClear)
                                    ASMLog.Info("Prep: SourceAreaClear — queuing cuts at source (opaque)", "Paste");
                                else
                                    ASMLog.Info("Prep: no source cut — finalizing queues", "Paste");
                                ASMLog.Debug("Prep: stage ClearTarget -> SourceAreaClear (opaque)", "Paste");
                                continue;
                            }

                            PasteTile pasteTileO = _lookup.PasteTiles[_prepIndex];
                            _prepIndex++;
                            QueueClearLayersForTile(
                                pasteTileO.Relative,
                                in pasteTileO,
                                in _lookup,
                                in identifyContext,
                                qClear,
                                restrictClearRelatives: null,
                                useSourceAreaWorld: false);
                            budget--;
                            prepUnitsThisCall++;
                        }
                        break;

                    case PrepWorkStage.SourceAreaClear:
                        if (!ApplySourceClear)
                        {
                            _prepWork = PrepWorkStage.Done;
                            ASMLog.Debug("Prep: stage SourceAreaClear skipped (no source clear) -> Done", "Paste");
                            continue;
                        }

                        if (_prepIndex >= Projection.SourceTiles.Count)
                        {
                            _prepWork = PrepWorkStage.Done;
                            ASMLog.Info("Prep: done — source-area clear pass finished", "Paste");
                            ASMLog.Debug("Prep: stage SourceAreaClear -> Done", "Paste");
                            continue;
                        }

                        Point relS = Projection.SourceTiles[_prepIndex].Relative;
                        _prepIndex++;
                        if (!allowed.Contains(relS) || !TryGetSourceAreaPasteTile(relS, out PasteTile pasteAtSource))
                            continue;

                        QueueClearLayersForTile(
                            relS,
                            in pasteAtSource,
                            in _lookup,
                            in identifyContext,
                            qClear,
                            allowed,
                            useSourceAreaWorld: true);
                        budget--;
                        prepUnitsThisCall++;
                        break;

                    default:
                        _prepWork = PrepWorkStage.Done;
                        break;
                }
            }

            if (_prepWork < PrepWorkStage.Done)
            {
                ASMLog.Debug(
                    $"Prep: batch stage={_prepWork} progress={FormatPrepProgress()} unitsThisFrame={prepUnitsThisCall} remainingBudget={budget}",
                    "Paste");
                return true;
            }

            int placeBatches = 0;
            for (int i = 0; i < placeTasks.Count; i++)
                placeBatches += placeTasks[i].Count;
            int clearBatches = 0;
            for (int i = 0; i < clearTasks.Count; i++)
                clearBatches += clearTasks[i].Count;
            ASMLog.Info($"Prep: queues ready | placeBatches={placeBatches} clearBatches={clearBatches}", "Paste");
            ASMLog.Debug($"Prep: complete placeBatches={placeBatches} clearBatches={clearBatches}", "Paste");

            _preparationComplete = true;
            _queuedClearByLayer = null;
            _queuedPlaceByLayer = null;
            _allowedPlaceRelatives = null;
            PasteTaskMapDebug.WriteTaskMap(Projection, ApplySourceClear, Pipeline, clearTasks, placeTasks);
            return false;
        }

        private string FormatPrepProgress()
        {
            return _prepWork switch
            {
                PrepWorkStage.PlaceSource =>
                    $"{Math.Min(_prepIndex, Projection.SourceTiles.Count)}/{Projection.SourceTiles.Count}",
                PrepWorkStage.ClearTarget => _clientPaste.TransparentPaste
                    ? $"{Math.Min(_prepIndex, Projection.TargetTiles.Count)}/{Projection.TargetTiles.Count}"
                    : $"{Math.Min(_prepIndex, _lookup.Count)}/{_lookup.Count}",
                PrepWorkStage.SourceAreaClear =>
                    $"{Math.Min(_prepIndex, Projection.SourceTiles.Count)}/{Projection.SourceTiles.Count}",
                PrepWorkStage.Done => "complete",
                _ => "?"
            };
        }

        /// <summary>
        /// Executes one incremental queue step and returns whether additional work remains.
        /// </summary>
        public bool Step()
        {
            // --- Process current phase queue and schedule the next allowed step time (context refreshed by PastingSystem).

            Player player = Context.Player;
            if (player is null || !player.active || player.dead)
                return false;

            if (_phase == Phase.Prepare)
            {
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
            }

            if (_phase == Phase.Place)
            {
                AdvanceToNextNonEmptyLayer(placeTasks);
                if (_currentLayerIndex >= placeTasks.Count)
                {
                    bool keep = AdvancePhase();
                    return keep;
                }

                SyncOverlayDoneTrackingIfNeeded();
                var q = placeTasks[_currentLayerIndex];
                var layer = Pipeline.PlaceOrder[_currentLayerIndex];
                ASMLog.Debug($"Place layer {layer.Id} (idx={_currentLayerIndex}) tasks={q.Count}", "Paste");
                _currentStepPlaceTiles.Clear();
                _currentStepClearTiles.Clear();
                ProcessPlaceQueue(q, Context, _currentStepPlaceTiles, _overlayDoneThisLayer);
            }
            else
            {
                AdvanceToNextNonEmptyLayer(clearTasks);
                if (_currentLayerIndex >= clearTasks.Count)
                {
                    bool keep = AdvancePhase();
                    return keep;
                }

                SyncOverlayDoneTrackingIfNeeded();
                var q = clearTasks[_currentLayerIndex];
                var layer = Pipeline.ClearOrder[_currentLayerIndex];
                ASMLog.Debug($"Clear layer {layer.Id} (idx={_currentLayerIndex}) tasks={q.Count}", "Paste");
                _currentStepClearTiles.Clear();
                _currentStepPlaceTiles.Clear();
                ProcessClearQueue(q, Context, _currentStepClearTiles, _overlayDoneThisLayer);
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
            if (_lookup.PasteTiles.Count == 0)
            {
                ClearAllTaskQueues();
                NextStepTime = 0;
                return false;
            }

            PasteTile anchorPasteTile = _lookup.PasteTiles[0];
            IReadOnlyList<Blueprint.BoundedTile> identified = sweep.Identify(in anchorPasteTile, in _lookup);
            if (identified.Count == 0)
            {
                ClearAllTaskQueues();
                NextStepTime = 0;
                return false;
            }

            List<PasteTile> pasteTiles = CreateSweepCheatPasteTiles(identified, in _lookup);
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
        /// Called when this operation leaves <see cref="PastingSystem"/> (completion, cancel, or invalid projection).
        /// Unlocks <see cref="Projection"/>.
        /// </summary>
        internal void Teardown()
        {
            Projection.Unlock();
        }

        /// <summary>
        /// Creates a lookup that pairs each source-relative tile with its source and target world state.
        /// </summary>
        private PasteTileLookup BuildLookup()
        {
            // --- Full source payload grid (WxH): opaque clears and layer identify need deselected / empty cells too.

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
                    if (!Projection.TryGetPayloadTile(rel, out Tile payload))
                        continue;
                    // Validate source
                    if (!Projection.TryGetSourceTile(rel, out Tile sourceTile))
                        continue;

                    if (!Projection.TryGetTargetTile(rel, out Tile targetTile))
                        continue;

                    Point world = Projection.RelativeToWorld(rel);
                    InteractionEntry interaction = ResolveInteraction(in payload);
                    list.Add(new PasteTile(
                        payload: payload,
                        interaction: interaction,
                        source: sourceTile,
                        target: targetTile,
                        relative: rel,
                        world: world));

                    // #region agent log
                    if (w * h <= 64)
                    {
                        Projection.TryGetPayloadData(rel, out var pf);
                        AgentNdjsonLog.Write(
                            "H2",
                            "PastingOperation.BuildLookup",
                            "lookup_entry",
                            new
                            {
                                netMode = Main.netMode,
                                relX = rel.X,
                                relY = rel.Y,
                                worldX = world.X,
                                worldY = world.Y,
                                facetRefNull = pf is null,
                                facetBlockType = pf?.Block?.TileType,
                                payloadHas = payload.HasTile,
                                payloadType = payload.HasTile ? payload.TileType : (ushort?)null,
                                srcHas = sourceTile.HasTile,
                                srcType = sourceTile.HasTile ? sourceTile.TileType : (ushort?)null,
                            });
                    }
                    // #endregion
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
                int style = TileObjectData.GetTileStyle(sourceTile);
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
            if (!_lookup.TryGetPasteTile(rel, out PasteTile baseTile))
                return false;
            if (!Projection.TryGetSourceTile(rel, out Tile sourceAreaTile))
                return false;

            Point world = new(Projection.SourceArea.X + rel.X, Projection.SourceArea.Y + rel.Y);
            Tile clearedIntent = new Tile();
            tile = new PasteTile(
                payload: baseTile.Payload,
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
            IReadOnlyList<Blueprint.BoundedTile> identified,
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
            IReadOnlyList<Blueprint.BoundedTile> identified,
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
        private static List<Blueprint.BoundedTile> ClampIdentified(
            IReadOnlyList<Blueprint.BoundedTile> identified,
            HashSet<Point> allowedRelatives = null)
        {
            // --- Keep only allowed relatives (when provided) and copy selected flags into a compact list.

            var clamped = new List<Blueprint.BoundedTile>(identified.Count);
            for (int i = 0; i < identified.Count; i++)
            {
                Point relative = identified[i].Relative;
                if (allowedRelatives is not null && !allowedRelatives.Contains(relative))
                    continue;

                clamped.Add(new Blueprint.BoundedTile(relative, identified[i].Selected));
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
            IReadOnlyList<Blueprint.BoundedTile> identified,
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
            IReadOnlyList<Blueprint.BoundedTile> identified,
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
