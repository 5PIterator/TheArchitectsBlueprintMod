using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using ArchitectsStencilMod.Config;
using ArchitectsStencilMod.Core.Layers;
using ArchitectsStencilMod.Core.TileMapping;
using ArchitectsStencilMod.Debug;
using ArchitectsStencilMod.UI;
using ArchitectsStencilMod.Utils;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Global manager for all active paste operations.
    /// Owns the tick/update loop; per-projection state lives in <see cref="PastingOperation"/>.
    /// </summary>
    public sealed class PastingSystem : ModSystem
    {
        private readonly List<PastingOperation> _operations = [];

        /// <summary>
        /// Starts a paste operation for the given projection. Idempotent per projection (ignored if one is already running).
        /// </summary>
        public void StartPaste(Projection projection)
        {
            ArgumentNullException.ThrowIfNull(projection);

            if (!projection.IsDefined)
            {
                return;
            }

            for (int i = 0; i < _operations.Count; i++)
            {
                if (ReferenceEquals(_operations[i].Projection, projection))
                {
                    return;
                }
            }

            var (tileIds, wallIds) = CollectTileAndWallIds(projection);
            for (int i = 0; i < _operations.Count; i++)
            {
                var (opTiles, opWalls) = CollectTileAndWallIds(_operations[i].Projection);
                tileIds.UnionWith(opTiles);
                wallIds.UnionWith(opWalls);
            }

            ItrStorage.PrepareForPaste(tileIds, wallIds);

            var config = ModContent.GetInstance<PastingConfig>();
            var op = new PastingOperation(projection, config);
            if (!config.CheatSweep)
                op.BuildTasks();
            PasteTaskMapDebug.WriteTaskMap(projection, op.Pipeline, op.ClearTasks, op.PlaceTasks);
            if (op.IsCompleted)
            {
                return;
            }

            projection.IsLocked = true;
            _operations.Add(op);
        }

        private static (HashSet<int> TileIds, HashSet<int> WallIds) CollectTileAndWallIds(Projection projection)
        {
            var tileIds = new HashSet<int>();
            var wallIds = new HashSet<int>();

            if (projection is null || !projection.HasSourcePayload)
                return (tileIds, wallIds);

            for (int i = 0; i < projection.SourceAreaTiles.Count; i++)
            {
                var rel = projection.SourceAreaTiles[i].Relative;
                if (!projection.TryGetSourceTile(rel, out var tile))
                    continue;

                if (tile.HasTile)
                    tileIds.Add(tile.TileType);
                if (tile.WallType != WallID.None)
                    wallIds.Add(tile.WallType);
            }

            return (tileIds, wallIds);
        }

        /// <summary>
        /// Stops the paste operation for the given projection, if any.
        /// </summary>
        public void StopPaste(Projection projection)
        {
            ArgumentNullException.ThrowIfNull(projection);

            for (int i = _operations.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_operations[i].Projection, projection))
                {
                    _operations.RemoveAt(i);
                    projection.IsLocked = false;
                    if (_operations.Count == 0)
                        ItrStorage.Release();
                    break;
                }
            }
        }

        public override void OnWorldUnload()
        {
            for (int i = 0; i < _operations.Count; i++)
                _operations[i].Projection.IsLocked = false;

            _operations.Clear();
            ItrStorage.Release();
        }

        /// <summary>
        /// Returns true if there is an active paste operation for the given projection.
        /// </summary>
        internal bool HasActiveOperationFor(Projection projection)
        {
            if (projection is null) return false;
            for (int i = 0; i < _operations.Count; i++)
            {
                if (ReferenceEquals(_operations[i].Projection, projection))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if the given player has an active paste operation, with <paramref name="needsItems"/> indicating item consumption.
        /// </summary>
        internal bool TryGetActivePasteForPlayer(Player player, out bool needsItems)
        {
            needsItems = false;
            if (player is null) return false;

            for (int i = 0; i < _operations.Count; i++)
            {
                if (_operations[i].Projection.OwnerPlayerIndex == player.whoAmI)
                {
                    needsItems = _operations[i].Context.NeedsItems;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Pushes overlay content for all active paste operations owned by the player.
        /// Called by OverlaySystem each frame after ProcessOperations.
        /// </summary>
        internal void PushOverlay(Player player)
        {
            if (player is null) return;

            for (int i = 0; i < _operations.Count; i++)
            {
                if (_operations[i].Projection.OwnerPlayerIndex == player.whoAmI)
                    _operations[i].PushOverlay();
            }

            if (TryGetActivePasteForPlayer(player, out bool needsItems) && needsItems)
                PushInventoryLabel();
        }

        private static void PushInventoryLabel()
        {
            const float InventoryLabelScale = 0.85f;
            const int InventoryPaddingX = 8;
            const int InventoryPaddingY = 4;
            const int InventoryOffsetFromRight = 22;
            const int InventoryOffsetFromBottom = 340;

            string label = "Pasting from: Player inventory";
            var font = Terraria.GameContent.FontAssets.MouseText.Value;
            Overlay.DrawScreen(() =>
            {
                Vector2 size = font.MeasureString(label) * InventoryLabelScale;
                float x = Main.screenWidth - size.X - InventoryPaddingX * 2 - InventoryOffsetFromRight;
                float y = Main.screenHeight - size.Y - InventoryPaddingY * 2 - InventoryOffsetFromBottom;

                var bgRect = new Rectangle((int)x, (int)y, (int)(size.X + InventoryPaddingX * 2), (int)(size.Y + InventoryPaddingY * 2));
                var pixel = ModUtils.DummyTexture;
                Main.spriteBatch.Draw(pixel, bgRect, null, Color.Black * 0.5f, 0f, Vector2.Zero, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0f);

                Vector2 textPos = new(x + InventoryPaddingX, y + InventoryPaddingY);
                Terraria.Utils.DrawBorderStringFourWay(Main.spriteBatch, font, label, textPos.X, textPos.Y, Color.LimeGreen * 0.9f, Color.Black, Vector2.Zero, InventoryLabelScale);
            }, "pasting");
        }

        /// <inheritdoc />
        public override void PostUpdatePlayers()
        {
            ProcessOperations();
        }

        /// <summary>
        /// Performs one tick of all active paste operations.
        /// Operations whose owner holds the debug stick are skipped (debug stick drives them manually).
        /// </summary>
        internal void ProcessOperations()
        {
            if (Main.gameMenu || _operations.Count == 0)
            {
                return;
            }

            for (int i = _operations.Count - 1; i >= 0; i--)
            {
                var op = _operations[i];
                if (DebugStepper.IsHoldingDebugStick(op.Context.Player))
                {
                    continue;
                }

                if (op.Projection is null || !op.Projection.IsDefined)
                {
                    ASMLog.Debug("Paste ended: projection became invalid during update", "Paste");
                    op.Projection!.IsLocked = false;
                    _operations.RemoveAt(i);
                    if (_operations.Count == 0) ItrStorage.Release();
                    continue;
                }

                if (op.IsCompleted)
                {
                    ASMLog.Info($"Complete (cheatSweep={op.Context.CheatSweep})", "Paste");
                    ASMLog.Debug("Paste ended: operation completed during update", "Paste");
                    op.Projection.IsLocked = false;
                    _operations.RemoveAt(i);
                    if (_operations.Count == 0) ItrStorage.Release();
                    continue;
                }

                if (!op.Context.CheatSweep && Environment.TickCount64 < op.NextStepTime)
                {
                    continue;
                }

                bool keep = op.Context.CheatSweep ? op.Sweep() : op.Step();
                if (!keep)
                {
                    ASMLog.Debug("Paste ended: step returned no more work", "Paste");
                    op.Projection.IsLocked = false;
                    _operations.RemoveAt(i);
                    if (_operations.Count == 0) ItrStorage.Release();
                }
            }
        }

        /// <summary>
        /// Steps the operation for the given projection once. For debug mode (debug stick drives execution).
        /// Returns true if the operation has more work; false when done or not found.
        /// </summary>
        internal bool TryStepOperation(Projection projection)
        {
            ArgumentNullException.ThrowIfNull(projection);

            for (int i = 0; i < _operations.Count; i++)
            {
                if (!ReferenceEquals(_operations[i].Projection, projection))
                    continue;

                var op = _operations[i];
                if (op.Projection is null || !op.Projection.IsDefined)
                {
                    ASMLog.Debug("Paste ended: projection became invalid during manual step", "Paste");
                    op.Projection!.IsLocked = false;
                    _operations.RemoveAt(i);
                    if (_operations.Count == 0) ItrStorage.Release();
                    return false;
                }

                if (op.IsCompleted)
                {
                    ASMLog.Info($"Complete (cheatSweep={op.Context.CheatSweep})", "Paste");
                    ASMLog.Debug("Paste ended: operation completed during manual step", "Paste");
                    op.Projection.IsLocked = false;
                    _operations.RemoveAt(i);
                    if (_operations.Count == 0) ItrStorage.Release();
                    return false;
                }

                bool keep = op.Step();
                if (!keep)
                {
                    ASMLog.Debug("Paste ended: manual step returned no more work", "Paste");
                    op.Projection.IsLocked = false;
                    _operations.RemoveAt(i);
                    if (_operations.Count == 0) ItrStorage.Release();
                }
                return keep;
            }

            return false;
        }

        /// <summary>
        /// A single paste operation. Fire-and-discard; each instance builds its own task lists.
        /// </summary>
        internal sealed class PastingOperation
        {
            private enum Phase { Clear, Place }

            public Projection Projection { get; }
            public IReadOnlyList<Layer> CustomLayers { get; }
            public LayerPipeline Pipeline { get; }
            internal IReadOnlyList<Queue<ClearingTask>> ClearTasks => clearTasks;
            internal IReadOnlyList<Queue<PlacingTask>> PlaceTasks => placeTasks;
            public PasteContext Context { get; private set; } = default!;
            public PastingConfig PasteConfig { get; private set; } = default!;

            private readonly List<Queue<ClearingTask>> clearTasks = [];
            private readonly List<Queue<PlacingTask>> placeTasks = [];
            private readonly PasteTileDatabase _lookup;

            private Phase _phase;
            private int _currentLayerIndex;
            public long NextStepTime { get; private set; }

            private readonly List<Point> _currentStepClearTiles = [];
            private readonly List<Point> _currentStepPlaceTiles = [];

            /// <summary>
            /// Cheat-sweep runs without queued tasks until <see cref="Sweep"/> applies; do not treat empty queues as done while cheat is enabled.
            /// </summary>
            public bool IsCompleted =>
                !PasteConfig.CheatSweep && !HasAnyTask(clearTasks) && !HasAnyTask(placeTasks);

            private static bool HasAnyTask<T>(List<Queue<T>> queues)
            {
                for (int i = 0; i < queues.Count; i++)
                {
                    if (queues[i].Count > 0)
                    {
                        return true;
                    }
                }

                return false;
            }

            public PastingOperation(Projection projection, PastingConfig config)
            {
                Projection = projection ?? throw new ArgumentNullException(nameof(projection));
                if (Projection.OwnerPlayerIndex < 0 || Projection.OwnerPlayerIndex >= Main.maxPlayers)
                    throw new InvalidOperationException($"Projection has invalid owner index: {Projection.OwnerPlayerIndex}");
                if (!projection.HasSourcePayload)
                    throw new InvalidOperationException("Projection has no source payload. Recreate the projection.");

                PasteConfig = config;
                CustomLayers = CustomLayer.BuildCustomLayers();
                Pipeline = LayerSystem.ComposePipeline(CustomLayers);
                _lookup = BuildLookup();
                UpdateContext();
                ASMLog.Debug($"Paste started: {Projection.Kind}, bounds=({Projection.Bounds.X},{Projection.Bounds.Y}) {Projection.Bounds.Width}x{Projection.Bounds.Height}", "Paste");
                ASMLog.Info($"Started ({Projection.Kind}) | source tiles: {Projection.SourceAreaTiles.Count}, target tiles: {Projection.TargetAreaTiles.Count}", "Paste");
            }

            private PasteTileDatabase BuildLookup() => new(
                worldOrigin: Projection.Bounds.Location,
                cutWorldOrigin: Projection.SourceArea.Location,
                size: Projection.SourcePayloadSize,
                selectedTiles: Projection.SourceAreaTiles,
                sourceLookup: Projection.TryGetSourceTile,
                targetLookup: Projection.TryGetTargetTile,
                cutLookup: Projection.TryGetSourceAreaTile);

            /// <summary>
            /// Updates the context for the operation.
            /// </summary>
            public void UpdateContext()
            {
                Context = new PasteContext(
                    playerIndex: Projection.OwnerPlayerIndex,
                    slots: PastingInventory.GetConsumableSlots(Main.player[Projection.OwnerPlayerIndex]),
                    config: PasteConfig);
            }

            /// <summary>
            /// Executes one incremental step. Call when !CheatSweep. Returns false if operation should be removed (e.g. invalid owner).
            /// </summary>
            public bool Step()
            {
                UpdateContext();
                Player player = Context.Player;
                if (player is null || !player.active || player.dead)
                    return false;

                if (_phase == Phase.Place)
                {
                    AdvanceToNextNonEmptyLayer(placeTasks);
                    if (_currentLayerIndex >= placeTasks.Count)
                    {
                        if (!AdvancePhase()) return false;
                        return true;
                    }
                    var q = placeTasks[_currentLayerIndex];
                    var layer = Pipeline.PlaceOrder[_currentLayerIndex];
                    ASMLog.Info($"Place layer {layer.Id} (idx={_currentLayerIndex}) tasks={q.Count}", "Paste");
                    _currentStepPlaceTiles.Clear();
                    _currentStepClearTiles.Clear();
                    ProcessPlaceQueue(q, Context, _currentStepPlaceTiles);
                }
                else
                {
                    AdvanceToNextNonEmptyLayer(clearTasks);
                    if (_currentLayerIndex >= clearTasks.Count)
                    {
                        if (!AdvancePhase()) return false;
                        return true;
                    }
                    var q = clearTasks[_currentLayerIndex];
                    var layer = Pipeline.ClearOrder[_currentLayerIndex];
                    ASMLog.Info($"Clear layer {layer.Id} (idx={_currentLayerIndex}) tasks={q.Count}", "Paste");
                    _currentStepClearTiles.Clear();
                    _currentStepPlaceTiles.Clear();
                    ProcessClearQueue(q, Context, _currentStepClearTiles);
                }

                NextStepTime = Environment.TickCount64 + Context.PasteDelayMs;
                return !IsCompleted;
            }

            /// <summary>
            /// Cheat sweep: build lookup, sweep identify (full footprint), place once, clear queues. Always returns false to remove the operation.
            /// </summary>
            public bool Sweep()
            {
                UpdateContext();
                Player player = Context.Player;
                if (player is null || !player.active || player.dead)
                    return false;

                var sourceLookup = _lookup.CreateQuery(PasteIdentifyChannel.Source);

                Layer sweep = LayerSystem.Sweep;
                Point anchor = Projection.SourceAreaTiles.Count > 0
                    ? Projection.SourceAreaTiles[0].Relative
                    : Point.Zero;

                if (!sourceLookup.TryGetIdentify(anchor, out Tile anchorSourceTile))
                {
                    if (!sourceLookup.TryGetSource(anchor, out anchorSourceTile))
                    {
                        ClearAllTaskQueues();
                        NextStepTime = 0;
                        return false;
                    }
                }

                IReadOnlyList<Blueprint.BoundedTile> identified = sweep.Identify(anchor, in anchorSourceTile, in sourceLookup);
                if (identified.Count == 0)
                {
                    ClearAllTaskQueues();
                    NextStepTime = 0;
                    return false;
                }

                List<PasteTile> pasteTiles = CreateSweepCheatPasteTiles(sweep, identified, in sourceLookup);
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

            private void ClearAllTaskQueues()
            {
                for (int i = 0; i < clearTasks.Count; i++)
                    clearTasks[i].Clear();
                for (int i = 0; i < placeTasks.Count; i++)
                    placeTasks[i].Clear();
            }

            private static List<PasteTile> CreateSweepCheatPasteTiles(
                Layer layer,
                IReadOnlyList<Blueprint.BoundedTile> identified,
                in PasteLookupQuery lookup)
            {
                var list = new List<PasteTile>(identified.Count);
                for (int i = 0; i < identified.Count; i++)
                {
                    if (!lookup.TryCreatePasteTile(identified[i].Relative, out PasteTile pasteTile))
                        continue;
                    list.Add(pasteTile);
                }

                return list;
            }

            internal void PushOverlay()
            {
                const int MaxOverlayTiles = 128;
                Color pendingColor = _phase == Phase.Place ? (Color.LimeGreen * 0.35f) : (Color.Orange * 0.35f);
                int remaining = MaxOverlayTiles;

                if (_phase == Phase.Place)
                {
                    int idx = _currentLayerIndex;
                    while (idx < placeTasks.Count && placeTasks[idx].Count == 0) idx++;
                    if (idx < placeTasks.Count)
                    {
                        foreach (var task in placeTasks[idx])
                        {
                            if (remaining <= 0) break;
                            remaining -= task.PushOverlay(pendingColor, remaining);
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
                            remaining -= task.PushOverlay(pendingColor, remaining);
                        }
                    }
                }

                if (_currentStepClearTiles.Count > 0)
                    Overlay.DrawTile(_currentStepClearTiles, Color.Orange * 0.6f, "pasting");
                if (_currentStepPlaceTiles.Count > 0)
                    Overlay.DrawTile(_currentStepPlaceTiles, Color.LimeGreen * 0.6f, "pasting");
            }

            /// <summary>
            /// Builds the clear and place task queues from the projection's source vs target comparison.
            /// Walks source and target tile-by-tile, enqueueing clear tasks for removals and place tasks for additions.
            /// </summary>
            public void BuildTasks()
            {
                // --- Reset every queue, identify the source side first, and then queue clears only for relatives that
                // --- are allowed to place so missing items don't clear the world.

                clearTasks.Clear();
                placeTasks.Clear();

                for (int i = 0; i < Pipeline.ClearOrder.Count; i++)
                    clearTasks.Add(new Queue<ClearingTask>());
                for (int i = 0; i < Pipeline.PlaceOrder.Count; i++)
                    placeTasks.Add(new Queue<PlacingTask>());

                var queuedClearByLayer = new HashSet<Point>[Pipeline.ClearOrder.Count];
                var queuedPlaceByLayer = new HashSet<Point>[Pipeline.PlaceOrder.Count];
                for (int i = 0; i < queuedClearByLayer.Length; i++)
                    queuedClearByLayer[i] = [];
                for (int i = 0; i < queuedPlaceByLayer.Length; i++)
                    queuedPlaceByLayer[i] = [];

                var identifyContext = new PasteContext(Projection.OwnerPlayerIndex, null, PasteConfig);
                var planningContext = new PasteContext(
                    Projection.OwnerPlayerIndex,
                    PastingInventory.GetPlanningSlots(Main.player[Projection.OwnerPlayerIndex]),
                    PasteConfig);

                var sourceLookup = _lookup.CreateQuery(PasteIdentifyChannel.Source);
                var targetLookup = _lookup.CreateQuery(PasteIdentifyChannel.Target);
                var cutLookup = _lookup.CreateQuery(PasteIdentifyChannel.CutSource);

                var allowedPlaceRelatives = new HashSet<Point>();

                for (int i = 0; i < Projection.SourceAreaTiles.Count; i++)
                {
                    var bt = Projection.SourceAreaTiles[i];
                    Point rel = bt.Relative;
                    if (!Projection.TryGetSourceTile(rel, out Tile sourceTile))
                        continue;

                    QueuePlaceLayersForTile(
                        rel,
                        in sourceTile,
                        in sourceLookup,
                        in planningContext,
                        queuedPlaceByLayer,
                        allowedPlaceRelatives);
                }

                for (int i = 0; i < Projection.TargetAreaTiles.Count; i++)
                {
                    var bt = Projection.TargetAreaTiles[i];
                    Point rel = bt.Relative;
                    if (!allowedPlaceRelatives.Contains(rel) || !Projection.TryGetTargetTile(rel, out Tile targetTile))
                        continue;

                    QueueClearLayersForTile(
                        rel,
                        in targetTile,
                        in targetLookup,
                        in identifyContext,
                        queuedClearByLayer,
                        allowedPlaceRelatives);
                }

                if (Projection.Kind == ProjectionKind.Cut)
                {
                    for (int i = 0; i < Projection.SourceAreaTiles.Count; i++)
                    {
                        Point rel = Projection.SourceAreaTiles[i].Relative;
                        if (!allowedPlaceRelatives.Contains(rel) || !cutLookup.TryGetIdentify(rel, out Tile tileAtSource))
                            continue;

                        QueueClearLayersForTile(
                            rel,
                            in tileAtSource,
                            in cutLookup,
                            in identifyContext,
                            queuedClearByLayer,
                            allowedPlaceRelatives);
                    }
                }
            }

            /// <summary>
            /// Queues every clear layer that applies to the given tile by using the explicit identification hierarchy.
            /// </summary>
            private void QueueClearLayersForTile(
                Point rel,
                in Tile tile,
                in PasteLookupQuery lookup,
                in PasteContext identifyContext,
                HashSet<Point>[] queuedClearByLayer,
                HashSet<Point> allowedPlaceRelatives)
            {
                var identifiedLayers = LayerSystem.Identify(rel, in tile, lookup, Pipeline, identifyContext, PasteConfig);
                for (int i = 0; i < identifiedLayers.Count; i++)
                {
                    var identified = identifiedLayers[i];
                    ASMLog.Debug($"Identify clear layer={identified.Layer.Id} rel=({rel.X},{rel.Y}) tiles={identified.Tiles.Count}", "Paste");
                }

                for (int i = 0; i < identifiedLayers.Count; i++)
                {
                    var identified = identifiedLayers[i];
                    QueueClearLayer(identified.Layer, identified.Tiles, lookup, queuedClearByLayer, allowedPlaceRelatives);
                }
            }

            /// <summary>
            /// Queues every place layer that applies to the given tile by using the explicit identification hierarchy.
            /// </summary>
            private void QueuePlaceLayersForTile(
                Point rel,
                in Tile tile,
                in PasteLookupQuery lookup,
                in PasteContext identifyContext,
                HashSet<Point>[] queuedPlaceByLayer,
                HashSet<Point> allowedPlaceRelatives)
            {
                var identifiedLayers = LayerSystem.Identify(rel, in tile, lookup, Pipeline, identifyContext, PasteConfig);
                for (int i = 0; i < identifiedLayers.Count; i++)
                {
                    var identified = identifiedLayers[i];
                    ASMLog.Debug($"Identify place layer={identified.Layer.Id} rel=({rel.X},{rel.Y}) tiles={identified.Tiles.Count} canPlace={identified.CanPlace}", "Paste");
                }

                for (int i = 0; i < identifiedLayers.Count; i++)
                {
                    var identified = identifiedLayers[i];
                    var clamped = ClampIdentified(identified.Tiles, lookup);
                    if (clamped.Count == 0)
                        continue;

                    if (!identified.CanPlace && Context.ForceTiles)
                        continue;

                    for (int j = 0; j < clamped.Count; j++)
                        allowedPlaceRelatives.Add(clamped[j].Relative);

                    QueuePlaceLayer(identified.Layer, clamped, lookup, queuedPlaceByLayer);
                }
            }

            /// <summary>
            /// Queues a clear task from an already identified tile set.
            /// </summary>
            private void QueueClearLayer(
                Layer layer,
                IReadOnlyList<Blueprint.BoundedTile> identified,
                in PasteLookupQuery lookup,
                HashSet<Point>[] queuedClearByLayer,
                HashSet<Point> allowedPlaceRelatives)
            {
                // --- Convert identified relatives into world tiles, skip no-ops, then enqueue the clear task.

                int layerIndex = Pipeline.ToClearIndex(layer);
                if (layerIndex < 0)
                    return;

                var clamped = ClampIdentified(identified, lookup, allowedPlaceRelatives);
                if (clamped.Count == 0)
                    return;

                var tiles = CreatePasteTiles(layer, clamped, queuedClearByLayer[layerIndex], lookup);
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
                in PasteLookupQuery lookup,
                HashSet<Point>[] queuedPlaceByLayer)
            {
                // --- Convert identified relatives into world tiles, skip no-ops, then enqueue the place task.

                int layerIndex = Pipeline.ToPlaceIndex(layer);
                if (layerIndex < 0)
                    return;

                var tiles = CreatePasteTiles(layer, identified, queuedPlaceByLayer[layerIndex], lookup);
                if (tiles.Count > 0)
                {
                    placeTasks[layerIndex].Enqueue(new PlacingTask(layer, tiles));
                    ASMLog.Debug($"Queued place task layer={layer.Id} tiles={tiles.Count} queue={placeTasks[layerIndex].Count}", "Paste");
                }
            }

            private static List<Blueprint.BoundedTile> ClampIdentified(
                IReadOnlyList<Blueprint.BoundedTile> identified,
                in PasteLookupQuery lookup,
                HashSet<Point> allowedRelatives = null)
            {
                var clamped = new List<Blueprint.BoundedTile>(identified.Count);
                for (int i = 0; i < identified.Count; i++)
                {
                    Point relative = identified[i].Relative;
                    if (!lookup.IsInPasteScope(relative))
                        continue;
                    if (allowedRelatives is not null && !allowedRelatives.Contains(relative))
                        continue;

                    clamped.Add(new Blueprint.BoundedTile(relative, identified[i].Selected));
                }

                return clamped;
            }

            /// <summary>
            /// Builds the paste-tile payload for a single task while deduplicating world positions per layer.
            /// </summary>
            private static List<PasteTile> CreatePasteTiles(
                Layer layer,
                IReadOnlyList<Blueprint.BoundedTile> identified,
                HashSet<Point> queuedWorldTiles,
                in PasteLookupQuery lookup)
            {
                // --- Track only the positions newly claimed by this task so overlapping scans stay stable.

                var newWorldTiles = new HashSet<Point>();
                for (int i = 0; i < identified.Count; i++)
                {
                    Point frameRel = identified[i].Relative;
                    if (!lookup.TryCreatePasteTile(frameRel, out PasteTile pasteTile))
                        continue;

                    if (queuedWorldTiles.Add(pasteTile.World))
                        newWorldTiles.Add(pasteTile.World);
                }

                if (newWorldTiles.Count == 0)
                    return [];

                var pasteTiles = new List<PasteTile>(newWorldTiles.Count);
                for (int i = 0; i < identified.Count; i++)
                {
                    Point frameRel = identified[i].Relative;
                    if (!lookup.TryCreatePasteTile(frameRel, out PasteTile pasteTile) || !newWorldTiles.Contains(pasteTile.World))
                        continue;
                    if (layer.Match(in pasteTile))
                        continue;

                    pasteTiles.Add(pasteTile);
                }

                return pasteTiles;
            }

            private const int MaxCurrentStepTiles = 128;

            private static int ProcessClearQueue(Queue<ClearingTask> q, in PasteContext context, List<Point> currentStepTiles)
            {
                int success = 0;
                int n = Math.Min(context.PasteStepSize, q.Count);
                for (int i = 0; i < n; i++)
                {
                    if (q.Count == 0) break;
                    var task = q.Dequeue();
                    task.AddTilesTo(currentStepTiles, MaxCurrentStepTiles);
                    if (task.Execute(in context)) success++;
                    else
                    {
                        q.Enqueue(task);
                        if (task.Tiles.Count > 0)
                            ASMLog.Debug($"Requeue clear task world=({task.Tiles[0].X},{task.Tiles[0].Y}) remainingTiles={task.Tiles.Count}", "Paste");
                    }
                }
                return success;
            }

            private static int ProcessPlaceQueue(Queue<PlacingTask> q, in PasteContext context, List<Point> currentStepTiles)
            {
                int success = 0;
                int n = Math.Min(context.PasteStepSize, q.Count);
                for (int i = 0; i < n; i++)
                {
                    if (q.Count == 0) break;
                    var task = q.Dequeue();
                    task.AddTilesTo(currentStepTiles, MaxCurrentStepTiles);
                    if (task.Execute(in context)) success++;
                    else
                    {
                        q.Enqueue(task);
                        if (task.Tiles.Count > 0)
                            ASMLog.Debug($"Requeue place task world=({task.Tiles[0].X},{task.Tiles[0].Y}) remainingTiles={task.Tiles.Count}", "Paste");
                    }
                }
                return success;
            }

            private bool AdvancePhase()
            {
                _currentLayerIndex = 0;
                if (_phase == Phase.Clear)
                {
                    _phase = Phase.Place;
                    ASMLog.Info("Phase Clear -> Place", "Paste");
                    return HasAnyTask(placeTasks);
                }
                return false;
            }

            private void AdvanceToNextNonEmptyLayer<T>(List<Queue<T>> queues)
            {
                while (_currentLayerIndex < queues.Count && queues[_currentLayerIndex].Count == 0)
                {
                    _currentLayerIndex++;
                }
            }
        }
    }

}
