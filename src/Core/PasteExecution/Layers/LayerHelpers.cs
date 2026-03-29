using System;
using System.Collections.Generic;
using ArchitectsStencilMod.Core;
using ArchitectsStencilMod.Core.TileMapping;
using ArchitectsStencilMod.Utils;
using ArchitectsStencilMod.Debug;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace ArchitectsStencilMod.Core.Layers
{
    internal static class TileComparison
    {
        public static bool MatchTileType(in PasteTile tile)
        {
            if (tile.Source.HasTile != tile.Target.HasTile) return false;
            if (!tile.Source.HasTile) return true;
            return tile.Source.TileType == tile.Target.TileType;
        }

        public static bool MatchExact(in PasteTile tile)
        {
            if (!MatchTileType(tile)) return false;
            if (!tile.Source.HasTile) return true;
            return tile.Source.TileType == tile.Target.TileType
                && tile.Source.TileFrameX == tile.Target.TileFrameX
                && tile.Source.TileFrameY == tile.Target.TileFrameY;
        }
    }

    internal static class FrameOperations
    {
        public static IReadOnlyList<Point>? GetFrameTiles(Point originRel, in Tile sourceTile, in PasteLookupQuery lookup)
        {
            if (!sourceTile.HasTile || sourceTile.TileType >= TileLoader.TileCount)
                return null;

            int style = TileObjectData.GetTileStyle(sourceTile);
            if (style < 0)
                return [originRel];

            var data = TileObjectData.GetTileData(sourceTile);
            if (data is null)
                return [originRel];

            int w = Math.Max(1, data.Width);
            int h = Math.Max(1, data.Height);

            int subX = ResolveSubX(sourceTile.TileFrameX, data, w);
            int subY = ResolveSubY(sourceTile.TileFrameY, data, h);

            int topLeftX = originRel.X - subX;
            int topLeftY = originRel.Y - subY;

            var list = new List<Point>(w * h);
            for (int dy = 0; dy < h; dy++)
            {
                for (int dx = 0; dx < w; dx++)
                {
                    Point relative = new(topLeftX + dx, topLeftY + dy);
                    if (!lookup.IsInPasteScope(relative))
                        return null;

                    list.Add(relative);
                }
            }
            return list;
        }

        public static IReadOnlyList<Blueprint.BoundedTile>? GetFrameTilesAsBounded(Point originRel, in Tile sourceTile, in PasteLookupQuery lookup)
        {
            var points = GetFrameTiles(originRel, sourceTile, lookup);
            if (points is null || points.Count == 0) return null;
            var list = new List<Blueprint.BoundedTile>(points.Count);
            for (int i = 0; i < points.Count; i++)
                list.Add(new Blueprint.BoundedTile(points[i], selected: true));
            return list;
        }

        public static IReadOnlyList<Blueprint.BoundedTile>? GetFrameTilesAsBoundedFromOrigin(Point originRel, in Tile sourceTile)
        {
            if (!sourceTile.HasTile || sourceTile.TileType >= TileLoader.TileCount)
                return null;

            int style = TileObjectData.GetTileStyle(sourceTile);
            if (style < 0)
                return [new Blueprint.BoundedTile(originRel, selected: true)];

            var data = TileObjectData.GetTileData(sourceTile);
            if (data is null)
                return [new Blueprint.BoundedTile(originRel, selected: true)];

            int w = Math.Max(1, data.Width);
            int h = Math.Max(1, data.Height);
            var origin = data.Origin;
            int ox = Math.Clamp(origin.X, 0, w - 1);
            int oy = Math.Clamp(origin.Y, 0, h - 1);

            int subX = ResolveSubX(sourceTile.TileFrameX, data, w);
            int subY = ResolveSubY(sourceTile.TileFrameY, data, h);
            if (subX != ox || subY != oy)
                return null;

            int topLeftX = originRel.X - ox;
            int topLeftY = originRel.Y - oy;

            var list = new List<Blueprint.BoundedTile>(w * h);
            for (int dy = 0; dy < h; dy++)
            {
                for (int dx = 0; dx < w; dx++)
                    list.Add(new Blueprint.BoundedTile(new Point(topLeftX + dx, topLeftY + dy), selected: true));
            }
            return list;
        }

        private static int ResolveSubX(int tileFrameX, TileObjectData data, int w)
        {
            int coordFullWidth = Math.Max(1, data.CoordinateFullWidth);
            int partFrameX = ((tileFrameX % coordFullWidth) + coordFullWidth) % coordFullWidth;
            int partWidth = data.CoordinateWidth + data.CoordinatePadding;
            if (partWidth <= 0)
                partWidth = 18;

            return Math.Clamp(partFrameX / partWidth, 0, w - 1);
        }

        private static int ResolveSubY(int tileFrameY, TileObjectData data, int h)
        {
            int coordFullHeight = Math.Max(1, data.CoordinateFullHeight);
            int partFrameY = ((tileFrameY % coordFullHeight) + coordFullHeight) % coordFullHeight;
            var heights = data.CoordinateHeights;
            int pad = data.CoordinatePadding;
            if (heights is { Length: > 0 } && heights.Length >= h)
            {
                int partY = 0;
                int remaining = partFrameY;
                while (partY + 1 < h && remaining - heights[partY] - pad >= 0)
                {
                    remaining -= heights[partY] + pad;
                    partY++;
                }
                return Math.Clamp(partY, 0, h - 1);
            }

            int coordH = h > 0 ? Math.Max(1, coordFullHeight / h) : 18;
            return Math.Clamp(partFrameY / coordH, 0, h - 1);
        }

        private static bool HasDirectionalAnchor(TileObjectData data) =>
            data.AnchorTop != default || data.AnchorBottom != default
            || data.AnchorLeft != default || data.AnchorRight != default;

        private static bool UsesSpecialAnchorType(AnchorData anchor)
        {
            if (anchor == default)
                return false;

            const AnchorType StandardSupportMask =
                AnchorType.SolidTile |
                AnchorType.SolidWithTop |
                AnchorType.SolidSide |
                AnchorType.SolidBottom |
                AnchorType.EmptyTile;

            AnchorType specialTypes = anchor.type & ~StandardSupportMask;
            return specialTypes != AnchorType.None;
        }

        private static bool HasSpecialDirectionalAnchor(TileObjectData data) =>
            UsesSpecialAnchorType(data.AnchorTop)
            || UsesSpecialAnchorType(data.AnchorBottom)
            || UsesSpecialAnchorType(data.AnchorLeft)
            || UsesSpecialAnchorType(data.AnchorRight);

        public static bool HasTileAnchor(Tile tile)
        {
            if (!tile.HasTile) return false;
            var data = TileObjectData.GetTileData(tile);
            if (data is null) return false;
            return HasDirectionalAnchor(data) && !HasSpecialDirectionalAnchor(data);
        }

        public static bool HasWallAnchor(Tile tile)
        {
            if (!tile.HasTile) return false;
            var data = TileObjectData.GetTileData(tile);
            return data is not null && data.AnchorWall;
        }

        public static bool HasOtherAnchor(Tile tile)
        {
            if (!tile.HasTile) return false;
            var data = TileObjectData.GetTileData(tile);
            if (data is null) return false;
            return HasSpecialDirectionalAnchor(data)
                || (data.AnchorValidTiles is { Length: > 0 })
                || (data.AnchorInvalidTiles is { Length: > 0 });
        }

        public static PasteTile GetPlacementOriginTile(IReadOnlyList<PasteTile> tiles) =>
            GetOriginTile(tiles, static tile => tile.Source);

        public static PasteTile GetClearingOriginTile(IReadOnlyList<PasteTile> tiles) =>
            GetOriginTile(tiles, static tile => tile.Target);

        private static PasteTile GetOriginTile(IReadOnlyList<PasteTile> tiles, Func<PasteTile, Tile> tileSelector)
        {
            if (tiles.Count <= 1)
                return tiles[0];

            int sampleIndex = -1;
            for (int i = 0; i < tiles.Count; i++)
            {
                if (tileSelector(tiles[i]).HasTile)
                {
                    sampleIndex = i;
                    break;
                }
            }

            if (sampleIndex < 0)
                return tiles[0];

            var samplePasteTile = tiles[sampleIndex];
            Tile sampleTile = tileSelector(samplePasteTile);

            var data = TileObjectData.GetTileData(sampleTile);
            if (data is null)
                return tiles[0];

            int w = Math.Max(1, data.Width);
            int h = Math.Max(1, data.Height);
            var origin = data.Origin;
            int ox = Math.Clamp(origin.X, 0, w - 1);
            int oy = Math.Clamp(origin.Y, 0, h - 1);

            int subX = ResolveSubX(sampleTile.TileFrameX, data, w);
            int subY = ResolveSubY(sampleTile.TileFrameY, data, h);
            Point topLeftRel = new(samplePasteTile.Relative.X - subX, samplePasteTile.Relative.Y - subY);
            Point expectedOriginRel = new(topLeftRel.X + ox, topLeftRel.Y + oy);

            for (int i = 0; i < tiles.Count; i++)
            {
                if (tiles[i].Relative == expectedOriginRel)
                    return tiles[i];
            }

            return tiles[sampleIndex];
        }

        public static void PasteFrame(IReadOnlyList<PasteTile> tiles)
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                if (t.Source.HasTile && Main.tile[t.X, t.Y].HasTile)
                {
                    int idx = t.X + t.Y * (int)Main.tile.Width;
                    var state = Main.tile.GetData<TileWallWireStateData>();
                    state[idx].TileFrameX = t.Source.TileFrameX;
                    state[idx].TileFrameY = t.Source.TileFrameY;
                }
            }

            for (int i = 0; i < tiles.Count; i++)
                LayerSync.SyncTileSquare(tiles[i].X, tiles[i].Y);
        }

        public static LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();

            bool changed = false;
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                if (!Main.tile[t.X, t.Y].HasTile)
                    continue;

                SuppressTileDrop.KillTile(t.X, t.Y);
                changed = true;
            }

            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                if (Main.tile[t.X, t.Y].HasTile)
                    return LayerResult.Fail(t.World);
            }

            return changed ? LayerResult.Ok(tiles[0].World) : LayerResult.Ok();
        }

        public static LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();

            var origin = GetPlacementOriginTile(tiles);
            int x = origin.X, y = origin.Y;
            if (!origin.Source.HasTile) return LayerResult.Ok();

            ushort tileType = origin.Source.TileType;
            if (tileType >= TileLoader.TileCount)
            {
                PastingDebug.LogPlacement(tileType, 0, null, x, y, false, "PlaceObject");
                return LayerResult.Fail(origin.World);
            }

            int style = 0;
            int alternate = 0;
            TileObjectData.GetTileInfo(origin.Source, ref style, ref alternate);
            Tile dest = Main.tile[x, y];

            if (dest.HasTile && dest.TileType == tileType)
            {
                PasteFrame(tiles);
                return LayerResult.Ok(origin.World);
            }

            if (context.NeedsItems && context.Slots != null)
            {
                int slotHandle = context.Slots.FindTileSlot(tileType, style);
                if (slotHandle < 0)
                {
                    PastingDebug.LogPlacement(tileType, style, null, x, y, false, "PlaceObject");
                    return LayerResult.Fail(origin.World);
                }
                style = Math.Max(0, context.Slots.GetPlaceStyle(slotHandle));
            }
            else
            {
                style = Math.Max(0, style);
            }

            if (!WorldGen.PlaceObject(x, y, tileType, mute: true, style, alternate, random: -1, direction: -1))
            {
                PastingDebug.LogPlacement(tileType, style, null, x, y, false, "PlaceObject");
                return LayerResult.Fail(origin.World);
            }

            LayerSync.SyncTileSquare(x, y);
            PastingDebug.LogPlacement(tileType, style, null, x, y, true, "PlaceObject");
            return LayerResult.Ok(origin.World);
        }

        public static bool Consume(in PasteTile tile, in PasteContext context)
        {
            if (!tile.Source.HasTile || !context.NeedsItems || context.Slots is null) return true;

            ushort tileType = tile.Source.TileType;
            int style = TileObjectData.GetTileStyle(tile.Source);
            ushort placedType = (ushort)Main.tile[tile.X, tile.Y].TileType;

            int lookupStyle = placedType == tileType ? (style >= 0 ? style : 0) : 0;
            int slotHandle = context.Slots.FindTileSlot(placedType, lookupStyle);
            if (slotHandle < 0) return false;
            context.Slots.ConsumeAt(slotHandle);
            return true;
        }
    }

    internal static class TileOperations
    {
        public static LayerResult Clear(in PasteTile originTile, in PasteContext context)
        {
            int x = originTile.X, y = originTile.Y;
            if (!Main.tile[x, y].HasTile)
                return LayerResult.Ok();

            SuppressTileDrop.KillTile(x, y);
            return Main.tile[x, y].HasTile ? LayerResult.Fail(originTile.World) : LayerResult.Ok(originTile.World);
        }

        public static LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();

            var origin = FrameOperations.GetPlacementOriginTile(tiles);
            int x = origin.X, y = origin.Y;
            if (!origin.Source.HasTile) return LayerResult.Ok();

            ushort tileType = origin.Source.TileType;
            if (tileType >= TileLoader.TileCount)
            {
                PastingDebug.LogPlacement(tileType, 0, null, x, y, false, "PlaceTile");
                return LayerResult.Fail(origin.World);
            }

            int style = TileObjectData.GetTileStyle(origin.Source);
            var baseTileId = InteractionMapProvider.GetRequiredBaseTile(tileType, style);
            PastingDebug.LogInteractionLookup(tileType, style, baseTileId, null);

            Tile dest = Main.tile[x, y];
            if (dest.HasTile && dest.TileType == tileType)
            {
                FrameOperations.PasteFrame(tiles);
                return LayerResult.Ok(origin.World);
            }

            return PlaceWithTile(origin, tiles, style, baseTileId, in context);
        }

        private static LayerResult PlaceWithTile(in PasteTile origin, IReadOnlyList<PasteTile> tiles, int style, int? baseTileId, in PasteContext context)
        {
            int x = origin.X, y = origin.Y;
            ushort tileType = origin.Source.TileType;
            Tile dest = Main.tile[x, y];

            if (baseTileId.HasValue)
            {
                ushort baseType = (ushort)baseTileId.Value;
                if (!dest.HasTile || dest.TileType != baseType)
                {
                    if (context.NeedsItems && context.Slots != null)
                    {
                        int slotHandle = context.Slots.FindTileSlot(baseType, 0);
                        if (slotHandle < 0)
                        {
                            PastingDebug.LogPlacement(tileType, style, baseTileId, x, y, false, "PlaceTile");
                            return LayerResult.Fail(origin.World);
                        }
                        WorldGen.PlaceTile(x, y, baseType, mute: false, forced: context.CheatSweep, plr: context.Player.whoAmI, style: 0);
                        if (!Main.tile[x, y].HasTile || Main.tile[x, y].TileType != baseType)
                        {
                            PastingDebug.LogPlacement(tileType, style, baseTileId, x, y, false, "PlaceTile");
                            return LayerResult.Fail(origin.World);
                        }
                        context.Slots.ConsumeAt(slotHandle);
                    }
                    else
                    {
                        WorldGen.PlaceTile(x, y, baseType, mute: false, forced: context.CheatSweep, plr: context.Player.whoAmI, style: 0);
                        if (!Main.tile[x, y].HasTile || Main.tile[x, y].TileType != baseType)
                        {
                            PastingDebug.LogPlacement(tileType, style, baseTileId, x, y, false, "PlaceTile");
                            return LayerResult.Fail(origin.World);
                        }
                    }
                    LayerSync.SyncTileSquare(x, y);
                    return LayerResult.Fail(origin.World);
                }
            }
            else if (dest.HasTile && !context.CheatSweep && context.NeedsItems)
            {
                PastingDebug.LogPlacement(tileType, style, baseTileId, x, y, false, "PlaceTile");
                return LayerResult.Fail(origin.World);
            }

            int placeStyle = Math.Max(0, style);
            if (context.NeedsItems && context.Slots != null)
            {
                int slotHandle = context.Slots.FindTileSlot(tileType, style);
                if (slotHandle < 0)
                {
                    PastingDebug.LogPlacement(tileType, style, baseTileId, x, y, false, "PlaceTile");
                    return LayerResult.Fail(origin.World);
                }
                placeStyle = Math.Max(0, context.Slots.GetPlaceStyle(slotHandle));
            }

            bool forcePlace = context.CheatSweep || baseTileId.HasValue;
            if (!WorldGen.PlaceTile(x, y, tileType, mute: false, forced: forcePlace, plr: context.Player.whoAmI, style: placeStyle))
            {
                PastingDebug.LogPlacement(tileType, style, baseTileId, x, y, false, "PlaceTile");
                return LayerResult.Fail(origin.World);
            }

            FrameOperations.PasteFrame(tiles);
            PastingDebug.LogPlacement(tileType, style, baseTileId, x, y, true, "PlaceTile");
            return LayerResult.Ok(origin.World);
        }

        public static bool Consume(in PasteTile tile, in PasteContext context)
        {
            if (!tile.Source.HasTile || !context.NeedsItems || context.Slots is null) return true;

            ushort tileType = tile.Source.TileType;
            int style = TileObjectData.GetTileStyle(tile.Source);
            ushort placedType = (ushort)Main.tile[tile.X, tile.Y].TileType;

            int lookupStyle = placedType == tileType ? (style >= 0 ? style : 0) : 0;
            int slotHandle = context.Slots.FindTileSlot(placedType, lookupStyle);
            if (slotHandle < 0) return false;
            context.Slots.ConsumeAt(slotHandle);
            return true;
        }
    }

    internal static class ModifierOperations
    {
        public static bool IsMechanismTileType(ushort tileType)
        {
            int count = TileLoader.TileCount;
            if (tileType >= count)
                return false;

            return TileID.Sets.IsAMechanism[tileType] || TileID.Sets.IsATrigger[tileType];
        }

        public static IReadOnlyList<int>? GetMechanismItems(in Tile sourceTile)
        {
            if (!sourceTile.HasTile || !IsMechanismTileType(sourceTile.TileType))
                return null;

            int style = TileObjectData.GetTileStyle(sourceTile);
            return InteractionMapProvider.GetTileItemIds(sourceTile.TileType, style);
        }

        public static ref TileWallWireStateData GetState(int x, int y)
        {
            int idx = x + y * (int)Main.tile.Width;
            return ref Main.tile.GetData<TileWallWireStateData>()[idx];
        }
    }

    internal static class LayerSync
    {
        public static void SyncTileSquare(int x, int y)
        {
            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendTileSquare(-1, x, y, 1, 1);
        }
    }
}
