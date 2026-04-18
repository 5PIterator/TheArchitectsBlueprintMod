using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ArchitectsStencilMod.Config;
using ArchitectsStencilMod.Core;
using ArchitectsStencilMod.Debug;
using Microsoft.Xna.Framework;
using Terraria;

namespace ArchitectsStencilMod.Core.Layers
{
    /// <summary>
    /// Result of a layer's Clear or Place action. Success indicates the operation completed;
    /// AffectedTiles lists world positions that were modified (for deduplicating frame tasks).
    /// RemainingTiles: tiles that still need to be placed/cleared; non-empty means the task should run again.
    /// </summary>
    internal readonly struct LayerResult(bool success, IReadOnlyList<Point> affectedTiles, IReadOnlyList<PasteTile>? remainingTiles)
    {
        public readonly bool Success = success;
        public readonly IReadOnlyList<Point> AffectedTiles = affectedTiles ?? Array.Empty<Point>();
        public readonly IReadOnlyList<PasteTile> RemainingTiles = remainingTiles ?? Array.Empty<PasteTile>();

        public static LayerResult Ok(Point affected) => new(true, [affected], null);
        public static LayerResult Ok(IReadOnlyList<Point> affected) => new(true, affected ?? [], null);
        public static LayerResult Ok() => new(true, [], null);
        public static LayerResult OkWithRemaining(IReadOnlyList<PasteTile> remaining) => new(true, [], remaining);
        public static LayerResult Ok(Point affected, IReadOnlyList<PasteTile> remaining) => new(true, [affected], remaining);
        public static LayerResult Fail(Point attempted) => new(false, [attempted], null);
        public static LayerResult Fail() => new(false, [], null);
    }

    internal readonly record struct LayerIdentifyResult(Layer Layer, IReadOnlyList<Blueprint.BoundedTile> Tiles, bool CanPlace);

    internal sealed class LayerPipeline
    {
        private readonly Dictionary<Layer, int> _placeIndexByLayer;
        private readonly Dictionary<Layer, int> _clearIndexByLayer;
        private readonly Dictionary<string, Layer> _layerById;

        public IReadOnlyList<Layer> PlaceOrder { get; }
        public IReadOnlyList<Layer> ClearOrder { get; }
        public IReadOnlyList<Layer> IdentifyOrder { get; }

        internal LayerPipeline(
            IReadOnlyList<Layer> placeOrder,
            IReadOnlyList<Layer> clearOrder,
            IReadOnlyList<Layer> identifyOrder,
            Dictionary<Layer, int> placeIndexByLayer,
            Dictionary<Layer, int> clearIndexByLayer,
            Dictionary<string, Layer> layerById)
        {
            PlaceOrder = placeOrder;
            ClearOrder = clearOrder;
            IdentifyOrder = identifyOrder;
            _placeIndexByLayer = placeIndexByLayer;
            _clearIndexByLayer = clearIndexByLayer;
            _layerById = layerById;
        }

        public int ToPlaceIndex(Layer layer) =>
            layer is not null && _placeIndexByLayer.TryGetValue(layer, out int idx) ? idx : -1;

        public int ToClearIndex(Layer layer) =>
            layer is not null && _clearIndexByLayer.TryGetValue(layer, out int idx) ? idx : -1;

        public Layer? TryGetLayer(string id) =>
            id is not null && _layerById.TryGetValue(id, out Layer? layer) ? layer : null;
    }

    internal static class LayerSystem
    {
        private const string ResourceName = "ArchitectsStencilMod.LayerOrder.json";

        public static Layer Wall => WallLayer.Definition;
        public static Layer Block => BlockLayer.Definition;
        public static Layer FallBlock => FallBlockLayer.Definition;
        public static Layer TileAnchored => TileAnchoredLayer.Definition;
        public static Layer WallAnchored => WallAnchoredLayer.Definition;
        public static Layer OtherAnchored => OtherAnchoredLayer.Definition;
        public static Layer Paint => PaintLayer.Definition;
        public static Layer Wiring => WiringLayer.Definition;
        public static Layer Detail => DetailLayer.Definition;
        public static Layer Fluid => FluidLayer.Definition;
        public static Layer Unknown => UnknownLayer.Definition;
        public static Layer Sweep => SweepLayer.Definition;

        public static IReadOnlyList<Layer> BuiltInLayers { get; } =
        [
            Wall,
            Block,
            FallBlock,
            TileAnchored,
            WallAnchored,
            OtherAnchored,
            Paint,
            Wiring,
            Detail,
            Sweep,
            Fluid,
            Unknown,
        ];

        /// <summary>
        /// Composes a layer pipeline from the built-in layers and custom layers.
        /// </summary>
        /// <returns></returns>
        public static LayerPipeline ComposePipeline()
        {
            /// Resolves various layer order and identification configurations.
            // Discover custom layers.
            IReadOnlyList<Layer> customLayers = CustomLayer.DiscoverAttributedLayers();
            // Create a registry of all layers.
            var registry = new List<Layer>(BuiltInLayers.Count + customLayers.Count);
            registry.AddRange(BuiltInLayers);
            registry.AddRange(customLayers);

            // Create a dictionary of layers by ID.
            var layerById = new Dictionary<string, Layer>(StringComparer.Ordinal);
            for (int i = 0; i < registry.Count; i++)
            {
                Layer layer = registry[i];
                if (!layerById.TryAdd(layer.Id, layer))
                    ASMLog.Warn($"Duplicate layer id '{layer.Id}' was registered. Later registrations are ignored.", "Layers");
            }

            // Load the layer order definition.
            LayerOrderDefinition definition = LayerOrderDefinition.Load();
            // Resolve the layer order for each phase.
            var placeOrder = ResolveOrder(definition.PlaceOrder, layerById, registry);
            var clearOrder = ResolveOrder(definition.ClearOrder, layerById, registry);
            var identifyOrder = ResolveOrder(definition.IdentifyOrder, layerById, registry);

            // Create a new layer pipeline.
            return new LayerPipeline(
                placeOrder,
                clearOrder,
                identifyOrder,
                CreateIndex(placeOrder),
                CreateIndex(clearOrder),
                layerById);
        }

        public static List<LayerIdentifyResult> Identify(
            in PasteTile pasteTile,
            in PasteTileLookup identifyLookup,
            LayerPipeline pipeline,
            in PasteContext context,
            PastingConfig config)
        {
            var identifiedLayers = new List<LayerIdentifyResult>();
            var claimedTypes = new HashSet<LayerType>();

            for (int i = 0; i < pipeline.IdentifyOrder.Count; i++)
            {
                Layer layer = pipeline.IdentifyOrder[i];
                if (!config.IsLayerIdentificationEnabled(layer.Id))
                    continue;
                if (claimedTypes.Contains(layer.LayerType))
                    continue;

                IReadOnlyList<Blueprint.BoundedTile> tiles = layer.Identify(in pasteTile, in identifyLookup);
                if (tiles.Count == 0)
                    continue;

                bool canPlace = layer.CanPlace(in pasteTile, in identifyLookup, in context);
                identifiedLayers.Add(new LayerIdentifyResult(layer, tiles, canPlace));
                claimedTypes.Add(layer.LayerType);
            }

            return identifiedLayers;
        }

        private static List<Layer> ResolveOrder(
            IReadOnlyList<string> configuredIds,
            IReadOnlyDictionary<string, Layer> layerById,
            IReadOnlyList<Layer> registry)
        {
            var order = new List<Layer>(registry.Count);
            var seen = new HashSet<Layer>();

            for (int i = 0; i < configuredIds.Count; i++)
            {
                string id = configuredIds[i];
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                if (!layerById.TryGetValue(id, out Layer? layer))
                {
                    ASMLog.Warn($"Layer id '{id}' is present in LayerOrder.json but no matching layer was registered.", "Layers");
                    continue;
                }

                if (seen.Add(layer))
                    order.Add(layer);
            }

            for (int i = 0; i < registry.Count; i++)
            {
                Layer layer = registry[i];
                if (!seen.Add(layer))
                    continue;

                InsertNearMatchingDomain(order, layer);
            }

            return order;
        }

        private static void InsertNearMatchingDomain(List<Layer> order, Layer layer)
        {
            int insertAfter = -1;
            for (int i = 0; i < order.Count; i++)
            {
                if (order[i].LayerType == layer.LayerType)
                    insertAfter = i;
            }

            if (insertAfter >= 0)
                order.Insert(insertAfter + 1, layer);
            else
                order.Add(layer);
        }

        private static Dictionary<Layer, int> CreateIndex(IReadOnlyList<Layer> order)
        {
            var map = new Dictionary<Layer, int>(order.Count);
            for (int i = 0; i < order.Count; i++)
                map[order[i]] = i;
            return map;
        }

        private sealed class LayerOrderDefinition
        {
            public List<string> PlaceOrder { get; init; } = [];
            public List<string> ClearOrder { get; init; } = [];
            public List<string> IdentifyOrder { get; init; } = [];

            public static LayerOrderDefinition Load()
            {
                try
                {
                    using Stream? stream = typeof(LayerSystem).Assembly.GetManifestResourceStream(ResourceName);
                    if (stream is null)
                    {
                        ASMLog.Warn($"Layer order resource '{ResourceName}' was not found. Falling back to defaults.", "Layers");
                        return CreateDefault();
                    }

                    var definition = JsonSerializer.Deserialize<LayerOrderDefinition>(stream, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    });

                    if (definition is null)
                    {
                        ASMLog.Warn("Layer order resource was empty. Falling back to defaults.", "Layers");
                        return CreateDefault();
                    }

                    return definition;
                }
                catch (Exception ex)
                {
                    ASMLog.Error("Failed to load layer order resource. Falling back to defaults.", ex, "Layers");
                    return CreateDefault();
                }
            }

            private static LayerOrderDefinition CreateDefault() => new()
            {
                PlaceOrder =
                [
                    "Wall",
                    "Block",
                    "FallBlock",
                    "TileAnchored",
                    "WallAnchored",
                    "OtherAnchored",
                    "Tree",
                    "Unknown",
                    "Fluid",
                    "Paint",
                    "Wiring",
                    "Detail",
                    "Sweep",
                ],
                ClearOrder =
                [
                    "Fluid",
                    "Detail",
                    "Wiring",
                    "Paint",
                    "Unknown",
                    "Tree",
                    "OtherAnchored",
                    "WallAnchored",
                    "TileAnchored",
                    "FallBlock",
                    "Block",
                    "Wall",
                ],
                IdentifyOrder =
                [
                    "Wall",
                    "Unknown",
                    "Tree",
                    "OtherAnchored",
                    "WallAnchored",
                    "TileAnchored",
                    "FallBlock",
                    "Block",
                    "Fluid",
                    "Paint",
                    "Wiring",
                    "Detail",
                    "Sweep",
                ],
            };
        }
    }
}
