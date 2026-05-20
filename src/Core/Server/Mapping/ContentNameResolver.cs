using System;
using Terraria;
using Terraria.GameContent.UI;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.TileMapping
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

        public static int? ResolveTile(string name) => ResolveTileFullName(name);

        public static int? ResolveWall(string name) => ResolveWallFullName(name);

        /// <summary>Resolves a tile by FullName (<c>ModName:InternalName</c>), search name, internal name, or <c>Tile_{id}</c>.</summary>
        public static int? ResolveTileFullName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name.StartsWith("Tile_", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.AsSpan(5), out int parsedId))
                return parsedId >= 0 && parsedId < TileLoader.TileCount ? parsedId : null;
            if (TileID.Search.TryGetId(name, out int id))
                return id;
            if (TrySplitModContentName(name, out string modName, out string internalName))
            {
                Mod? mod = ModLoader.TryGetMod(modName, out Mod? m) ? m : null;
                if (mod is not null && ModContent.TryFind(mod.Name, internalName, out ModTile? modTile))
                    return modTile.Type;
            }

            for (int i = (int)TileID.Count; i < TileLoader.TileCount; i++)
            {
                ModTile? mt = TileLoader.GetTile(i);
                if (mt is null) continue;
                if (string.Equals(mt.FullName, name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(mt.Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return null;
        }

        /// <summary>Resolves a wall by FullName, search name, internal name, or <c>Wall_{id}</c>.</summary>
        public static int? ResolveWallFullName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name.StartsWith("Wall_", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.AsSpan(5), out int parsedId))
                return parsedId >= 0 && parsedId < WallLoader.WallCount ? parsedId : null;
            if (WallID.Search.TryGetId(name, out int id))
                return id;
            if (TrySplitModContentName(name, out string modName, out string internalName))
            {
                Mod? mod = ModLoader.TryGetMod(modName, out Mod? m) ? m : null;
                if (mod is not null && ModContent.TryFind(mod.Name, internalName, out ModWall? modWall))
                    return modWall.Type;
            }

            for (int i = (int)WallID.Count; i < WallLoader.WallCount; i++)
            {
                ModWall? mw = WallLoader.GetWall(i);
                if (mw is null) continue;
                if (string.Equals(mw.FullName, name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(mw.Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return null;
        }

        private static bool TrySplitModContentName(string name, out string modName, out string internalName)
        {
            modName = string.Empty;
            internalName = string.Empty;
            int colon = name.IndexOf(':');
            if (colon <= 0 || colon >= name.Length - 1)
                return false;
            modName = name[..colon];
            internalName = name[(colon + 1)..];
            return true;
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

        public static string GetTileName(int id) => GetTileFullName(id);

        public static string GetWallName(int id) => GetWallFullName(id);

        /// <summary>Stable export name: Terraria search id for vanilla; <see cref="ModTile.FullName"/> for mod tiles.</summary>
        public static string GetTileFullName(int id)
        {
            if (id < 0 || id >= TileLoader.TileCount) return $"Tile_{id}";
            if (TileID.Search.TryGetName(id, out string name))
                return name;
            return TileLoader.GetTile(id)?.FullName ?? $"Tile_{id}";
        }

        /// <summary>Stable export name: Terraria search id for vanilla; <see cref="ModWall.FullName"/> for mod walls.</summary>
        public static string GetWallFullName(int id)
        {
            if (id < 0 || id >= WallLoader.WallCount) return $"Wall_{id}";
            if (WallID.Search.TryGetName(id, out string name))
                return name;
            return WallLoader.GetWall(id)?.FullName ?? $"Wall_{id}";
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
