using System;
using System.Collections.Generic;
using System.Reflection;
using ArchitectsBlueprintMod.Core;

namespace ArchitectsBlueprintMod.Core.Layers
{
    /// <summary>
    /// Discovers attributed custom layers and exposes tile-type ownership checks used by layer ordering and exclusions.
    /// </summary>
    internal static class CustomLayer
    {
        private static IReadOnlyList<Layer>? _cached;

        public static IReadOnlyList<Layer> DiscoverAttributedLayers()
        {
            if (_cached is not null)
                return _cached;

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
            _cached = list;
            return _cached;
        }

        /// <summary>
        /// True if any attributed custom layer claims this tile type (e.g. <see cref="BlockLayer"/> exclusion).
        /// </summary>
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
    }
}
