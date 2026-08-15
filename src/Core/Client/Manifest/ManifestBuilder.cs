using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core.Layers;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Builds a paste item manifest for the given projection without mutating world or inventory state.</summary>
    internal static class ManifestBuilder
    {
        private readonly record struct LayerWorldKey(string LayerId, int WorldX, int WorldY);

        /// <summary>
        /// Computes full payload totals, remaining destination demand, availability, and missing counts.
        /// </summary>
        public static Manifest Build(Projection projection)
        {
            // --- Ensure the interaction map is available, snapshot inventories, then merge demand into entries.

            if (projection is not { IsDefined: true, HasPayload: true })
                return new Manifest([], []);

            bool ownedLoad = EnsureInteractionMapForPlanning();
            try
            {
                List<Inventory> inventories = PasteInventorySources.Collect(projection);
                InventoryStackLedger ledger = InventoryStackLedger.FromInventories(inventories);
                PasteTileLookup lookup = PastePlanningLookup.Build(projection);
                LayerPipeline pipeline = LayerSystem.ComposePipeline();
                PastingConfig config = ModContent.GetInstance<PastingConfig>();
                PasteContext identifyContext = CreateIdentifyContext(projection, lookup);

                PasteDemandAccounting totalAccounting = new(ledger);
                PasteDemandAccounting remainingAccounting = new(ledger);
                CollectDemand(projection, lookup, pipeline, config, in identifyContext, skipMatched: false, totalAccounting);
                CollectDemand(projection, lookup, pipeline, config, in identifyContext, skipMatched: true, remainingAccounting);

                Dictionary<DemandPairKey, int> totalDemand = totalAccounting.BuildFinalPairs();
                Dictionary<DemandPairKey, int> remainingDemand = remainingAccounting.BuildFinalPairs();

                var pairKeys = new SortedSet<DemandPairKey>(DemandPairKeyComparer.Instance);
                foreach (DemandPairKey key in totalDemand.Keys)
                    pairKeys.Add(key);
                foreach (DemandPairKey key in remainingDemand.Keys)
                    pairKeys.Add(key);

                // Emit one entry per main+tool+ammo pair: direct, then ammo, then missing (ammo last).
                var satisfied = new List<ManifestEntry>(pairKeys.Count);
                var satisfiedAmmo = new List<ManifestEntry>();
                var missingEntries = new List<ManifestEntry>();
                var missingAmmo = new List<ManifestEntry>();
                foreach (DemandPairKey key in pairKeys)
                {
                    totalDemand.TryGetValue(key, out int total);
                    remainingDemand.TryGetValue(key, out int remaining);
                    int available = ledger.CountAvailable(key.ConsumableItemId);
                    int missing = Math.Max(0, remaining - available);

                    bool isAmmo = key.ToolItemId != key.ConsumableItemId;
                    int toolAvailable = 0;
                    int toolMissing = 0;
                    if (isAmmo)
                    {
                        toolAvailable = CountToolAvailable(ledger, key.ToolItemId);
                        toolMissing = toolAvailable > 0 ? 0 : 1;
                    }

                    var entry = new ManifestEntry(
                        key.MainName,
                        key.ToolItemId,
                        key.ConsumableItemId,
                        total,
                        available,
                        missing,
                        toolAvailable,
                        toolMissing,
                        ledger.GetContributingSources(key.ConsumableItemId));

                    bool isMissing = missing > 0 || toolMissing > 0;
                    if (isMissing && isAmmo)
                        missingAmmo.Add(entry);
                    else if (isMissing)
                        missingEntries.Add(entry);
                    else if (isAmmo)
                        satisfiedAmmo.Add(entry);
                    else
                        satisfied.Add(entry);
                }

                satisfied.AddRange(satisfiedAmmo);
                satisfied.AddRange(missingEntries);
                satisfied.AddRange(missingAmmo);
                var entries = satisfied;

                var unresolved = new List<string>();
                AppendUnique(unresolved, totalAccounting.Unresolved);
                AppendUnique(unresolved, remainingAccounting.Unresolved);
                unresolved.Sort(StringComparer.OrdinalIgnoreCase);
                return new Manifest(entries, unresolved);
            }
            finally
            {
                // Release only when Manifest loaded the map and no paste still needs it.
                if (ownedLoad && !HasActivePasteOperations())
                    ItrStorage.Release();
            }
        }

        /// <summary>
        /// Ensures InteractionMapProvider.Current holds the full compiled map for planning.
        /// </summary>
        private static bool EnsureInteractionMapForPlanning()
        {
            // --- Always reload unfiltered; a paste cache may omit walls/tiles this blueprint needs.

            if (!ItrStorage.CanLoadPasteInteractionMaps())
            {
                TABLog.Warn(
                    "Manifest demand cannot load CompiledMaps on this client; paint/wall lookups may be unresolved.",
                    TABLog.ManifestCategory);
                LogMapStatus(mapLoaded: false, ownedLoad: false, reusedExisting: false);
                return false;
            }

            // Null tile/wall filters load every layer, including Paint/Wire/Liquid.
            ItrStorage.PrepareForPaste(null, null);
            bool mapLoaded = InteractionMapProvider.Current is not null;
            LogMapStatus(mapLoaded, ownedLoad: mapLoaded, reusedExisting: false);
            return mapLoaded;
        }

        /// <summary>Counts ledger stacks for a tool id including any interaction alias ids.</summary>
        private static int CountToolAvailable(InventoryStackLedger ledger, int toolItemId)
        {
            // --- Prefer alias expansion from the first mapped interaction for this tool.

            IReadOnlyList<InteractionEntry> byItem = InteractionMapProvider.Current?.GetByItem(toolItemId) ?? [];
            if (byItem.Count > 0)
            {
                IReadOnlyList<int> toolIds = byItem[0].GetToolItemIds();
                int total = 0;
                for (int i = 0; i < toolIds.Count; i++)
                    total += ledger.CountAvailable(toolIds[i]);
                return total;
            }

            return ledger.CountAvailable(toolItemId);
        }

        /// <summary>True when PastingSystem still holds at least one in-flight paste operation.</summary>
        private static bool HasActivePasteOperations()
        {
            // --- Avoid releasing a full map out from under an active paste after Manifest planning.

            PastingSystem? pasting = ModContent.GetInstance<PastingSystem>();
            return pasting is not null && pasting.GetActiveOperationCount() > 0;
        }

        /// <summary>Logs once whether Manifest planning has a compiled interaction map.</summary>
        private static void LogMapStatus(bool mapLoaded, bool ownedLoad, bool reusedExisting)
        {
            // --- Emit under both Manifest and InteractionMap tags so either toggle surfaces the status.

            string message =
                $"Manifest map planning mapLoaded={mapLoaded} ownedLoad={ownedLoad} reusedExisting={reusedExisting}";
            TABLog.Debug(message, TABLog.ManifestCategory);
            TABLog.Debug(message, "InteractionMap");
        }

        private static PasteContext CreateIdentifyContext(Projection projection, PasteTileLookup lookup)
        {
            // --- Manifest planning ignores live inventory gates; demand recording owns item math.

            EffectivePastePolicy policy = EffectivePastePolicy.Resolve(
                ModContent.GetInstance<PastingConfig>(),
                PasteClientHints.FromClientConfig());
            return new PasteContext(
                playerIndex: projection.Owner,
                inventoryLookup: null,
                policy: policy,
                lookup: lookup,
                effectSession: null,
                interactionBudget: null);
        }

        private static void CollectDemand(
            Projection projection,
            PasteTileLookup lookup,
            LayerPipeline pipeline,
            PastingConfig config,
            in PasteContext identifyContext,
            bool skipMatched,
            PasteDemandAccounting accounting)
        {
            // --- Walk included payload cells, identify layers, and record demand units with batch dedup.

            var visitedCells = new HashSet<LayerWorldKey>();
            var visitedFrameOrigins = new HashSet<Point>();
            var visitedFluidPools = new HashSet<Point>();

            Blueprint.BlueprintCell[,] targetTiles = projection.TargetTiles;
            int width = targetTiles.GetLength(0);
            int height = targetTiles.GetLength(1);

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Blueprint.BlueprintCell targetCell = targetTiles[x, y];
                    if (!targetCell.Included)
                        continue;

                    Point rel = targetCell.Relative;
                    if (!lookup.TryGetPasteTile(rel, out PasteTile pasteTile))
                        continue;
                    if (pasteTile.PayloadCell.IsEmpty)
                        continue;

                    List<LayerIdentifyResult> identified = LayerSystem.Identify(
                        in pasteTile,
                        in lookup,
                        pipeline,
                        in identifyContext,
                        config,
                        PasteSubject.Payload);

                    for (int i = 0; i < identified.Count; i++)
                        RecordIdentifiedLayer(
                            identified[i],
                            in lookup,
                            skipMatched,
                            accounting,
                            visitedCells,
                            visitedFrameOrigins,
                            visitedFluidPools);
                }
            }
        }

        private static void RecordIdentifiedLayer(
            LayerIdentifyResult identified,
            in PasteTileLookup lookup,
            bool skipMatched,
            PasteDemandAccounting accounting,
            HashSet<LayerWorldKey> visitedCells,
            HashSet<Point> visitedFrameOrigins,
            HashSet<Point> visitedFluidPools)
        {
            Layer layer = identified.Layer;
            IReadOnlyList<Blueprint.BlueprintCell> cells = identified.Tiles;
            if (cells.Count == 0)
                return;

            if (layer.LayerType == LayerType.Liquid)
            {
                RecordFluidDemand(layer, cells, in lookup, skipMatched, accounting, visitedFluidPools);
                return;
            }

            if (layer.Id == "Frame" || IsFrameLikeCustom(layer))
            {
                RecordFrameDemand(layer, cells, in lookup, skipMatched, accounting, visitedFrameOrigins);
                return;
            }

            if (layer.Id == "Tree")
            {
                RecordTreeDemand(cells, in lookup, skipMatched, accounting, visitedCells);
                return;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                Blueprint.BlueprintCell cell = cells[i];
                if (!lookup.TryGetPasteTile(cell.Relative, out PasteTile pasteTile))
                    continue;

                var key = new LayerWorldKey(layer.Id, pasteTile.World.X, pasteTile.World.Y);
                if (!visitedCells.Add(key))
                    continue;

                if (skipMatched && layer.Match(in pasteTile))
                    continue;

                RecordCellDemand(layer, in pasteTile, skipMatched, accounting);
            }
        }

        /// <summary>Records Living Wood Wand demand for each unmatched tree/cactus segment using family ammo.</summary>
        private static void RecordTreeDemand(
            IReadOnlyList<Blueprint.BlueprintCell> cells,
            in PasteTileLookup lookup,
            bool skipMatched,
            PasteDemandAccounting accounting,
            HashSet<LayerWorldKey> visitedCells)
        {
            // --- Resolve ammo per segment so shared Trees tiles use their wiki style / wood family.
            for (int i = 0; i < cells.Count; i++)
            {
                Blueprint.BlueprintCell cell = cells[i];
                if (!lookup.TryGetPasteTile(cell.Relative, out PasteTile pasteTile))
                    continue;

                var key = new LayerWorldKey("Tree", pasteTile.World.X, pasteTile.World.Y);
                if (!visitedCells.Add(key))
                    continue;

                if (skipMatched && TreeLayer.Definition.Match(in pasteTile))
                    continue;

                if (!pasteTile.PayloadCell.HasTile())
                    continue;

                if (TreeLayer.TryResolveTreeInteraction(in pasteTile, lookup, out InteractionEntry? entry)
                    && entry is not null)
                {
                    accounting.RecordInteraction(entry);
                    continue;
                }

                ushort tileType = (ushort)pasteTile.PayloadCell.Block.Type;
                int style = pasteTile.PayloadCell.ResolvePlacementStyle();
                accounting.NoteUnresolvedTile(tileType, style);
            }
        }

        private static bool IsFrameLikeCustom(Layer layer) =>
            layer.Id is "Plant" && layer.LayerType == LayerType.Custom;

        private static void RecordFluidDemand(
            Layer layer,
            IReadOnlyList<Blueprint.BlueprintCell> cells,
            in PasteTileLookup lookup,
            bool skipMatched,
            PasteDemandAccounting accounting,
            HashSet<Point> visitedFluidPools)
        {
            // --- Count unmatched pool cells once per pool anchor and spend liquid yield per cell.

            var changed = new List<PasteTile>(cells.Count);
            int liquidType = -1;

            for (int i = 0; i < cells.Count; i++)
            {
                Blueprint.BlueprintCell cell = cells[i];
                if (!lookup.TryGetPasteTile(cell.Relative, out PasteTile pasteTile))
                    continue;
                if (!visitedFluidPools.Add(pasteTile.World))
                    continue;
                if (skipMatched && layer.Match(in pasteTile))
                    continue;

                PayloadCell.LiquidData liquid = pasteTile.PayloadCell.Liquid;
                if (liquid.Amount <= 0)
                    continue;

                liquidType = liquid.Type;
                changed.Add(pasteTile);
            }

            if (changed.Count == 0 || liquidType < 0)
                return;

            accounting.RecordLiquidPool(liquidType, changed.Count);
        }

        private static void RecordFrameDemand(
            Layer layer,
            IReadOnlyList<Blueprint.BlueprintCell> cells,
            in PasteTileLookup lookup,
            bool skipMatched,
            PasteDemandAccounting accounting,
            HashSet<Point> visitedFrameOrigins)
        {
            // --- One item per logical frame object origin among unmatched footprint cells.

            for (int i = 0; i < cells.Count; i++)
            {
                Blueprint.BlueprintCell cell = cells[i];
                if (!lookup.TryGetPasteTile(cell.Relative, out PasteTile pasteTile))
                    continue;
                if (skipMatched && layer.Match(in pasteTile))
                    continue;

                Point origin = FrameOperations.ResolveOriginWorld(in pasteTile, forClear: false);
                if (!visitedFrameOrigins.Add(origin))
                    continue;

                if (!pasteTile.PayloadCell.HasTile())
                    continue;

                ushort tileType = (ushort)pasteTile.PayloadCell.Block.Type;
                int style = pasteTile.PayloadCell.ResolvePlacementStyle();
                accounting.RecordTilePlacement(tileType, style, pasteTile.World.X, pasteTile.World.Y, ignoreWorldState: !skipMatched);
            }
        }

        private static void RecordCellDemand(
            Layer layer,
            in PasteTile pasteTile,
            bool skipMatched,
            PasteDemandAccounting accounting)
        {
            // --- Dispatch by layer family so wire/paint/wall/tile deltas match paste reservation units.

            bool ignoreWorld = !skipMatched;

            switch (layer.LayerType)
            {
                case LayerType.Wall:
                    RecordWallDemand(in pasteTile, ignoreWorld, accounting);
                    break;
                case LayerType.Wiring:
                    RecordWiringDemand(in pasteTile, ignoreWorld, accounting);
                    break;
                case LayerType.Paint:
                    RecordPaintDemand(in pasteTile, ignoreWorld, accounting);
                    break;
                case LayerType.Detail:
                    break;
                default:
                    RecordTileCellDemand(in pasteTile, ignoreWorld, accounting);
                    break;
            }
        }

        private static void RecordWallDemand(in PasteTile pasteTile, bool ignoreWorld, PasteDemandAccounting accounting)
        {
            ushort wallType = (ushort)pasteTile.PayloadCell.Wall.Type;
            if (wallType == WallID.None)
                return;
            if (!ignoreWorld && pasteTile.Target.WallType == wallType)
                return;

            accounting.RecordWallPlacement(wallType);
        }

        private static void RecordWiringDemand(in PasteTile pasteTile, bool ignoreWorld, PasteDemandAccounting accounting)
        {
            PayloadCell.WireData intent = pasteTile.PayloadCell.Wire;
            Tile target = ignoreWorld ? default : pasteTile.Target;

            if (intent.RedWire && (ignoreWorld || !target.RedWire))
                accounting.RecordWireKind(0);
            if (intent.GreenWire && (ignoreWorld || !target.GreenWire))
                accounting.RecordWireKind(1);
            if (intent.BlueWire && (ignoreWorld || !target.BlueWire))
                accounting.RecordWireKind(2);
            if (intent.YellowWire && (ignoreWorld || !target.YellowWire))
                accounting.RecordWireKind(3);
            if (intent.HasActuator && (ignoreWorld || !target.HasActuator))
                accounting.RecordWireKind(4);
        }

        private static void RecordPaintDemand(in PasteTile pasteTile, bool ignoreWorld, PasteDemandAccounting accounting)
        {
            // --- Demand paint colors and coatings like colors: payload wants them and target facet is unmatched.

            PayloadCell.PaintData intent = pasteTile.PayloadCell.Paint;
            Tile target = ignoreWorld ? default : pasteTile.Target;

            if (intent.TileColor != PaintID.None && (ignoreWorld || target.TileColor != intent.TileColor))
                accounting.RecordPaintKind(intent.TileColor);
            if (intent.WallColor != PaintID.None && (ignoreWorld || target.WallColor != intent.WallColor))
                accounting.RecordPaintKind(intent.WallColor);

            // Do not require a live tile/wall here; empty destinations still need coating materials for paste.
            if (intent.TileInvisible && (ignoreWorld || !target.IsTileInvisible)
                || intent.WallInvisible && (ignoreWorld || !target.IsWallInvisible))
                accounting.RecordPaintKind(ContentNameResolver.EchoCoatingId);

            if (intent.TileFullbright && (ignoreWorld || !target.IsTileFullbright)
                || intent.WallFullbright && (ignoreWorld || !target.IsWallFullbright))
                accounting.RecordPaintKind(ContentNameResolver.IlluminantCoatingId);
        }

        private static void RecordTileCellDemand(in PasteTile pasteTile, bool ignoreWorld, PasteDemandAccounting accounting)
        {
            if (!pasteTile.PayloadCell.HasTile())
                return;

            ushort tileType = (ushort)pasteTile.PayloadCell.Block.Type;
            int style = pasteTile.PayloadCell.ResolvePlacementStyle();
            if (!ignoreWorld && pasteTile.Target.HasTile && pasteTile.Target.TileType == tileType)
                return;

            accounting.RecordTilePlacement(tileType, style, pasteTile.World.X, pasteTile.World.Y, ignoreWorld);
        }

        private static void AppendUnique(List<string> target, IReadOnlyList<string> source)
        {
            for (int i = 0; i < source.Count; i++)
            {
                string value = source[i];
                if (!target.Contains(value))
                    target.Add(value);
            }
        }
    }
}
