using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace TheArchitectsBlueprintMod.Utils
{
    /// <summary>Resolves break drops and authoritative placement style/alternate for live world tiles.</summary>
    internal static class TileBreakDropResolver
    {
        private static MethodInfo? _killTileGetItemDrops;
        private static MethodInfo? _killWallGetItemDrops;
        private static bool _reflectionInitialized;

        /// <summary>Returns the primary break drop for the tile at world coordinates, if any.</summary>
        public static bool TryResolveTileStoreDrop(int x, int y, out int itemType, out int stack)
        {
            // --- Query only KillTile / mod break drops; never fall back to placement items.

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
            // --- Prefer drop-item placeStyle; fall back to GetTileStyle, then GetTileInfo.

            style = 0;
            alternate = 0;
            if (!tile.HasTile || tile.TileType >= TileLoader.TileCount)
                return;

            // Capture alternate from frame layout even when style comes from the placement item.
            int infoStyle = 0;
            TileObjectData.GetTileInfo(tile, ref infoStyle, ref alternate);

            // Prefer world-aware drop → item.placeStyle when the drop is this tile's placement item.
            if (WorldGen.InWorld(x, y)
                && TryResolveWorldAwareTileDrop(x, y, tile, out int itemType, out _)
                && TryGetPlacementItemPlaceStyle(itemType, tile.TileType, out int placeStyle))
            {
                style = placeStyle;
                return;
            }

            // Fall back to TileObjectData style when frames encode placement style correctly.
            int tileStyle = TileObjectData.GetTileStyle(tile);
            if (tileStyle >= 0)
            {
                style = tileStyle;
                return;
            }

            // Last resort: raw GetTileInfo style (can misread stateful FrameX on some tiles).
            style = Math.Max(0, infoStyle);
        }

        /// <summary>Resolves a break drop using KillTile hooks or mod tile drop APIs at world coordinates.</summary>
        private static bool TryResolveWorldAwareTileDrop(int x, int y, Tile tile, out int itemType, out int stack)
        {
            // --- Query vanilla KillTile drops, then mod tile GetItemDrops.

            itemType = 0;
            stack = 1;

            if (TryInvokeKillTileGetItemDrops(x, y, tile, out int primaryItem, out int primaryStack, out int secondaryItem, out int secondaryStack))
            {
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
            }

            return TryResolveModTileDrop(x, y, tile.TileType, out itemType, out stack);
        }

        /// <summary>Reads placeStyle when the item places the given tile type.</summary>
        private static bool TryGetPlacementItemPlaceStyle(int itemType, int tileType, out int placeStyle)
        {
            // --- Accept only content samples whose createTile matches the live tile.

            placeStyle = 0;
            if (itemType <= ItemID.None || itemType >= ItemLoader.ItemCount)
                return false;

            if (!ContentSamples.ItemsByType.TryGetValue(itemType, out Item? sample) || sample is null)
                return false;

            if (sample.createTile != tileType || sample.placeStyle < 0)
                return false;

            placeStyle = sample.placeStyle;
            return true;
        }

        /// <summary>Returns the break drop for the wall at world coordinates, if any.</summary>
        public static bool TryResolveWallStoreDrop(int x, int y, out int itemType, out int stack)
        {
            // --- Query vanilla wall break drops, then apply mod wall drop hooks.
            itemType = 0;
            stack = 1;

            Tile tile = Main.tile[x, y];
            int wallType = tile.WallType;
            if (wallType <= 0)
                return false;

            if (TryInvokeKillWallGetItemDrops(tile, out int dropItem) && dropItem > 0)
            {
                itemType = dropItem;
                WallLoader.Drop(x, y, wallType, ref itemType);
                return itemType > 0;
            }

            return TryResolveModWallDrop(x, y, wallType, out itemType);
        }

        private static bool TryInvokeKillTileGetItemDrops(
            int x,
            int y,
            Tile tile,
            out int dropItem,
            out int dropItemStack,
            out int secondaryItem,
            out int secondaryItemStack)
        {
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

        private static bool TryInvokeKillWallGetItemDrops(Tile tile, out int dropItem)
        {
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

        private static bool TryResolveModTileDrop(int x, int y, int tileType, out int itemType, out int stack)
        {
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

        private static bool TryResolveModWallDrop(int x, int y, int wallType, out int itemType)
        {
            itemType = 0;

            ModWall? modWall = WallLoader.GetWall(wallType);
            if (modWall is null)
                return false;

            int drop = 0;
            for (int i = 1; i < ItemLoader.ItemCount; i++)
            {
                Item item = new();
                item.SetDefaults(i);
                if (item.createWall == wallType)
                {
                    drop = i;
                    break;
                }
            }

            if (drop <= 0)
                return false;

            if (!modWall.Drop(x, y, ref drop) || drop <= 0)
                return false;

            itemType = drop;
            return true;
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

        private static void EnsureReflectionInitialized()
        {
            if (_reflectionInitialized)
                return;

            _reflectionInitialized = true;
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Type worldGen = typeof(WorldGen);

            _killTileGetItemDrops = worldGen
                .GetMethods(flags)
                .FirstOrDefault(method =>
                    method.Name == "KillTile_GetItemDrops"
                    && method.GetParameters().Length >= 6
                    && method.GetParameters()[2].ParameterType == typeof(Tile));

            _killWallGetItemDrops = worldGen
                .GetMethods(flags)
                .FirstOrDefault(method =>
                    method.Name == "KillWall_GetItemDrops"
                    && method.GetParameters().Length == 1
                    && method.GetParameters()[0].ParameterType == typeof(Tile));
        }
    }
}
