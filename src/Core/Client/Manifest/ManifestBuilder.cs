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

                Dictionary<int, int> totalDemand = totalAccounting.BuildFinalDemand();
                Dictionary<int, int> remainingDemand = remainingAccounting.BuildFinalDemand();

                var itemIds = new SortedSet<int>();
                foreach (int itemId in totalDemand.Keys)
                    itemIds.Add(itemId);
                foreach (int itemId in remainingDemand.Keys)
                    itemIds.Add(itemId);

                var entries = new List<ManifestEntry>(itemIds.Count);
                foreach (int itemId in itemIds)
                {
                    totalDemand.TryGetValue(itemId, out int total);
                    remainingDemand.TryGetValue(itemId, out int remaining);
                    int available = ledger.CountAvailable(itemId);
                    int missing = Math.Max(0, remaining - available);
                    entries.Add(new ManifestEntry(
                        itemId,
                        total,
                        available,
                        missing,
                        ledger.GetContributingSources(itemId)));
                }

                var unresolved = new List<string>();
                AppendUnique(unresolved, totalAccounting.Unresolved);
                AppendUnique(unresolved, remainingAccounting.Unresolved);
                return new Manifest(entries, unresolved);
            }
            finally
            {
                // Only release a map Manifest itself loaded; keep an in-progress paste cache intact.
                if (ownedLoad)
                    ItrStorage.Release();
            }
        }

        /// <summary>
        /// Ensures InteractionMapProvider.Current is set for planning; returns true when Manifest owns the load.
        /// </summary>
        private static bool EnsureInteractionMapForPlanning()
        {
            // --- Reuse a paste-loaded cache; otherwise load the full compiled map when filesystem access is allowed.

            bool alreadyLoaded = InteractionMapProvider.Current is not null;
            if (alreadyLoaded)
            {
                LogMapStatus(mapLoaded: true, ownedLoad: false, reusedExisting: true);
                return false;
            }

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
            PayloadCell.PaintData intent = pasteTile.PayloadCell.Paint;
            Tile target = ignoreWorld ? default : pasteTile.Target;

            if (intent.TileColor != PaintID.None && (ignoreWorld || target.TileColor != intent.TileColor))
                accounting.RecordPaintKind(intent.TileColor);
            if (intent.WallColor != PaintID.None && (ignoreWorld || target.WallColor != intent.WallColor))
                accounting.RecordPaintKind(intent.WallColor);

            // Record coating tools when echo/illuminant flags still need applying.
            bool needsEcho = ignoreWorld
                ? (intent.TileInvisible || intent.WallInvisible)
                : ((intent.TileInvisible && target.HasTile && !target.IsTileInvisible)
                    || (intent.WallInvisible && target.WallType != WallID.None && !target.IsWallInvisible));
            if (needsEcho)
                accounting.RecordPaintKind(ContentNameResolver.EchoCoatingId);

            bool needsIlluminant = ignoreWorld
                ? (intent.TileFullbright || intent.WallFullbright)
                : ((intent.TileFullbright && target.HasTile && !target.IsTileFullbright)
                    || (intent.WallFullbright && target.WallType != WallID.None && !target.IsWallFullbright));
            if (needsIlluminant)
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
