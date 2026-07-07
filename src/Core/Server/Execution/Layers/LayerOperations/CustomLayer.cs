using System;
using System.Collections.Generic;
using System.Reflection;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    /// <summary>
    /// Composite paste layer for attributed custom handlers. Appears once in the main pipeline; sub-layers are discovered and ordered internally.
    /// </summary>
    internal sealed class CustomLayer : Layer
    {
        public static readonly CustomLayer Definition = new();

        private static IReadOnlyList<Layer>? _cachedSubLayers;
        private static Dictionary<string, Layer>? _subLayerById;

        private static readonly string[] DefaultIdentifyOrder = ["Tree", "Vine", "Plant"];
        private static readonly string[] DefaultPlaceOrder = ["Plant", "Vine", "Tree"];
        private static readonly string[] DefaultClearOrder = ["Tree", "Vine", "Plant"];

        private CustomLayer() : base("Custom", LayerType.Custom) { }

        /// <summary>Discovers and caches attributed custom sub-layers from the mod assembly.</summary>
        public static IReadOnlyList<Layer> DiscoverAttributedLayers()
        {
            if (_cachedSubLayers is not null)
                return _cachedSubLayers;

            // --- Scan for [CustomPasteLayer] types and collect their static Definition fields.
            var list = new List<Layer>();
            Assembly asm = typeof(CustomLayer).Assembly;
            Type layerBase = typeof(Layer);

            foreach (Type type in asm.GetTypes())
            {
                if (type.IsAbstract || !layerBase.IsAssignableFrom(type))
                    continue;
                if (type.GetCustomAttribute<CustomPasteLayerAttribute>() is null)
                    continue;

                FieldInfo? defField = type.GetField("Definition", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (defField is null || !layerBase.IsAssignableFrom(defField.FieldType))
                    continue;

                if (defField.GetValue(null) is not Layer layer)
                    continue;

                list.Add(layer);
            }

            list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            _cachedSubLayers = list;

            var byId = new Dictionary<string, Layer>(StringComparer.Ordinal);
            for (int i = 0; i < list.Count; i++)
            {
                Layer subLayer = list[i];
                if (!byId.TryAdd(subLayer.Id, subLayer))
                    TABLog.Warn($"Duplicate custom sub-layer id '{subLayer.Id}' was registered. Later registrations are ignored.", "Layers");
            }

            _subLayerById = byId;
            return _cachedSubLayers;
        }

        /// <summary>True when <paramref name="layer"/> is an attributed custom sub-layer rather than the composite.</summary>
        public static bool IsAttributedSubLayer(Layer layer)
        {
            if (layer is null || layer == Definition)
                return false;

            DiscoverAttributedLayers();
            return _subLayerById!.ContainsKey(layer.Id);
        }

        /// <summary>True if any attributed custom sub-layer claims this tile type (e.g. <see cref="BlockLayer"/> exclusion).</summary>
        public static bool TileTypeIsClaimedByAttributedLayer(ushort tileType)
        {
            IReadOnlyList<Layer> layers = DiscoverAttributedLayers();
            for (int i = 0; i < layers.Count; i++)
            {
                if (layers[i].IsCustom(tileType))
                    return true;
            }

            return false;
        }

        /// <summary>Identifies matching custom sub-layers for a tile in configured priority order.</summary>
        public static IReadOnlyList<LayerIdentifyResult> IdentifySubLayers(
            in PasteTile pasteTile,
            in PasteTileLookup identifyLookup,
            in PasteContext context,
            PastingConfig config,
            PasteSubject identifySubject)
        {
            // --- Walk sub-layer identify order; return the first enabled handler that claims this tile.
            var results = new List<LayerIdentifyResult>();
            IReadOnlyList<Layer> ordered = ResolveSubLayerOrder(DefaultIdentifyOrder);

            for (int i = 0; i < ordered.Count; i++)
            {
                Layer subLayer = ordered[i];
                if (!config.IsLayerIdentificationEnabled(subLayer.Id))
                    continue;

                LayerResult identifyResult = subLayer.Identify(in pasteTile, in identifyLookup, identifySubject);
                if (identifyResult.TileBatch is null || identifyResult.TileBatch.Cells.Count == 0)
                    continue;

                if (pasteTile.PayloadCell.IsEmpty && identifySubject == PasteSubject.Payload)
                    continue;

                bool placementValid = subLayer.PlacementValid(in pasteTile, in identifyLookup, in context);
                bool hasRequiredItems = subLayer.HasRequiredItems(in pasteTile, in identifyLookup, in context);
                results.Add(new LayerIdentifyResult(subLayer, identifyResult, placementValid, hasRequiredItems));
                break;
            }

            return results;
        }

        public static IReadOnlyList<Layer> SubLayerPlaceOrder => ResolveSubLayerOrder(DefaultPlaceOrder);

        public static IReadOnlyList<Layer> SubLayerClearOrder => ResolveSubLayerOrder(DefaultClearOrder);

        private static IReadOnlyList<Layer> ResolveSubLayerOrder(IReadOnlyList<string> configuredIds)
        {
            // --- Map configured ids to discovered sub-layers, then append any unlisted handlers alphabetically.
            DiscoverAttributedLayers();
            var order = new List<Layer>();
            var seen = new HashSet<Layer>();

            for (int i = 0; i < configuredIds.Count; i++)
            {
                string id = configuredIds[i];
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                if (_subLayerById!.TryGetValue(id, out Layer? subLayer) && seen.Add(subLayer))
                    order.Add(subLayer);
            }

            IReadOnlyList<Layer> discovered = _cachedSubLayers!;
            for (int i = 0; i < discovered.Count; i++)
            {
                Layer subLayer = discovered[i];
                if (seen.Add(subLayer))
                    order.Add(subLayer);
            }

            return order;
        }

        public override bool Match(in PasteTile tile) => false;

        public override LayerResult Identify(in PasteTile tile, in PasteTileLookup lookup, PasteSubject identifySubject) =>
            LayerResult.IdentifyNone();

        public override LayerResult Clear(TileBatch batch, in PasteContext context) =>
            LayerResult.Fail(LayerFailReason.Unknown);

        public override LayerResult Place(TileBatch batch, in PasteContext context) =>
            LayerResult.Fail(LayerFailReason.Unknown);

        public override bool Consume(in PasteTile tile, in PasteContext context) => true;

        public override bool CanConsume(in PasteTile tile, in PasteContext context) => true;

        public override bool CanClear(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context) => false;

        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context) => false;

        public override bool HasRequiredItems(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context) => false;

        public override IReadOnlyList<int>? GetItem(in PasteTile tile) => null;
    }
}
