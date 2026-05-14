using System;
using Terraria;
using Terraria.GameContent.UI;
using Terraria.ID;
using Terraria.ModLoader;

namespace ArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Resolves content names (item, tile, wall) to numeric IDs at runtime.
    /// Persists strings in JSON; resolves once and caches for performance.
    /// </summary>
    internal static class ContentNameResolver
    {
        public static int? ResolveItem(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name.StartsWith("Item_", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.AsSpan(5), out int parsedId))
                return parsedId >= 0 && parsedId < ItemLoader.ItemCount ? parsedId : null;
            if (ItemID.Search.TryGetId(name, out int id))
                return id;
            for (int i = (int)ItemID.Count; i < ItemLoader.ItemCount; i++)
            {
                var item = new Item();
                item.SetDefaults(i);
                if (item.IsAir || item.type != i) continue;
                if (string.Equals(item.ModItem?.FullName ?? item.Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return null;
        }

        public static int? ResolveTile(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            // Tile_{id} format for compatibility with GetTileName fallback
            if (name.StartsWith("Tile_", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.AsSpan(5), out int parsedId))
                return parsedId >= 0 && parsedId < TileLoader.TileCount ? parsedId : null;
            if (TileID.Search.TryGetId(name, out int id))
                return id;
            // Mod tiles: linear search (no cache)
            for (int i = (int)TileID.Count; i < TileLoader.TileCount; i++)
            {
                if (string.Equals(TileLoader.GetTile(i)?.Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return null;
        }

        public static int? ResolveWall(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name.StartsWith("Wall_", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.AsSpan(5), out int parsedId))
                return parsedId >= 0 && parsedId < WallLoader.WallCount ? parsedId : null;
            if (WallID.Search.TryGetId(name, out int id))
                return id;
            for (int i = (int)WallID.Count; i < WallLoader.WallCount; i++)
            {
                if (string.Equals(WallLoader.GetWall(i)?.Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return null;
        }

        public static string GetItemName(int id)
        {
            if (id <= 0 || id >= ItemLoader.ItemCount) return $"Item_{id}";
            if (ItemID.Search.TryGetName(id, out string name))
                return name;
            var item = new Item();
            item.SetDefaults(id);
            return item.ModItem?.FullName ?? item.Name ?? Lang.GetItemNameValue(id)?.ToString() ?? $"Item_{id}";
        }

        public static string GetTileName(int id)
        {
            if (id < 0 || id >= TileLoader.TileCount) return $"Tile_{id}";
            if (TileID.Search.TryGetName(id, out string name))
                return name;
            return TileLoader.GetTile(id)?.Name ?? $"Tile_{id}";
        }

        public static string GetWallName(int id)
        {
            if (id < 0 || id >= WallLoader.WallCount) return $"Wall_{id}";
            if (WallID.Search.TryGetName(id, out string name))
                return name;
            return WallLoader.GetWall(id)?.Name ?? $"Wall_{id}";
        }

        public static string GetLiquidTypeName(int type)
        {
            if (LiquidID.Search.TryGetName(type, out string name))
                return name;
            return $"Liquid_{type}";
        }

        public static int? ResolveLiquidType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (LiquidID.Search.TryGetId(name, out int id))
                return id;
            return null;
        }

        /// <summary>Wire kinds: 0=Red, 1=Green, 2=Blue, 3=Yellow, 4=Actuator. No WireID in tModLoader.</summary>
        public static string GetWireKindName(int kind)
        {
            return kind switch { 0 => "Red", 1 => "Green", 2 => "Blue", 3 => "Yellow", 4 => "Actuator", _ => $"Wire_{kind}" };
        }

        public static int? ResolveWireKind(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return name.ToLowerInvariant() switch
            {
                "red" => 0,
                "green" => 1,
                "blue" => 2,
                "yellow" => 3,
                "actuator" => 4,
                _ => null,
            };
        }

        public static string GetPaintName(int paintId)
        {
            if (PaintID.Search.TryGetName(paintId, out string name))
                return name;
            return $"Paint_{paintId}";
        }

        public static int? ResolvePaintId(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (PaintID.Search.TryGetId(name, out int id))
                return id;
            return null;
        }

        /// <summary>
        /// Returns the canonical layer name for consistent indexing and lookup.
        /// Returns null if layer is missing, empty, or not a known layer (Tile, Wall, Liquid, Wire, Paint).
        /// </summary>
        public static string? NormalizeLayer(string? layer)
        {
            if (string.IsNullOrWhiteSpace(layer)) return null;
            if (string.Equals(layer, "Tile", StringComparison.OrdinalIgnoreCase)) return "Tile";
            if (string.Equals(layer, "Wall", StringComparison.OrdinalIgnoreCase)) return "Wall";
            if (string.Equals(layer, "Liquid", StringComparison.OrdinalIgnoreCase)) return "Liquid";
            if (string.Equals(layer, "Wire", StringComparison.OrdinalIgnoreCase)) return "Wire";
            if (string.Equals(layer, "Paint", StringComparison.OrdinalIgnoreCase)) return "Paint";
            return null;
        }

        /// <summary>
        /// Resolves a main content name (tile/wall/liquid/etc.) to its ID using layer context.
        /// Returns null if layer is missing or invalid.
        /// </summary>
        public static int? ResolveMain(string? layer, string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var norm = NormalizeLayer(layer);
            if (norm is null) return null;
            return norm switch
            {
                "Tile" => ResolveTile(name),
                "Wall" => ResolveWall(name),
                "Liquid" => ResolveLiquidType(name),
                "Wire" => ResolveWireKind(name),
                "Paint" => ResolvePaintId(name),
                _ => null,
            };
        }

        /// <summary>
        /// Resolves a base content name (tile/wall for conversion placement) using layer context.
        /// Returns null if layer is missing or invalid.
        /// </summary>
        public static int? ResolveBase(string? layer, string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var norm = NormalizeLayer(layer);
            if (norm is null) return null;
            return norm switch
            {
                "Tile" => ResolveTile(name),
                "Wall" => ResolveWall(name),
                _ => null,
            };
        }

        /// <summary>
        /// Whether the layer supports style variants (e.g. Tile has torch styles; Wall does not).
        /// </summary>
        public static bool SupportsStyle(string? layer)
        {
            var norm = NormalizeLayer(layer);
            return norm is not null && string.Equals(norm, "Tile", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolves a main content ID back to its name. Inverse of ResolveMain.
        /// </summary>
        public static string? ResolveMainIdToName(string? layer, int mainId)
        {
            var norm = NormalizeLayer(layer);
            if (norm is null) return null;
            return norm switch
            {
                "Tile" => GetTileName(mainId),
                "Wall" => GetWallName(mainId),
                "Liquid" => GetLiquidTypeName(mainId),
                "Wire" => GetWireKindName(mainId),
                "Paint" => GetPaintName(mainId),
                _ => null,
            };
        }
    }
}
