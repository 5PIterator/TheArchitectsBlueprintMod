using System;
using System.Collections.Generic;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.Debug;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace TheArchitectsBlueprintMod.Core.Layers
{

    internal static class TileComparison
    {
        /// <summary>Cut-at-source tiles use an empty <see cref="PasteTile.Source"/> and live <see cref="PasteTile.Target"/> at the capture area.</summary>
        public static bool IsSourceCutIntent(in PasteTile tile) =>
            tile.PayloadCell.HasTile() && !tile.Source.HasTile && tile.Target.HasTile;

        public static bool MatchForPaste(in PasteTile tile) =>
            IsSourceCutIntent(in tile) ? MatchSourceToTargetTileType(in tile) : MatchPayloadToTargetTileType(in tile);

        public static bool MatchPayloadToTargetTileType(in PasteTile tile)
        {
            if (tile.PayloadCell.IsEmpty)
                return !tile.Target.HasTile;

            Tile intent = tile.PayloadTile;
            if (intent.HasTile != tile.Target.HasTile) return false;
            if (!intent.HasTile) return !tile.Target.HasTile;
            return intent.TileType == tile.Target.TileType;
        }

        public static bool MatchSourceToTargetTileType(in PasteTile tile)
        {
            if (!tile.Source.HasTile) return !tile.Target.HasTile;
            return tile.Source.TileType == tile.Target.TileType;
        }

        public static bool MatchTileType(in PasteTile tile) => MatchForPaste(in tile);

        public static bool MatchExact(in PasteTile tile)
        {
            if (!MatchForPaste(in tile)) return false;
            if (IsSourceCutIntent(in tile))
            {
                if (!tile.Source.HasTile) return true;
                return tile.Source.TileType == tile.Target.TileType
                    && tile.Source.TileFrameX == tile.Target.TileFrameX
                    && tile.Source.TileFrameY == tile.Target.TileFrameY;
            }

            if (tile.PayloadCell.IsEmpty)
                return !tile.Target.HasTile;

            Tile intent = tile.PayloadTile;
            if (!intent.HasTile) return true;
            return intent.TileType == tile.Target.TileType
                && intent.TileFrameX == tile.Target.TileFrameX
                && intent.TileFrameY == tile.Target.TileFrameY;
        }
    }

    internal static class TileLoaderSupport
    {
        public static bool IsKnownTileType(int tileType) =>
            tileType >= 0 && tileType < TileLoader.TileCount;

        public static bool IsKnownTileType(ushort tileType) =>
            tileType < TileLoader.TileCount;

        public static bool IsReplaceAllowedAt(int x, int y, ushort newTileType, in PasteContext context)
        {
            Tile target = Main.tile[x, y];
            if (!target.HasTile || target.TileType == newTileType)
                return true;

            if (context.IsSweepCheat)
                return true;

            if (!context.UseReplacePlace)
                return false;

            if (!IsKnownTileType(target.TileType))
                return false;

            return TileLoader.CanReplace(x, y, target.TileType, newTileType);
        }

        public static bool IsCanPlaceAllowedAt(int x, int y, ushort tileType, in PasteContext context)
        {
            if (!IsKnownTileType(tileType) || !WorldGen.InWorld(x, y, 1))
                return false;

            if (!IsReplaceAllowedAt(x, y, tileType, in context))
                return false;

            if (context.IsSweepCheat)
                return true;

            return TileLoader.CanPlace(x, y, tileType);
        }

        public static void RunPostPlacementHooks(int x, int y)
        {
            if (!WorldGen.InWorld(x, y, 1))
                return;

            Tile placed = Main.tile[x, y];
            if (!placed.HasTile || !IsKnownTileType(placed.TileType))
                return;

            int tileType = placed.TileType;
            bool resetFrame = false;
            bool noBreak = false;
            TileLoader.TileFrame(x, y, tileType, ref resetFrame, ref noBreak);
            if (tileType >= TileID.Count)
                TileLoader.CheckModTile(x, y, tileType);
        }
    }

    internal static class FrameOperations
    {
        public static IReadOnlyList<Point>? GetFrameTiles(Point originRel, in Tile sourceTile, in PasteTileLookup lookup)
        {
            if (!sourceTile.HasTile || !TileLoaderSupport.IsKnownTileType(sourceTile.TileType))
                return null;

            int style = 0, alternate = 0;
            TileObjectData.GetTileInfo(sourceTile, ref style, ref alternate);
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

        public static IReadOnlyList<Blueprint.BlueprintCell>? GetFrameTilesAsBounded(Point originRel, in Tile sourceTile, in PasteTileLookup lookup)
        {
            var points = GetFrameTiles(originRel, sourceTile, lookup);
            if (points is null || points.Count == 0) return null;
            var list = new List<Blueprint.BlueprintCell>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                Point rel = points[i];
                var pt = lookup.GetPasteTile(rel);
                list.Add(new Blueprint.BlueprintCell(rel, pt.World, true));
            }
            return list;
        }

        /// <param name="anchorWorld">World position of the tile at <paramref name="originRel"/> (target paste coordinates).</param>
        public static IReadOnlyList<Blueprint.BlueprintCell>? GetFrameTilesAsBoundedFromOrigin(Point originRel, Point anchorWorld, in Tile sourceTile)
        {
            if (!sourceTile.HasTile || !TileLoaderSupport.IsKnownTileType(sourceTile.TileType))
                return null;

            int style = 0, alternate = 0;
            TileObjectData.GetTileInfo(sourceTile, ref style, ref alternate);
            if (style < 0)
                return [new Blueprint.BlueprintCell(originRel, anchorWorld, true)];

            var data = TileObjectData.GetTileData(sourceTile);
            if (data is null)
                return [new Blueprint.BlueprintCell(originRel, anchorWorld, true)];

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

            var list = new List<Blueprint.BlueprintCell>(w * h);
            for (int dy = 0; dy < h; dy++)
            {
                for (int dx = 0; dx < w; dx++)
                {
                    var rel = new Point(topLeftX + dx, topLeftY + dy);
                    var wxy = new Point(anchorWorld.X + rel.X - originRel.X, anchorWorld.Y + rel.Y - originRel.Y);
                    list.Add(new Blueprint.BlueprintCell(rel, wxy, true));
                }
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

        /// <summary>
        /// Checks if the tile type at a given style has a standard directional tile anchor.
        /// Mirrors <see cref="HasTileAnchor(Tile)"/> but works from a type and style rather than a placed tile instance.
        /// </summary>
        public static bool HasTileAnchor(int tileType, int style)
        {
            var data = TileObjectData.GetTileData(tileType, style);
            if (data is null) return false;
            return HasDirectionalAnchor(data) && !HasSpecialDirectionalAnchor(data);
        }

        /// <summary>
        /// Checks if the tile type at a given style requires a wall anchor.
        /// Mirrors <see cref="HasWallAnchor(Tile)"/> but works from a type and style rather than a placed tile instance.
        /// </summary>
        public static bool HasWallAnchor(int tileType, int style)
        {
            var data = TileObjectData.GetTileData(tileType, style);
            return data is not null && data.AnchorWall;
        }

        /// <summary>
        /// Checks if the tile type at a given style has a special or restricted anchor.
        /// Mirrors <see cref="HasOtherAnchor(Tile)"/> but works from a type and style rather than a placed tile instance.
        /// </summary>
        public static bool HasOtherAnchor(int tileType, int style)
        {
            var data = TileObjectData.GetTileData(tileType, style);
            if (data is null) return false;
            return HasSpecialDirectionalAnchor(data)
                || (data.AnchorValidTiles is { Length: > 0 })
                || (data.AnchorInvalidTiles is { Length: > 0 });
        }

        public static PasteTile GetPlacementOriginTile(IReadOnlyList<PasteTile> tiles) =>
            GetOriginTile(tiles, static tile => tile.PayloadCell.HasTile(), static tile => tile.PayloadCell.ToTile());

        public static PasteTile GetClearingOriginTile(IReadOnlyList<PasteTile> tiles) =>
            GetOriginTile(tiles, static tile => tile.Target.HasTile, static tile => tile.Target);

        private static PasteTile GetOriginTile(
            IReadOnlyList<PasteTile> tiles,
            Func<PasteTile, bool> hasTile,
            Func<PasteTile, Tile> getFramingTile)
        {
            if (tiles.Count <= 1)
                return tiles[0];

            int sampleIndex = -1;
            for (int i = 0; i < tiles.Count; i++)
            {
                if (hasTile(tiles[i]))
                {
                    sampleIndex = i;
                    break;
                }
            }

            if (sampleIndex < 0)
                return tiles[0];

            var samplePasteTile = tiles[sampleIndex];
            Tile sampleTile = getFramingTile(samplePasteTile);

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
                int wx = t.World.X, wy = t.World.Y;
                if (t.PayloadCell.HasTile() && Main.tile[wx, wy].HasTile)
                {
                    int idx = wx + wy * (int)Main.tile.Width;
                    var state = Main.tile.GetData<TileWallWireStateData>();
                    var b = t.PayloadCell.Block;
                    state[idx].TileFrameX = (short)b.FrameX;
                    state[idx].TileFrameY = (short)b.FrameY;
                }
            }

            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                TileLoaderSupport.RunPostPlacementHooks(t.World.X, t.World.Y);
                LayerSync.SyncTileSquare(t.World.X, t.World.Y);
            }
        }

        public static LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context, string? message = null)
        {
            if (tiles.Count == 0) return LayerResult.Ok();

            message ??= LayerLogFormat.FormatTile(tiles[0].Target);

            bool changed = false;
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                int wx = t.World.X, wy = t.World.Y;
                if (!Main.tile[wx, wy].HasTile)
                    continue;

                TileExecutor.EnqueueClearTile(wx, wy, in context);
                changed = true;
            }

            return changed ? LayerResult.Ok(message, LayerLogFormat.ClearedLabel) : LayerResult.Ok();
        }

        public static LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();

            var origin = GetPlacementOriginTile(tiles);
            int x = origin.World.X, y = origin.World.Y;
            if (!origin.PayloadCell.HasTile()) return LayerResult.Ok();

            string message = LayerLogFormat.FormatTile(origin.PayloadTile);
            string okLabel = LayerLogFormat.PlacedLabel(in context);

            ushort tileType = (ushort)origin.PayloadCell.Block.Type;
            if (!TileLoaderSupport.IsKnownTileType(tileType))
            {
                PastingDebug.LogPlacement(tileType, 0, null, x, y, false, "PlaceObject");
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            int style = origin.PayloadCell.Detail.Style;
            int alternate = origin.PayloadCell.Detail.Alternate;
            Tile dest = Main.tile[x, y];

            if (dest.HasTile && dest.TileType == tileType)
            {
                PasteFrame(tiles);
                return LayerResult.Ok(message, okLabel);
            }

            if (!TileLoaderSupport.IsReplaceAllowedAt(x, y, tileType, in context))
            {
                PastingDebug.LogPlacement(tileType, style, null, x, y, false, "PlaceObject");
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            if (context.UseReplacePlace && dest.HasTile)
                TileExecutor.EnqueueVoidKillTile(x, y, in context);

            if (!context.IsSweepCheat && !TileLoader.CanPlace(x, y, tileType))
            {
                PastingDebug.LogPlacement(tileType, style, null, x, y, false, "PlaceObject");
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            if (!TileExecutor.HasItemForTile(tileType, style, in context))
            {
                PastingDebug.LogPlacement(tileType, style, null, x, y, false, "PlaceObject");
                return LayerResult.Fail(LayerFailReason.MissingItems, message);
            }

            if (!TileExecutor.EnqueuePlaceObject(x, y, tileType, style, alternate, in context, consumeItem: true))
            {
                PastingDebug.LogPlacement(tileType, style, null, x, y, false, "PlaceObject");
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            PastingDebug.LogPlacement(tileType, style, null, x, y, true, "PlaceObject");
            return LayerResult.Ok(message, okLabel);
        }

        public static bool Consume(in PasteTile tile, in PasteContext context) => true;
    }

    internal static class TileOperations
    {
        public static LayerResult Clear(in PasteTile originTile, in PasteContext context, string? message = null)
        {
            message ??= LayerLogFormat.FormatTile(originTile.Target);
            int x = originTile.World.X, y = originTile.World.Y;
            if (!Main.tile[x, y].HasTile)
                return LayerResult.Ok();

            bool queued = TileExecutor.EnqueueClearTile(x, y, in context);
            return queued
                ? LayerResult.Ok(message, LayerLogFormat.ClearedLabel)
                : LayerResult.Fail(LayerFailReason.ClearBlocked, message);
        }

        public static LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();

            var origin = FrameOperations.GetPlacementOriginTile(tiles);
            int x = origin.World.X, y = origin.World.Y;
            if (!origin.PayloadCell.HasTile()) return LayerResult.Ok();

            ushort tileType = (ushort)origin.PayloadCell.Block.Type;
            if (!TileLoaderSupport.IsKnownTileType(tileType))
            {
                PastingDebug.LogPlacement(tileType, 0, null, x, y, false, "PlaceTile");
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, LayerLogFormat.FormatTile(origin.PayloadTile));
            }

            int style = origin.PayloadCell.Detail.Style;
            var baseTileId = InteractionMapProvider.GetRequiredBaseTile(tileType, style);
            PastingDebug.LogInteractionLookup(tileType, style, baseTileId, null);

            Tile dest = Main.tile[x, y];
            if (dest.HasTile && dest.TileType == tileType)
            {
                FrameOperations.PasteFrame(tiles);
                return LayerResult.Ok(
                    LayerLogFormat.FormatTile(origin.PayloadTile),
                    LayerLogFormat.PlacedLabel(in context));
            }

            return PlaceWithTile(origin, tiles, style, baseTileId, in context);
        }

        private static LayerResult PlaceWithTile(in PasteTile origin, IReadOnlyList<PasteTile> tiles, int style, int? baseTileId, in PasteContext context)
        {
            int x = origin.World.X, y = origin.World.Y;
            ushort tileType = (ushort)origin.PayloadCell.Block.Type;
            string message = LayerLogFormat.FormatTile(origin.PayloadTile);
            string okLabel = LayerLogFormat.PlacedLabel(in context);
            Tile dest = Main.tile[x, y];

            if (baseTileId.HasValue)
            {
                ushort baseType = (ushort)baseTileId.Value;
                if (!dest.HasTile || dest.TileType != baseType)
                {
                    if (!TileLoaderSupport.IsCanPlaceAllowedAt(x, y, baseType, in context))
                    {
                        PastingDebug.LogPlacement(tileType, style, baseTileId, x, y, false, "PlaceTile");
                        return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
                    }

                    if (!TileExecutor.HasItemForTile(baseType, 0, in context))
                    {
                        PastingDebug.LogPlacement(tileType, style, baseTileId, x, y, false, "PlaceTile");
                        return LayerResult.Fail(LayerFailReason.MissingItems, message);
                    }

                    if (!TileExecutor.EnqueuePlaceTile(x, y, baseType, 0, in context, forced: context.IsSweepCheat, consumeItem: true))
                    {
                        PastingDebug.LogPlacement(tileType, style, baseTileId, x, y, false, "PlaceTile");
                        return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
                    }

                    PastingDebug.LogPlacement(tileType, style, baseTileId, x, y, true, "PlaceBaseTile");
                    // Base-first conversion tiles (e.g., dirt -> grass) require a second pass:
                    // this pass lays support, next pass applies the conversion tile itself.
                    return LayerResult.OkWithRemaining(tiles, message, okLabel);
                }
            }
            else if (dest.HasTile && !context.IsSweepCheat && context.RequireItems && !context.UseReplacePlace)
            {
                PastingDebug.LogPlacement(tileType, style, baseTileId, x, y, false, "PlaceTile");
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            bool conversionBaseReady = baseTileId.HasValue
                && dest.HasTile
                && dest.TileType == (ushort)baseTileId.Value;

            if (!conversionBaseReady)
            {
                if (!TileLoaderSupport.IsReplaceAllowedAt(x, y, tileType, in context))
                {
                    PastingDebug.LogPlacement(tileType, style, baseTileId, x, y, false, "PlaceTile");
                    return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
                }

                if (context.UseReplacePlace && dest.HasTile && dest.TileType != tileType)
                    TileExecutor.EnqueueVoidKillTile(x, y, in context);

                if (!context.IsSweepCheat && !TileLoader.CanPlace(x, y, tileType))
                {
                    PastingDebug.LogPlacement(tileType, style, baseTileId, x, y, false, "PlaceTile");
                    return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
                }
            }

            if (!TileExecutor.HasItemForTile(tileType, style, in context))
            {
                PastingDebug.LogPlacement(tileType, style, baseTileId, x, y, false, "PlaceTile");
                return LayerResult.Fail(LayerFailReason.MissingItems, message);
            }

            bool forcePlace = context.IsSweepCheat || baseTileId.HasValue || context.UseReplacePlace;
            if (!TileExecutor.EnqueuePlaceTile(x, y, tileType, style, in context, forcePlace, consumeItem: true))
            {
                PastingDebug.LogPlacement(tileType, style, baseTileId, x, y, false, "PlaceTile");
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            PastingDebug.LogPlacement(tileType, style, baseTileId, x, y, true, "PlaceTile");
            return LayerResult.Ok(message, okLabel);
        }

        public static bool Consume(in PasteTile tile, in PasteContext context) => true;
    }

    internal static class DetailOperations
    {
        public static bool IsMechanismTileType(ushort tileType)
        {
            int count = TileLoader.TileCount;
            if (tileType >= count)
                return false;

            return TileID.Sets.IsAMechanism[tileType] || TileID.Sets.IsATrigger[tileType];
        }

        public static IReadOnlyList<int>? GetMechanismItems(in Tile payloadTile)
        {
            if (!payloadTile.HasTile || !IsMechanismTileType(payloadTile.TileType))
                return null;

            int style = 0, alternate = 0;
            TileObjectData.GetTileInfo(payloadTile, ref style, ref alternate);
            return InteractionMapProvider.GetTileItemIds(payloadTile.TileType, style);
        }

        public static IReadOnlyList<int>? GetMechanismItems(in PayloadCell payloadCell)
        {
            if (!payloadCell.HasTile() || !IsMechanismTileType((ushort)payloadCell.Block.Type))
                return null;

            int style = payloadCell.Detail.Style;
            return InteractionMapProvider.GetTileItemIds((ushort)payloadCell.Block.Type, style);
        }

        public static void WriteState(int x, int y, in TileWallWireStateData state)
        {
            Main.tile[x, y].Get<TileWallWireStateData>() = state;
        }

        /// <summary>Reads live <see cref="TileWallWireStateData"/>; commit mutations with <see cref="WriteState"/>.</summary>
        public static TileWallWireStateData ReadState(int x, int y) =>
            Main.tile[x, y].Get<TileWallWireStateData>();
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
