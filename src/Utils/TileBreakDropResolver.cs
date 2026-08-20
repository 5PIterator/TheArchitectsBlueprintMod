using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;
using TheArchitectsBlueprintMod.Core.Layers;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Utils
{
    /// <summary>Resolves break drops and authoritative placement style/alternate for live world tiles.</summary>
    internal static class TileBreakDropResolver
    {
        private static MethodInfo? _killTileGetItemDrops;
        private static MethodInfo? _killWallGetItemDrops;
        private static bool _reflectionInitialized;
        private static bool _warnedMissingKillTile;
        private static bool _warnedMissingKillWall;

        /// <summary>Returns the primary break drop for the tile at world coordinates, if any.</summary>
        public static bool TryResolveTileStoreDrop(int x, int y, out int itemType, out int stack)
        {
            // --- Prefer public drop APIs; fall back to KillTile reflection only as last resort.

            itemType = 0;
            stack = 1;

            if (!Main.tile[x, y].HasTile)
                return false;

            // Credit Store clears from actual break loot only (e.g. leaves drop nothing, not LeafWand).
            return TryResolveWorldAwareTileDrop(x, y, Main.tile[x, y], out itemType, out stack);
        }

        /// <summary>Resolves placement style and alternate for the live tile at world coordinates.</summary>
        public static void ResolvePlacementVariant(int x, int y, out int style, out int alternate)
        {
            // --- Prefer drop-item placeStyle; fall back to GetTileStyle, then GetTileInfo.

            style = 0;
            alternate = 0;
            if (!WorldGen.InWorld(x, y) || !Main.tile[x, y].HasTile)
                return;

            ResolvePlacementVariant(Main.tile[x, y], x, y, out style, out alternate);
        }

        /// <summary>Resolves placement style and alternate from a tile snapshot, optionally with world coordinates.</summary>
        public static void ResolvePlacementVariant(Tile tile, out int style, out int alternate) =>
            ResolvePlacementVariant(tile, x: -1, y: -1, out style, out alternate);

        /// <summary>Resolves placement style and alternate from a tile, using coordinates when valid for world-aware drops.</summary>
        public static void ResolvePlacementVariant(Tile tile, int x, int y, out int style, out int alternate)
        {
            // --- Prefer frame-matched placeStyle/alternate so Signs-style orientations are not stored as styles.

            style = 0;
            alternate = 0;
            if (!tile.HasTile || tile.TileType >= TileLoader.TileCount)
                return;

            // Seed with GetTileInfo, then override style from drop placeStyle when available.
            int infoStyle = 0;
            TileObjectData.GetTileInfo(tile, ref infoStyle, ref alternate);
            int cachedStyle = Math.Max(0, infoStyle);
            int cachedAlternate = Math.Max(0, alternate);

            if (WorldGen.InWorld(x, y)
                && TryResolveWorldAwareTileDrop(x, y, tile, out int itemType, out _)
                && TryGetPlacementItemPlaceStyle(itemType, tile.TileType, out int placeStyle))
            {
                cachedStyle = placeStyle;
            }
            else
            {
                int tileStyle = TileObjectData.GetTileStyle(tile);
                if (tileStyle >= 0)
                    cachedStyle = tileStyle;
            }

            // Recover orientation alternates when vanilla StyleMultiplier is missing (Signs / TatteredWoodSign).
            FrameOperations.FramePlacementVariant variant = FrameOperations.ResolveFramePlacementVariant(
                tile.TileType,
                tile.TileFrameX,
                tile.TileFrameY,
                cachedStyle,
                cachedAlternate);
            style = variant.Style;
            alternate = variant.Alternate;
        }

        /// <summary>Resolves a break drop using public APIs first, then KillTile reflection as last resort.</summary>
        private static bool TryResolveWorldAwareTileDrop(int x, int y, Tile tile, out int itemType, out int stack)
        {
            // --- Public/mod path first; reflected KillTile only when those yield nothing.

            itemType = 0;
            stack = 1;

            if (TryResolvePublicTileDrop(x, y, tile, out itemType, out stack))
                return true;

            if (!TryInvokeKillTileGetItemDrops(
                    x,
                    y,
                    tile,
                    out int primaryItem,
                    out int primaryStack,
                    out int secondaryItem,
                    out int secondaryStack))
                return false;

            if (primaryItem > 0)
            {
                itemType = primaryItem;
                stack = Math.Max(1, primaryStack);
                return true;
            }

            if (secondaryItem > 0)
            {
                itemType = secondaryItem;
                stack = Math.Max(1, secondaryStack);
                return true;
            }

            return false;
        }

        /// <summary>Resolves mod tile drops via GetItemDrops, then GetItemDropFromTypeAndStyle for mod types only.</summary>
        private static bool TryResolvePublicTileDrop(int x, int y, Tile tile, out int itemType, out int stack)
        {
            // --- Mod tiles only: never reverse-scan vanilla createTile (Leaf Wand false positives).

            itemType = 0;
            stack = 1;
            ushort tileType = tile.TileType;
            if (tileType < TileID.Count || tileType >= TileLoader.TileCount)
                return false;

            if (TryResolveModTileDrop(x, y, tileType, out itemType, out stack))
                return true;

            // Fall back to the registered type+style drop table for mod tiles without custom GetItemDrops.
            int style = TileObjectData.GetTileStyle(tile);
            if (style < 0)
                style = 0;

            int drop = TileLoader.GetItemDropFromTypeAndStyle(tileType, style);
            if (drop <= ItemID.None)
                return false;

            itemType = drop;
            stack = 1;
            return true;
        }

        /// <summary>Reads placeStyle when the item places the given tile type.</summary>
        private static bool TryGetPlacementItemPlaceStyle(int itemType, int tileType, out int placeStyle)
        {
            // --- Accept only samples whose createTile is this tile or its furniture counterpart.

            placeStyle = 0;
            if (itemType <= ItemID.None || itemType >= ItemLoader.ItemCount)
                return false;

            if (!ContentSamples.ItemsByType.TryGetValue(itemType, out Item? sample) || sample is null)
                return false;

            if (!PlacesTileType(sample.createTile, tileType) || sample.placeStyle < 0)
                return false;

            placeStyle = sample.placeStyle;
            return true;
        }

        /// <summary>True when the item's createTile is this tile or its open/closed furniture counterpart.</summary>
        private static bool PlacesTileType(int createTile, int tileType)
        {
            // --- Door items create the closed tile; captured open doors still use that placeStyle.

            if (createTile == tileType)
                return true;

            return InteractionMap.TryGetAlternateFurnitureTile(tileType, out int alternate) && alternate == createTile;
        }

        /// <summary>Returns the break drop for the wall at world coordinates, if any.</summary>
        public static bool TryResolveWallStoreDrop(int x, int y, out int itemType, out int stack)
        {
            // --- Prefer createWall + WallLoader.Drop; reflect KillWall only as last resort.

            itemType = 0;
            stack = 1;

            Tile tile = Main.tile[x, y];
            int wallType = tile.WallType;
            if (wallType <= 0)
                return false;

            if (TryResolvePublicWallDrop(x, y, wallType, out itemType))
                return true;

            if (!TryInvokeKillWallGetItemDrops(tile, out int dropItem) || dropItem <= 0)
                return false;

            itemType = dropItem;
            WallLoader.Drop(x, y, wallType, ref itemType);
            return itemType > 0;
        }

        /// <summary>Resolves a wall drop from createWall samples, then applies WallLoader.Drop hooks.</summary>
        private static bool TryResolvePublicWallDrop(int x, int y, int wallType, out int itemType)
        {
            // --- Find the placement item for this wall, then let WallLoader adjust or cancel it.

            itemType = 0;
            if (!TryFindWallPlacementItem(wallType, out int drop) || drop <= 0)
                return false;

            WallLoader.Drop(x, y, wallType, ref drop);
            if (drop <= 0)
                return false;

            itemType = drop;
            return true;
        }

        /// <summary>Finds the first ContentSamples item whose createWall matches the wall type.</summary>
        private static bool TryFindWallPlacementItem(int wallType, out int itemType)
        {
            // --- Prefer ContentSamples over SetDefaults scans for every item id.

            itemType = 0;
            foreach (KeyValuePair<int, Item> pair in ContentSamples.ItemsByType)
            {
                Item sample = pair.Value;
                if (sample is null || sample.createWall != wallType)
                    continue;

                itemType = sample.type;
                return itemType > 0;
            }

            return false;
        }

        /// <summary>Invokes internal WorldGen.KillTile_GetItemDrops when public APIs miss.</summary>
        private static bool TryInvokeKillTileGetItemDrops(
            int x,
            int y,
            Tile tile,
            out int dropItem,
            out int dropItemStack,
            out int secondaryItem,
            out int secondaryItemStack)
        {
            // --- Last-resort vanilla break loot via reflected internal helper.

            dropItem = 0;
            dropItemStack = 1;
            secondaryItem = 0;
            secondaryItemStack = 1;

            MethodInfo? method = GetKillTileGetItemDropsMethod();
            if (method is null)
                return false;

            // Include multi-tile object drops (e.g. Sunflower) so Store clears can refund them.
            object?[] args = { x, y, tile, 0, 1, 0, 1, true };
            try
            {
                method.Invoke(null, args);
            }
            catch
            {
                return false;
            }

            dropItem = (int)args[3]!;
            dropItemStack = (int)args[4]!;
            secondaryItem = (int)args[5]!;
            secondaryItemStack = (int)args[6]!;
            return true;
        }

        /// <summary>Invokes private WorldGen.KillWall_GetItemDrops when public APIs miss.</summary>
        private static bool TryInvokeKillWallGetItemDrops(Tile tile, out int dropItem)
        {
            // --- Last-resort vanilla wall break loot via reflected private helper.

            dropItem = 0;

            MethodInfo? method = GetKillWallGetItemDropsMethod();
            if (method is null)
                return false;

            try
            {
                dropItem = (int)method.Invoke(null, [tile])!;
            }
            catch
            {
                return false;
            }

            return true;
        }

        /// <summary>Reads the first non-empty ModTile.GetItemDrops entry at world coordinates.</summary>
        private static bool TryResolveModTileDrop(int x, int y, int tileType, out int itemType, out int stack)
        {
            // --- Use the mod tile's custom drop enumeration when present.

            itemType = 0;
            stack = 1;

            ModTile? modTile = TileLoader.GetTile(tileType);
            if (modTile is null)
                return false;

            IEnumerable<Item>? drops = modTile.GetItemDrops(x, y);
            if (drops is null)
                return false;

            foreach (Item item in drops)
            {
                if (item is null || item.type <= ItemID.None || item.stack <= 0)
                    continue;

                itemType = item.type;
                stack = item.stack;
                return true;
            }

            return false;
        }

        private static MethodInfo? GetKillTileGetItemDropsMethod()
        {
            EnsureReflectionInitialized();
            return _killTileGetItemDrops;
        }

        private static MethodInfo? GetKillWallGetItemDropsMethod()
        {
            EnsureReflectionInitialized();
            return _killWallGetItemDrops;
        }

        /// <summary>Lazily binds Kill*GetItemDrops methods and warns once when either is missing.</summary>
        private static void EnsureReflectionInitialized()
        {
            // --- Bind by name+signature; warn once per missing method after a tML update.
            if (_reflectionInitialized)
                return;

            _reflectionInitialized = true;
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Type worldGen = typeof(WorldGen);
            MethodInfo[] methods = worldGen.GetMethods(flags);

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "KillTile_GetItemDrops"
                    && parameters.Length >= 6
                    && parameters[2].ParameterType == typeof(Tile))
                {
                    _killTileGetItemDrops = method;
                }
                else if (method.Name == "KillWall_GetItemDrops"
                    && parameters.Length == 1
                    && parameters[0].ParameterType == typeof(Tile))
                {
                    _killWallGetItemDrops = method;
                }
            }

            if (_killTileGetItemDrops is null && !_warnedMissingKillTile)
            {
                _warnedMissingKillTile = true;
                TABLog.Warn(
                    "WorldGen.KillTile_GetItemDrops was not found; Store tile clears use public drop APIs only.",
                    "Drops");
            }

            if (_killWallGetItemDrops is null && !_warnedMissingKillWall)
            {
                _warnedMissingKillWall = true;
                TABLog.Warn(
                    "WorldGen.KillWall_GetItemDrops was not found; Store wall clears use public drop APIs only.",
                    "Drops");
            }
        }
    }
}
