using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Debug;
using Microsoft.Xna.Framework;
using Terraria;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    /// <summary>Why a <see cref="Layer"/> operation did not succeed.</summary>
    internal enum LayerFailReason
    {
        None,
        Unknown,
        MissingItems,
        /// <summary>No interaction map entry (or item mapping) exists for the required tile/wall.</summary>
        UnresolvedInteraction,
        InvalidPlacement,
        /// <summary>Clear could not break the tile (e.g. future pickaxe power check).</summary>
        InsufficientPickaxePower,
        ClearBlocked,
    }

    /// <summary>
    /// Result of <see cref="Layer.Identify"/>, <see cref="Layer.Clear"/>, or <see cref="Layer.Place"/>.
    /// <see cref="TileBatch"/> holds identified cells (identify) or remaining paste tiles (clear/place retry).
    /// </summary>
    internal readonly struct LayerResult
    {
        public readonly bool Success;
        public readonly TileBatch? TileBatch;
        public readonly LayerFailReason FailReason;
        public readonly string? Message;
        public readonly string? OkLabel;
        public readonly string? Detail;

        public IReadOnlyList<PasteTile> RemainingTiles => TileBatch?.Tiles ?? Array.Empty<PasteTile>();

        private LayerResult(
            bool success,
            TileBatch? tileBatch,
            LayerFailReason failReason,
            string? message,
            string? okLabel,
            string? detail)
        {
            Success = success;
            TileBatch = tileBatch;
            FailReason = failReason;
            Message = message;
            OkLabel = okLabel;
            Detail = detail;
        }

        public static LayerResult IdentifyNone() =>
            new(true, null, LayerFailReason.None, null, null, null);

        public static LayerResult Identified(TileBatch cells, string? message) =>
            new(true, cells, LayerFailReason.None, message, null, null);

        public static LayerResult Ok(string? message = null, string? okLabel = null) =>
            new(true, null, LayerFailReason.None, message, okLabel, null);

        public static LayerResult OkWithRemaining(
            TileBatch batch,
            string? message = null,
            string? okLabel = null) =>
            new(true, batch, LayerFailReason.None, message, okLabel, null);

        public static LayerResult OkWithRemaining(
            IReadOnlyList<PasteTile> remaining,
            string? message = null,
            string? okLabel = null) =>
            OkWithRemaining(new TileBatch([.. remaining]), message, okLabel);

        public static LayerResult Fail(LayerFailReason reason, string? message = null, string? detail = null) =>
            new(false, null, reason, message, null, detail);

        public static LayerResult Fail(Point _, LayerFailReason reason, string? message = null, string? detail = null) =>
            Fail(reason, message, detail);

        public LayerResult WithLog(string? message = null, string? okLabel = null, string? detail = null) =>
            new(Success, TileBatch, FailReason, message ?? Message, okLabel ?? OkLabel, detail ?? Detail);
    }

    internal readonly record struct LayerIdentifyResult(
        Layer Layer,
        LayerResult Result,
        bool PlacementValid,
        bool HasRequiredItems)
    {
        public IReadOnlyList<Blueprint.BlueprintCell> Tiles => Result.TileBatch?.Cells ?? [];
        public string? Message => Result.Message;
        public readonly bool CanPlace => PlacementValid && HasRequiredItems;
    }

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

        public int ToPlaceIndex(Layer layer)
        {
            if (layer is null)
                return -1;

            if (CustomLayer.IsAttributedSubLayer(layer))
                layer = CustomLayer.Definition;

            return _placeIndexByLayer.TryGetValue(layer, out int idx) ? idx : -1;
        }

        public int ToClearIndex(Layer layer)
        {
            if (layer is null)
                return -1;

            if (CustomLayer.IsAttributedSubLayer(layer))
                layer = CustomLayer.Definition;

            return _clearIndexByLayer.TryGetValue(layer, out int idx) ? idx : -1;
        }

        public Layer? TryGetLayer(string id) =>
            id is not null && _layerById.TryGetValue(id, out Layer? layer) ? layer : null;
    }

    internal static class LayerSystem
    {
        private const string ResourceName = "TheArchitectsBlueprintMod.LayerOrder.json";

        public static Layer Wall => WallLayer.Definition;
        public static Layer Block => BlockLayer.Definition;
        public static Layer Falling => FallingLayer.Definition;
        public static Layer Frame => FrameLayer.Definition;
        public static Layer Paint => PaintLayer.Definition;
        public static Layer Wiring => WiringLayer.Definition;
        public static Layer Detail => DetailLayer.Definition;
        public static Layer Fluid => FluidLayer.Definition;
        public static Layer Custom => CustomLayer.Definition;
        public static Layer Unknown => UnknownLayer.Definition;

        public static IReadOnlyList<Layer> BuiltInLayers { get; } =
        [
            Wall,
            Block,
            Falling,
            Frame,
            Custom,
            Paint,
            Wiring,
            Detail,
            Fluid,
            Unknown,
        ];

        /// <summary>
        /// Composes a layer pipeline from built-in layers. Custom sub-layers are registered for lookup but ordered internally by <see cref="CustomLayer"/>.
        /// </summary>
        public static LayerPipeline ComposePipeline()
        {
            // --- Register built-in pipeline layers and discover custom sub-layers for id lookup only.
            IReadOnlyList<Layer> customSubLayers = CustomLayer.DiscoverAttributedLayers();
            var registry = new List<Layer>(BuiltInLayers);

            var layerById = new Dictionary<string, Layer>(StringComparer.Ordinal);
            for (int i = 0; i < registry.Count; i++)
            {
                Layer layer = registry[i];
                if (!layerById.TryAdd(layer.Id, layer))
                    TABLog.Warn($"Duplicate layer id '{layer.Id}' was registered. Later registrations are ignored.", "Layers");
            }

            for (int i = 0; i < customSubLayers.Count; i++)
            {
                Layer subLayer = customSubLayers[i];
                if (!layerById.TryAdd(subLayer.Id, subLayer))
                    TABLog.Warn($"Duplicate custom sub-layer id '{subLayer.Id}' was registered. Later registrations are ignored.", "Layers");
            }

            LayerOrderDefinition definition = LayerOrderDefinition.Load();
            var placeOrder = ResolveOrder(definition.PlaceOrder, layerById, registry);
            var clearOrder = ResolveOrder(definition.ClearOrder, layerById, registry);
            var identifyOrder = ResolveOrder(definition.IdentifyOrder, layerById, registry);

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
            PastingConfig config,
            PasteSubject identifySubject)
        {
            var identifiedLayers = new List<LayerIdentifyResult>();
            var claimedTypes = new HashSet<LayerType>();

            for (int i = 0; i < pipeline.IdentifyOrder.Count; i++)
            {
                Layer layer = pipeline.IdentifyOrder[i];
                if (!config.IsLayerIdentificationEnabled(layer.Id))
                    continue;

                if (layer == CustomLayer.Definition)
                {
                    IReadOnlyList<LayerIdentifyResult> customResults = CustomLayer.IdentifySubLayers(
                        in pasteTile,
                        in identifyLookup,
                        in context,
                        config,
                        identifySubject);
                    for (int j = 0; j < customResults.Count; j++)
                    {
                        LayerIdentifyResult custom = customResults[j];
                        if (claimedTypes.Contains(LayerType.Custom))
                            continue;

                        identifiedLayers.Add(custom);
                        claimedTypes.Add(LayerType.Custom);
                    }

                    continue;
                }

                if (claimedTypes.Contains(layer.LayerType))
                    continue;

                LayerResult identifyResult = layer.Identify(in pasteTile, in identifyLookup, identifySubject);
                if (identifyResult.TileBatch is null || identifyResult.TileBatch.Cells.Count == 0)
                    continue;

                // Vacant payload: clear/sweep apply air intent; place layers must not queue.
                if (pasteTile.PayloadCell.IsEmpty && identifySubject == PasteSubject.Payload)
                    continue;

                bool placementValid = layer.PlacementValid(in pasteTile, in identifyLookup, in context);
                bool hasRequiredItems = layer.HasRequiredItems(in pasteTile, in identifyLookup, in context);
                identifiedLayers.Add(new LayerIdentifyResult(layer, identifyResult, placementValid, hasRequiredItems));
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
                    TABLog.Warn($"Layer id '{id}' is present in LayerOrder.json but no matching layer was registered.", "Layers");
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
                        TABLog.Warn($"Layer order resource '{ResourceName}' was not found. Falling back to defaults.", "Layers");
                        return CreateDefault();
                    }

                    var definition = JsonSerializer.Deserialize<LayerOrderDefinition>(stream, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    });

                    if (definition is null)
                    {
                        TABLog.Warn("Layer order resource was empty. Falling back to defaults.", "Layers");
                        return CreateDefault();
                    }

                    return definition;
                }
                catch (Exception ex)
                {
                    TABLog.Error("Failed to load layer order resource. Falling back to defaults.", ex, "Layers");
                    return CreateDefault();
                }
            }

            private static LayerOrderDefinition CreateDefault() => new()
            {
                PlaceOrder =
                [
                    "Wall",
                    "Block",
                    "Falling",
                    "Frame",
                    "Custom",
                    "Fluid",
                    "Unknown",
                    "Paint",
                    "Wiring",
                    "Detail",
                ],
                ClearOrder =
                [
                    "Fluid",
                    "Detail",
                    "Wiring",
                    "Paint",
                    "Unknown",
                    "Custom",
                    "Frame",
                    "Falling",
                    "Block",
                    "Wall",
                ],
                IdentifyOrder =
                [
                    "Wall",
                    "Unknown",
                    "Custom",
                    "Frame",
                    "Falling",
                    "Block",
                    "Fluid",
                    "Paint",
                    "Wiring",
                    "Detail",
                ],
            };
        }
    }
}
