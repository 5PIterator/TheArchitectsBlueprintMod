using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Metadata for interaction map JSON files. Enables identification by content rather than file name.
    /// </summary>
    public sealed class InteractionMapMetadata
    {
        /// <summary>Format: "tile-first" | "item-first".</summary>
        [JsonPropertyName("format")]
        public string Format { get; set; } = "tile-first";
        /// <summary>Map type: "placement" | "biome" | "custom" | "tilemap" | "shimmer".</summary>
        [JsonPropertyName("mapType")]
        public string? MapType { get; set; }
        /// <summary>The Architect's Blueprint mod version when generated.</summary>
        [JsonPropertyName("modVersion")]
        public string? ModVersion { get; set; }
        /// <summary>Terraria/tModLoader version when generated.</summary>
        [JsonPropertyName("terrariaVersion")]
        public string? TerrariaVersion { get; set; }
        /// <summary>Mod names loaded (optional, for cache invalidation).</summary>
        [JsonPropertyName("modsLoaded")]
        public List<string>? ModsLoaded { get; set; }
        /// <summary>UTC timestamp when generated.</summary>
        [JsonPropertyName("generatedAt")]
        public DateTime? GeneratedAt { get; set; }

        public static InteractionMapMetadata Create(Mod? mod, string format, string? mapType)
        {
            var list = new List<string>();
            try
            {
                for (int i = 0; i < ModLoader.Mods.Length; i++)
                {
                    var m = ModLoader.Mods[i];
                    if (m != null && !string.IsNullOrEmpty(m.Name))
                        list.Add(m.Name);
                }
            }
            catch { /* ignore */ }

            return new InteractionMapMetadata
            {
                Format = format,
                MapType = mapType,
                ModVersion = mod?.Version?.ToString(),
                TerrariaVersion = Main.versionNumber,
                ModsLoaded = list.Count > 0 ? list : null,
                GeneratedAt = DateTime.UtcNow,
            };
        }
    }

    /// <summary>
    /// Wrapper for interaction map JSON: metadata + data. Enables directory-based loading by content.
    /// </summary>
    public sealed class InteractionMapDocument<T>
    {
        [JsonPropertyName("metadata")]
        public InteractionMapMetadata Metadata { get; set; } = new();
        [JsonPropertyName("data")]
        public T Data { get; set; } = default!;
    }
}
