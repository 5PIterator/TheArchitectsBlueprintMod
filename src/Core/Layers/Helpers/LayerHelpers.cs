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
using Terraria.ModLoader.Default;
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
            if (intent.TileType == tile.Target.TileType)
                return true;

            // Treat a placed fake (e.g. Echo rubble) as satisfying the natural payload main.
            int style = tile.PayloadCell.ResolvePlacementStyle();
            InteractionEntry? entry = InteractionMapProvider.GetFirstTileInteraction(intent.TileType, style);
            return entry?.FakeId is int fakeId && fakeId == tile.Target.TileType;
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

        /// <summary>True when the tile id is out of range or a tModLoader Unloaded placeholder.</summary>
        public static bool IsUnknownTileType(ushort tileType)
        {
            // --- Out-of-range ids, or mod slots with no ModTile / UnloadedTile subclass.
            if (tileType >= TileLoader.TileCount)
                return true;
            if (tileType < TileID.Count)
                return false;
            return TileLoader.GetTile(tileType) is UnloadedTile or null;
        }

        /// <summary>True when the wall id is out of range or a tModLoader Unloaded placeholder.</summary>
        public static bool IsUnknownWallType(ushort wallType)
        {
            // --- Non-empty walls that are out of range or UnloadedWall / missing ModWall.
            if (wallType == WallID.None)
                return false;
            if (wallType >= WallLoader.WallCount)
                return true;
            if (wallType < WallID.Count)
                return false;
            return WallLoader.GetWall(wallType) is UnloadedWall or null;
        }

        /// <summary>Returns a safe tML unloaded tile type for sweep/sanitize writes.</summary>
        public static ushort ResolveUnloadableTileType(ushort tileType)
        {
            // --- Preserve existing Unloaded* tiles; map everything else unknown to UnloadedSolidTile.
            if (IsKnownTileType(tileType) && TileLoader.GetTile(tileType) is UnloadedTile)
                return tileType;
            return (ushort)ModContent.TileType<UnloadedSolidTile>();
        }

        /// <summary>Returns a safe tML unloaded wall type for sweep/sanitize writes.</summary>
        public static ushort ResolveUnloadableWallType(ushort wallType)
        {
            // --- Preserve existing UnloadedWall; map everything else unknown to UnloadedWall.
            if (wallType != WallID.None
                && wallType < WallLoader.WallCount
                && WallLoader.GetWall(wallType) is UnloadedWall)
                return wallType;
            return (ushort)ModContent.WallType<UnloadedWall>();
        }

        public static bool IsReplaceAllowedAt(int x, int y, ushort newTileType, in PasteContext context)
        {
            Tile target = Main.tile[x, y];
            if (!target.HasTile || target.TileType == newTileType)
                return true;

            if (context.IsSweep)
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

            if (context.IsSweep)
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

        /// <summary>
        /// Lists every world cell of a PlaceObject footprint when <paramref name="originWorld"/> is the TileObjectData.Origin cell.
        /// </summary>
        public static IReadOnlyList<Point> EnumerateFootprintWorldCellsFromPlaceOrigin(
            Point originWorld,
            int tileType,
            int style,
            int alternate)
        {
            // --- Origin is known from PlaceObject; subtract data.Origin to get top-left, then emit width×height.

            if (!WorldGen.InWorld(originWorld.X, originWorld.Y))
                return [];

            TileObjectData? data = TileObjectData.GetTileData(tileType, style, alternate)
                ?? TileObjectData.GetTileData(tileType, style);
            if (data is null)
                return [originWorld];

            int w = Math.Max(1, data.Width);
            int h = Math.Max(1, data.Height);
            int ox = Math.Clamp(data.Origin.X, 0, w - 1);
            int oy = Math.Clamp(data.Origin.Y, 0, h - 1);
            int topLeftX = originWorld.X - ox;
            int topLeftY = originWorld.Y - oy;
            return BuildFootprintCells(topLeftX, topLeftY, w, h, fallback: originWorld);
        }

        /// <summary>Lists every world cell in the multitile footprint containing <paramref name="worldX"/>,<paramref name="worldY"/>.</summary>
        public static IReadOnlyList<Point> EnumerateFootprintWorldCells(int worldX, int worldY)
        {
            // --- Derive top-left from TileObjectData frames, then emit width×height world points.

            // Fall back to the single sample cell when out of world or not a framed object.
            if (!WorldGen.InWorld(worldX, worldY))
                return [];

            Tile tile = Framing.GetTileSafely(worldX, worldY);
            if (!tile.HasTile || !TileLoaderSupport.IsKnownTileType(tile.TileType))
                return [new Point(worldX, worldY)];

            TileObjectData? data = TileObjectData.GetTileData(tile);
            if (data is null)
                return [new Point(worldX, worldY)];

            // Resolve this cell's offset within the frame, then expand to the full footprint.
            int w = Math.Max(1, data.Width);
            int h = Math.Max(1, data.Height);
            int subX = ResolveSubX(tile.TileFrameX, data, w);
            int subY = ResolveSubY(tile.TileFrameY, data, h);
            int topLeftX = worldX - subX;
            int topLeftY = worldY - subY;
            return BuildFootprintCells(topLeftX, topLeftY, w, h, fallback: new Point(worldX, worldY));
        }

        private static IReadOnlyList<Point> BuildFootprintCells(int topLeftX, int topLeftY, int w, int h, Point fallback)
        {
            // --- Emit in-world unique cells covering the rectangle; fall back if none survive.

            var cells = new List<Point>(w * h);
            var seen = new HashSet<Point>();
            for (int dy = 0; dy < h; dy++)
            {
                for (int dx = 0; dx < w; dx++)
                {
                    var cell = new Point(topLeftX + dx, topLeftY + dy);
                    if (!WorldGen.InWorld(cell.X, cell.Y) || !seen.Add(cell))
                        continue;
                    cells.Add(cell);
                }
            }

            return cells.Count > 0 ? cells : [fallback];
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

        /// <summary>Returns true when object data declares any tile, wall, or restricted placement anchor.</summary>
        public static bool HasFrameAnchor(Tile tile) =>
            HasTileAnchor(tile) || HasWallAnchor(tile) || HasOtherAnchor(tile);

        /// <summary>Returns true when the type/style declares any tile, wall, or restricted placement anchor.</summary>
        public static bool HasFrameAnchor(int tileType, int style) =>
            HasTileAnchor(tileType, style) || HasWallAnchor(tileType, style) || HasOtherAnchor(tileType, style);

        /// <summary>Returns true when TileObjectData defines a multi-cell placement footprint.</summary>
        public static bool IsMultiCellObject(int tileType, int style)
        {
            // --- Multi-cell objects need PlaceObject and Frame ordering regardless of anchors.
            var data = TileObjectData.GetTileData(tileType, style);
            return data is not null && (data.Width > 1 || data.Height > 1);
        }

        /// <summary>
        /// Returns true for tiles whose placement depends on anchors, multi-cell structure, or multiple placement styles.
        /// Excludes ordinary 1×1 blocks that only auto-frame; does not use solidity.
        /// </summary>
        public static bool IsFrameClassification(Tile tile)
        {
            // --- Prefer live style-aware anchors, then type+resolved-style dependency checks.
            if (!tile.HasTile || !IsFrameEligibleTileType(tile.TileType))
                return false;

            // Live TileObjectData already encodes the placed style/alternate.
            if (HasFrameAnchor(tile))
                return true;

            // Resolve style from frames for multi-style and multi-cell checks.
            int style = 0, alternate = 0;
            TileObjectData.GetTileInfo(tile, ref style, ref alternate);
            return MatchesFramePlacementCharacteristics(tile.TileType, style);
        }

        /// <summary>
        /// Type-only Frame classification using <paramref name="style"/> (defaults to 0 when style is unknown).
        /// </summary>
        public static bool IsFrameClassification(int tileType, int style = 0)
        {
            // --- Known non-falling tiles with dependency, footprint, or multi-style placement needs.
            if (!IsFrameEligibleTileType(tileType))
                return false;

            return MatchesFramePlacementCharacteristics(tileType, style);
        }

        /// <summary>Returns true when type/style has anchors, a multi-cell footprint, or multiple placement styles.</summary>
        public static bool MatchesFramePlacementCharacteristics(int tileType, int style)
        {
            // --- Dependency anchors, multi-cell footprints, or multi-style interaction-map entries.
            if (HasFrameAnchor(tileType, style))
                return true;

            if (InteractionMapProvider.HasMultipleTilePlacementStyles((ushort)tileType))
                return true;

            return IsMultiCellObject(tileType, style);
        }

        private static bool IsFrameEligibleTileType(int tileType) =>
            TileLoaderSupport.IsKnownTileType(tileType)
            && !TileLoaderSupport.IsUnknownTileType((ushort)tileType)
            && !TileID.Sets.Falling[tileType];

        public static PasteTile GetPlacementOriginTile(IReadOnlyList<PasteTile> tiles) =>
            GetOriginTile(
                tiles,
                static tile => tile.PayloadCell.HasTile(),
                static tile => tile.PayloadCell.ToTile(),
                forClear: false);

        public static PasteTile GetClearingOriginTile(IReadOnlyList<PasteTile> tiles) =>
            GetOriginTile(
                tiles,
                static tile => tile.Target.HasTile,
                static tile => tile.Target,
                forClear: true);

        private static PasteTile GetOriginTile(
            IReadOnlyList<PasteTile> tiles,
            Func<PasteTile, bool> hasTile,
            Func<PasteTile, Tile> getFramingTile,
            bool forClear)
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
            FramePlacementVariant variant = ResolveFramePlacementVariant(in samplePasteTile, forClear);
            TileObjectData? data = variant.Data ?? TileObjectData.GetTileData(sampleTile);
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

        /// <summary>Resolved placeStyle, alternate, origin, and CanPlace direction for a framed object.</summary>
        internal readonly struct FramePlacementVariant
        {
            public int Style { get; }
            public int Alternate { get; }
            public Point16 Origin { get; }
            public int Direction { get; }
            public TileObjectData? Data { get; }

            public FramePlacementVariant(int style, int alternate, Point16 origin, int direction, TileObjectData? data)
            {
                Style = style;
                Alternate = alternate;
                Origin = origin;
                Direction = direction;
                Data = data;
            }
        }

        /// <summary>One logical frame object: origin world point and every footprint cell in the batch.</summary>
        internal readonly struct FrameCandidate
        {
            public Point OriginWorld { get; }
            public PasteTile OriginTile { get; }
            public IReadOnlyList<PasteTile> Tiles { get; }

            public FrameCandidate(Point originWorld, PasteTile originTile, IReadOnlyList<PasteTile> tiles)
            {
                OriginWorld = originWorld;
                OriginTile = originTile;
                Tiles = tiles;
            }
        }

        /// <summary>Resolves placeStyle, alternate, origin, and direction from frames and TileObjectData alternates.</summary>
        public static FramePlacementVariant ResolveFramePlacementVariant(in PasteTile tile, bool forClear)
        {
            // --- Prefer payload style/alt; for clears fall back to live GetTileInfo, then recover from frames.
            if (forClear)
            {
                Tile framing = tile.Target;
                if (!framing.HasTile || !TileLoaderSupport.IsKnownTileType(framing.TileType))
                    return default;

                int infoStyle = 0, infoAlternate = 0;
                TileObjectData.GetTileInfo(framing, ref infoStyle, ref infoAlternate);
                return ResolveFramePlacementVariant(
                    framing.TileType,
                    framing.TileFrameX,
                    framing.TileFrameY,
                    Math.Max(0, infoStyle),
                    Math.Max(0, infoAlternate));
            }

            if (!tile.PayloadCell.HasTile())
                return default;

            return ResolveFramePlacementVariant(tile.PayloadCell);
        }

        /// <summary>Resolves placeStyle, alternate, origin, and direction from a payload cell snapshot.</summary>
        public static FramePlacementVariant ResolveFramePlacementVariant(in PayloadCell cell)
        {
            // --- Use cached style/alt plus frames so mis-captured orientation-as-style still recovers.
            if (!cell.HasTile())
                return default;

            return ResolveFramePlacementVariant(
                cell.Block.Type,
                cell.Block.FrameX,
                cell.Block.FrameY,
                cell.ResolvePlacementStyle(),
                cell.Detail.Alternate);
        }

        /// <summary>Resolves placeStyle, alternate, origin, and direction for a tile type and frame layout.</summary>
        public static FramePlacementVariant ResolveFramePlacementVariant(
            int tileType,
            short frameX,
            short frameY,
            int cachedStyle,
            int cachedAlternate)
        {
            // --- Prefer a frame-matching alternate; fix vanilla Signs-style orientation-as-style captures.
            if (!TileLoaderSupport.IsKnownTileType(tileType))
                return default;

            int placeStyle = ResolveAuthoritativePlaceStyle(tileType, cachedStyle, cachedAlternate);
            if (TryMatchFramePlacementVariant(tileType, placeStyle, frameX, frameY, out FramePlacementVariant matched))
                return matched;

            // Cached orientation-as-style may only match under placeStyle 0 (vanilla Signs / TatteredWoodSign).
            if (placeStyle != 0
                && TryMatchFramePlacementVariant(tileType, placeStyle: 0, frameX, frameY, out matched))
            {
                return matched;
            }

            TileObjectData? data = TileObjectData.GetTileData(tileType, placeStyle, cachedAlternate)
                ?? TileObjectData.GetTileData(tileType, cachedStyle, cachedAlternate)
                ?? TileObjectData.GetTileData(tileType, placeStyle);
            if (data is null)
                return new FramePlacementVariant(placeStyle, cachedAlternate, Point16.Zero, -1, null);

            return new FramePlacementVariant(
                placeStyle,
                cachedAlternate,
                data.Origin,
                ResolvePlacementDirection(data),
                data);
        }

        /// <summary>Returns the item placeStyle when orientation was mis-captured as style for multi-alternate tiles.</summary>
        private static int ResolveAuthoritativePlaceStyle(int tileType, int cachedStyle, int cachedAlternate)
        {
            // --- Drop-item placeStyle wins; otherwise keep cached style unless it indexes an alternate of style 0.
            InteractionEntry? interaction = InteractionMapProvider.GetFirstTileInteraction((ushort)tileType, cachedStyle);
            if (interaction?.ItemId is int itemId && itemId > 0
                && ContentSamples.ItemsByType.TryGetValue(itemId, out Item? sample)
                && sample is not null
                && sample.createTile == tileType
                && sample.placeStyle >= 0)
            {
                return sample.placeStyle;
            }

            TileObjectData? atCached = TileObjectData.GetTileData(tileType, cachedStyle, cachedAlternate);
            TileObjectData? baseAtZero = TileObjectData.GetTileData(tileType, 0);
            if (baseAtZero is { AlternatesCount: > 0 }
                && cachedAlternate == 0
                && cachedStyle > 0
                && cachedStyle <= baseAtZero.AlternatesCount
                && (atCached is null || atCached.AlternatesCount == 0))
            {
                return 0;
            }

            return Math.Max(0, cachedStyle);
        }

        /// <summary>Walks style alternates and returns the first whose sprite column matches the captured frames.</summary>
        private static bool TryMatchFramePlacementVariant(
            int tileType,
            int placeStyle,
            short frameX,
            short frameY,
            out FramePlacementVariant variant)
        {
            // --- Compare each alternate's Style column to the frame's style index.
            variant = default;
            TileObjectData? baseData = TileObjectData.GetTileData(tileType, placeStyle);
            if (baseData is null)
                return false;

            int alternateCount = baseData.AlternatesCount;
            for (int alternate = 0; alternate <= alternateCount; alternate++)
            {
                TileObjectData? data = TileObjectData.GetTileData(tileType, placeStyle, alternate);
                if (data is null || !FramesMatchAlternateStyle(data, placeStyle, frameX, frameY))
                    continue;

                variant = new FramePlacementVariant(
                    placeStyle,
                    alternate,
                    data.Origin,
                    ResolvePlacementDirection(data),
                    data);
                return true;
            }

            return false;
        }

        /// <summary>Returns true when frame X/Y fall in this alternate's style sprite column or row.</summary>
        private static bool FramesMatchAlternateStyle(TileObjectData data, int placeStyle, short frameX, short frameY)
        {
            // --- Derive the spritesheet style index from frames and compare to placeStyle * multiplier + Style.
            int multiplier = data.StyleMultiplier > 0 ? data.StyleMultiplier : 1;
            int expectedStyleIndex = placeStyle * multiplier + data.Style;
            if (data.StyleHorizontal)
            {
                int fullWidth = Math.Max(1, data.CoordinateFullWidth);
                int frameStyleIndex = Math.Abs((int)frameX) / fullWidth;
                return frameStyleIndex == expectedStyleIndex;
            }

            int fullHeight = Math.Max(1, data.CoordinateFullHeight);
            int frameStyleRow = Math.Abs((int)frameY) / fullHeight;
            return frameStyleRow == expectedStyleIndex;
        }

        /// <summary>Maps TileObjectData.Direction to Player.direction for TileObject.CanPlace.</summary>
        private static int ResolvePlacementDirection(TileObjectData data) =>
            data.Direction switch
            {
                TileObjectDirection.PlaceLeft => -1,
                TileObjectDirection.PlaceRight => 1,
                _ => -1,
            };

        /// <summary>Resolves the placement or clearing origin world position for a single footprint cell.</summary>
        public static Point ResolveOriginWorld(in PasteTile tile, bool forClear)
        {
            // --- Derive alternate-aware TileObjectData origin offset and map it to world coordinates.
            bool hasTile = forClear ? tile.Target.HasTile : tile.PayloadCell.HasTile();
            Tile framing = forClear ? tile.Target : tile.PayloadCell.ToTile();
            if (!hasTile || !TileLoaderSupport.IsKnownTileType(framing.TileType))
                return tile.World;

            FramePlacementVariant variant = ResolveFramePlacementVariant(in tile, forClear);
            TileObjectData? data = variant.Data ?? TileObjectData.GetTileData(framing);
            if (data is null)
                return tile.World;

            int w = Math.Max(1, data.Width);
            int h = Math.Max(1, data.Height);
            int subX = ResolveSubX(framing.TileFrameX, data, w);
            int subY = ResolveSubY(framing.TileFrameY, data, h);
            var origin = data.Origin;
            int ox = Math.Clamp(origin.X, 0, w - 1);
            int oy = Math.Clamp(origin.Y, 0, h - 1);
            return new Point(tile.World.X + (ox - subX), tile.World.Y + (oy - subY));
        }

        /// <summary>Groups batch tiles into logical frame objects keyed by origin world position.</summary>
        public static List<FrameCandidate> GroupIntoCandidates(IReadOnlyList<PasteTile> tiles, bool forClear)
        {
            // --- Bucket every footprint cell by its resolved origin, then build one candidate per bucket.
            var groups = new Dictionary<Point, List<PasteTile>>();
            for (int i = 0; i < tiles.Count; i++)
            {
                PasteTile tile = tiles[i];
                Point origin = ResolveOriginWorld(in tile, forClear);
                if (!groups.TryGetValue(origin, out List<PasteTile>? group))
                {
                    group = new List<PasteTile>();
                    groups[origin] = group;
                }

                group.Add(tile);
            }

            var candidates = new List<FrameCandidate>(groups.Count);
            foreach (KeyValuePair<Point, List<PasteTile>> entry in groups)
            {
                PasteTile originTile = ResolveOriginPasteTileFromGroup(entry.Value, entry.Key, forClear);
                candidates.Add(new FrameCandidate(entry.Key, originTile, entry.Value));
            }

            return candidates;
        }

        /// <summary>Picks the origin paste tile for a grouped candidate footprint.</summary>
        private static PasteTile ResolveOriginPasteTileFromGroup(
            IReadOnlyList<PasteTile> tiles,
            Point originWorld,
            bool forClear)
        {
            // --- Prefer the cell whose world position matches the origin; otherwise fall back to framing helpers.
            for (int i = 0; i < tiles.Count; i++)
            {
                if (tiles[i].World == originWorld)
                    return tiles[i];
            }

            return forClear ? GetClearingOriginTile(tiles) : GetPlacementOriginTile(tiles);
        }

        /// <summary>Returns the origin paste tile for a frame candidate.</summary>
        public static PasteTile ResolveOriginPasteTile(FrameCandidate candidate, bool forClear) =>
            ResolveOriginPasteTileFromGroup(candidate.Tiles, candidate.OriginWorld, forClear);

        /// <summary>Returns true when this candidate should use single-tile placement instead of PlaceObject.</summary>
        public static bool UsesTilePlacement(IReadOnlyList<PasteTile> tiles, in PasteTile origin)
        {
            // --- Route grass, base-tile conversions, tracks, and plain 1x1 objects through TileOperations.
            if (!origin.PayloadCell.HasTile())
                return false;

            ushort tileType = origin.PayloadCell.Block.Type;
            if (!TileLoaderSupport.IsKnownTileType(tileType))
                return false;

            FramePlacementVariant variant = ResolveFramePlacementVariant(origin.PayloadCell);
            int style = variant.Style;
            if (InteractionMapProvider.GetRequiredBaseTile(tileType, style).HasValue || TileID.Sets.Grass[tileType])
                return true;

            // Minecart tracks are multi-style but still place via PlaceTile, not PlaceObject.
            if (tileType == TileID.MinecartTrack)
                return true;

            if (InteractionMapProvider.HasMultipleTilePlacementStyles(tileType))
                return false;

            if (tiles.Count != 1)
                return false;

            var data = variant.Data ?? TileObjectData.GetTileData(tileType, style);
            return data is null || (data.Width <= 1 && data.Height <= 1);
        }

        /// <summary>Returns true when every footprint cell already matches the payload intent exactly.</summary>
        public static bool IsCandidateAlreadyPlaced(FrameCandidate candidate)
        {
            // --- Treat the candidate as done only when type and framing match on every cell.
            for (int i = 0; i < candidate.Tiles.Count; i++)
            {
                if (!TileComparison.MatchExact(candidate.Tiles[i]))
                    return false;
            }

            return true;
        }

        /// <summary>Returns true when Terraria placement rules allow scheduling this frame candidate now.</summary>
        public static bool CanPlaceFrameCandidate(
            FrameCandidate candidate,
            in PasteContext context,
            IReadOnlyList<FrameCandidate>? allPending = null)
        {
            // --- Validate replace rules, then use TileObject.CanPlace with recovered style and direction.
            PasteTile origin = candidate.OriginTile;
            if (!origin.PayloadCell.HasTile())
                return true;

            ushort tileType = origin.PayloadCell.Block.Type;
            if (!TileLoaderSupport.IsKnownTileType(tileType))
                return false;

            FramePlacementVariant variant = ResolveFramePlacementVariant(origin.PayloadCell);
            int x = origin.World.X;
            int y = origin.World.Y;
            int style = variant.Style;
            Tile dest = Main.tile[x, y];
            if (dest.HasTile && dest.TileType == tileType)
                return true;

            // --- Defer selection until inventory can supply the item when retry-on-missing is enabled.
            if (context.RequireItems
                && context.RetryOnMissingItems
                && !context.IsSweep
                && !HasRequiredItemsForFrameCandidate(candidate, in context))
                return false;

            if (UsesTilePlacement(candidate.Tiles, in origin))
                return TileLoaderSupport.IsCanPlaceAllowedAt(x, y, tileType, in context);

            if (!TileLoaderSupport.IsReplaceAllowedAt(x, y, tileType, in context))
                return false;

            if (context.IsSweep)
                return true;

            if (TileObject.CanPlace(x, y, tileType, style, variant.Direction, out _, onlyCheck: true))
                return true;

            if (allPending is null)
                return false;

            return HasReadyFramePlacementSupport(candidate, allPending, in context);
        }

        /// <summary>Returns true when tracked inventories can supply the candidate's placement item.</summary>
        public static bool HasRequiredItemsForFrameCandidate(FrameCandidate candidate, in PasteContext context)
        {
            // --- Empty payload and sweep never require a consumable item.
            if (!context.RequireItems || context.IsSweep)
                return true;

            PasteTile origin = candidate.OriginTile;
            if (!origin.PayloadCell.HasTile())
                return true;

            FramePlacementVariant variant = ResolveFramePlacementVariant(origin.PayloadCell);
            return TileInteractionChecks.HasTilePlacementItems(
                (ushort)origin.PayloadCell.Block.Type,
                variant.Style,
                origin.World.X,
                origin.World.Y,
                in context);
        }

        /// <summary>Returns the index of the best globally ready frame candidate in bottom-up order.</summary>
        public static int SelectNextGlobalPlaceCandidateIndex(
            IReadOnlyList<FrameCandidate> allPending,
            in PasteContext context) =>
            SelectNextPlaceCandidateIndex(allPending, allPending, in context);

        /// <summary>Returns true when any pending candidate is only blocked by another unplaced frame object.</summary>
        public static bool HasPendingPlaceSupportWait(
            IReadOnlyList<FrameCandidate> pending,
            in PasteContext context)
        {
            // --- Retry is useful only while a lower frame supporter has not yet been placed.
            for (int i = 0; i < pending.Count; i++)
            {
                if (IsBlockedFromPlace(pending[i], pending, in context))
                    return true;
            }

            return false;
        }

        /// <summary>Builds a debug summary of why each pending frame candidate is not ready to place.</summary>
        public static string FormatPendingPlaceBlockers(
            IReadOnlyList<FrameCandidate> pending,
            in PasteContext context)
        {
            // --- Describe waiting-on-support and CanPlace failures with recovered style/alternate.
            var details = new System.Text.StringBuilder();
            for (int i = 0; i < pending.Count; i++)
            {
                FrameCandidate candidate = pending[i];
                string? blocker = DescribePendingPlaceBlocker(candidate, pending, in context);
                if (blocker is null)
                    continue;

                if (details.Length > 0)
                    details.Append("; ");

                FramePlacementVariant variant = candidate.OriginTile.PayloadCell.HasTile()
                    ? ResolveFramePlacementVariant(candidate.OriginTile.PayloadCell)
                    : default;
                details.Append(
                    $"[{i}] origin=({candidate.OriginWorld.X},{candidate.OriginWorld.Y}) "
                    + $"type={candidate.OriginTile.PayloadCell.Block.Type} "
                    + $"style={variant.Style} alt={variant.Alternate}: {blocker}");
            }

            return details.Length > 0 ? details.ToString() : "no pending candidates";
        }

        /// <summary>Returns a short reason string when a pending candidate cannot place yet, or null when ready.</summary>
        private static string? DescribePendingPlaceBlocker(
            FrameCandidate candidate,
            IReadOnlyList<FrameCandidate> pending,
            in PasteContext context)
        {
            // --- Prefer support waits, then inventory, then CanPlace failure.
            for (int i = 0; i < pending.Count; i++)
            {
                FrameCandidate other = pending[i];
                if (other.OriginWorld == candidate.OriginWorld)
                    continue;

                if (IsSupportedByPlacement(candidate, other) && !IsCandidatePresentInWorld(other, in context))
                {
                    return $"waitingOnPending origin=({other.OriginWorld.X},{other.OriginWorld.Y}) "
                        + $"footprint={other.Tiles.Count}";
                }
            }

            if (context.RetryOnMissingItems
                && context.RequireItems
                && !context.IsSweep
                && !HasRequiredItemsForFrameCandidate(candidate, in context))
            {
                FramePlacementVariant variant = ResolveFramePlacementVariant(candidate.OriginTile.PayloadCell);
                return $"missingItems type={candidate.OriginTile.PayloadCell.Block.Type} style={variant.Style}";
            }

            if (!CanPlaceFrameCandidate(candidate, in context, pending))
                return "CanPlaceFrameCandidate=false";

            return null;
        }

        /// <summary>Returns true when a place batch still owns the given placement origin.</summary>
        public static bool BatchContainsPlaceOrigin(IReadOnlyList<PasteTile> tiles, Point originWorld)
        {
            List<FrameCandidate> candidates = GroupIntoCandidates(tiles, forClear: false);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].OriginWorld == originWorld)
                    return true;
            }

            return false;
        }

        /// <summary>Returns the index of the first currently placeable frame candidate in bottom-up order.</summary>
        public static int SelectNextPlaceCandidateIndex(
            IReadOnlyList<FrameCandidate> localCandidates,
            IReadOnlyList<FrameCandidate> allPending,
            in PasteContext context)
        {
            // --- Prefer lower objects first, skipping candidates blocked by unplaced pending support.
            int selected = -1;
            for (int i = 0; i < localCandidates.Count; i++)
            {
                FrameCandidate candidate = localCandidates[i];
                if (IsBlockedFromPlace(candidate, allPending, in context))
                    continue;

                if (!CanPlaceFrameCandidate(candidate, in context, allPending))
                    continue;

                if (selected < 0 || ComparePlaceOrder(candidate, localCandidates[selected]) < 0)
                    selected = i;
            }

            return selected;
        }

        /// <summary>Returns the index of the next frame candidate that can be cleared without orphaning pending support.</summary>
        public static int SelectNextClearCandidateIndex(IReadOnlyList<FrameCandidate> candidates, in PasteContext context)
        {
            // --- Clear top-most unblocked objects first so stacked frames lose support in a safe order.
            int selected = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (IsBlockedFromClear(candidates[i], candidates))
                    continue;

                if (selected < 0 || CompareClearOrder(candidates[i], candidates[selected]) < 0)
                    selected = i;
            }

            return selected;
        }

        /// <summary>Returns true when clearing <paramref name="supporter"/> would leave <paramref name="supported"/> unsupported.</summary>
        public static bool IsSupportedBy(FrameCandidate supported, FrameCandidate supporter) =>
            IsSupportedByClear(supported, supporter);

        /// <summary>Returns true when a placed object depends on another object's footprint for clear-time support.</summary>
        public static bool IsSupportedByClear(FrameCandidate supported, FrameCandidate supporter)
        {
            // --- A stacked object depends on another pending footprint cell satisfying its anchor checks.
            if (supported.OriginWorld == supporter.OriginWorld)
                return false;

            Tile framing = supported.OriginTile.Target;
            PasteTile originTile = supported.OriginTile;
            FramePlacementVariant variant = ResolveFramePlacementVariant(in originTile, forClear: true);
            return IsSupportedByAnchorFootprint(framing, variant.Data, supported.OriginWorld, supporter);
        }

        /// <summary>Returns true when a payload object depends on another pending object for placement support.</summary>
        public static bool IsSupportedByPlacement(FrameCandidate supported, FrameCandidate supporter)
        {
            // --- Mirror clear support checks using payload intent so stacked place order is bottom-up.
            if (supported.OriginWorld == supporter.OriginWorld)
                return false;

            if (!supported.OriginTile.PayloadCell.HasTile())
                return false;

            FramePlacementVariant variant = ResolveFramePlacementVariant(supported.OriginTile.PayloadCell);
            Tile framing = supported.OriginTile.PayloadCell.ToTile();
            return IsSupportedByAnchorFootprint(framing, variant.Data, supported.OriginWorld, supporter);
        }

        /// <summary>Returns true when supporter footprint cells satisfy supported object's anchor requirements.</summary>
        private static bool IsSupportedByAnchorFootprint(
            Tile framingTile,
            TileObjectData? objectData,
            Point supportedOriginWorld,
            FrameCandidate supporter)
        {
            // --- Walk supporter footprint cells and test each against the dependent object's anchor geometry.
            if (!framingTile.HasTile || !TileLoaderSupport.IsKnownTileType(framingTile.TileType))
                return false;

            var data = objectData ?? TileObjectData.GetTileData(framingTile);
            if (data is null)
                return false;

            for (int i = 0; i < supporter.Tiles.Count; i++)
            {
                if (IsAnchorSupportWorld(data, supportedOriginWorld, supporter.Tiles[i].World))
                    return true;
            }

            return IsVerticallyStackedSupport(data, supportedOriginWorld, supporter);
        }

        /// <summary>Returns true when another pending candidate must be placed before this one.</summary>
        public static bool IsBlockedFromPlace(
            FrameCandidate candidate,
            IReadOnlyList<FrameCandidate> pending,
            in PasteContext context)
        {
            // --- Defer placement while a lower pending object this candidate anchors to is not yet in the world.
            for (int i = 0; i < pending.Count; i++)
            {
                FrameCandidate other = pending[i];
                if (other.OriginWorld == candidate.OriginWorld)
                    continue;

                if (IsSupportedByPlacement(candidate, other) && !IsCandidatePresentInWorld(other, in context))
                    return true;
            }

            return false;
        }

        /// <summary>Returns true when the candidate's payload object already exists in the live world.</summary>
        public static bool IsCandidatePresentInWorld(FrameCandidate candidate, in PasteContext context)
        {
            // --- Exact match is ideal; type presence at origin is enough for stacked-support ordering.
            if (IsCandidateAlreadyPlaced(candidate))
                return true;

            PasteTile origin = candidate.OriginTile;
            if (!origin.PayloadCell.HasTile())
                return true;

            ushort tileType = origin.PayloadCell.Block.Type;
            Tile live = Main.tile[origin.World.X, origin.World.Y];
            if (live.HasTile && live.TileType == tileType)
                return true;

            return context.EffectSession?.HasPendingOrInFlightPlacementAt(origin.World, tileType) == true;
        }

        /// <summary>Returns true when every frame-on-frame supporter is live or already scheduled.</summary>
        private static bool HasReadyFramePlacementSupport(
            FrameCandidate candidate,
            IReadOnlyList<FrameCandidate> pending,
            in PasteContext context)
        {
            bool dependsOnFrameSupport = false;
            for (int i = 0; i < pending.Count; i++)
            {
                FrameCandidate other = pending[i];
                if (other.OriginWorld == candidate.OriginWorld)
                    continue;

                if (!IsSupportedByPlacement(candidate, other))
                    continue;

                dependsOnFrameSupport = true;
                if (!IsCandidatePresentInWorld(other, in context))
                    return false;
            }

            return dependsOnFrameSupport;
        }

        /// <summary>Returns true when another pending candidate still depends on this candidate for support.</summary>
        public static bool IsBlockedFromClear(FrameCandidate candidate, IReadOnlyList<FrameCandidate> pending)
        {
            // --- Defer clearing while any remaining object would lose required anchor support.
            for (int i = 0; i < pending.Count; i++)
            {
                FrameCandidate other = pending[i];
                if (other.OriginWorld == candidate.OriginWorld)
                    continue;

                if (IsSupportedByClear(other, candidate))
                    return true;
            }

            return false;
        }

        /// <summary>Returns true when a world cell satisfies this object's anchor requirements.</summary>
        private static bool IsAnchorSupportWorld(TileObjectData data, Point objectOriginWorld, Point supportWorld)
        {
            // --- Map standard bottom/top anchor check rows from the placement origin into world space.
            if (data.AnchorBottom != default)
            {
                AnchorData anchor = data.AnchorBottom;
                int ox = data.Origin.X;
                int oy = data.Origin.Y;
                int checkY = objectOriginWorld.Y - oy + data.Height;
                if (supportWorld.Y != checkY)
                    return false;

                int checkStartX = objectOriginWorld.X - ox + anchor.checkStart;
                if (supportWorld.X < checkStartX || supportWorld.X >= checkStartX + anchor.tileCount)
                    return false;

                return AnchorAcceptsLiveTile(anchor, supportWorld, data);
            }

            if (data.AnchorTop != default)
            {
                AnchorData anchor = data.AnchorTop;
                int ox = data.Origin.X;
                int oy = data.Origin.Y;
                int checkY = objectOriginWorld.Y - oy - 1;
                if (supportWorld.Y != checkY)
                    return false;

                int checkStartX = objectOriginWorld.X - ox + anchor.checkStart;
                if (supportWorld.X < checkStartX || supportWorld.X >= checkStartX + anchor.tileCount)
                    return false;

                return AnchorAcceptsLiveTile(anchor, supportWorld, data);
            }

            if (data.AnchorLeft != default)
            {
                AnchorData anchor = data.AnchorLeft;
                int ox = data.Origin.X;
                int oy = data.Origin.Y;
                int checkX = objectOriginWorld.X - ox - 1;
                if (supportWorld.X != checkX)
                    return false;

                int checkStartY = objectOriginWorld.Y - oy + anchor.checkStart;
                if (supportWorld.Y < checkStartY || supportWorld.Y >= checkStartY + anchor.tileCount)
                    return false;

                return AnchorAcceptsLiveTile(anchor, supportWorld, data);
            }

            if (data.AnchorRight != default)
            {
                AnchorData anchor = data.AnchorRight;
                int ox = data.Origin.X;
                int oy = data.Origin.Y;
                int checkX = objectOriginWorld.X - ox + data.Width;
                if (supportWorld.X != checkX)
                    return false;

                int checkStartY = objectOriginWorld.Y - oy + anchor.checkStart;
                if (supportWorld.Y < checkStartY || supportWorld.Y >= checkStartY + anchor.tileCount)
                    return false;

                return AnchorAcceptsLiveTile(anchor, supportWorld, data);
            }

            if (data.AnchorWall)
            {
                // Wall-anchored objects defer when another pending frame occupies their footprint.
                int ox = data.Origin.X;
                int oy = data.Origin.Y;
                int topLeftX = objectOriginWorld.X - ox;
                int topLeftY = objectOriginWorld.Y - oy;
                return supportWorld.X >= topLeftX && supportWorld.X < topLeftX + data.Width
                    && supportWorld.Y >= topLeftY && supportWorld.Y < topLeftY + data.Height;
            }

            return false;
        }

        /// <summary>Returns true when the live tile at the anchor check position satisfies the anchor type.</summary>
        private static bool AnchorAcceptsLiveTile(AnchorData anchor, Point supportWorld, TileObjectData data)
        {
            // --- Restricted anchors (e.g. crates stacking on crates) must win over coarse solidity checks.
            Tile support = Main.tile[supportWorld.X, supportWorld.Y];
            if (data.AnchorValidTiles is { Length: > 0 })
                return support.HasTile && data.isValidTileAnchor(support.TileType);

            const AnchorType StandardSupportMask =
                AnchorType.SolidTile |
                AnchorType.SolidWithTop |
                AnchorType.SolidSide |
                AnchorType.SolidBottom |
                AnchorType.EmptyTile;

            if ((anchor.type & StandardSupportMask) != AnchorType.None)
                return support.HasTile;

            return anchor.type != AnchorType.None && support.HasTile;
        }

        /// <summary>Returns true when supporter sits directly below supported object's footprint with valid anchor overlap.</summary>
        private static bool IsVerticallyStackedSupport(
            TileObjectData data,
            Point supportedOriginWorld,
            FrameCandidate supporter)
        {
            if (data.AnchorBottom == default && data.AnchorValidTiles is not { Length: > 0 })
                return false;

            int ox = data.Origin.X;
            int oy = data.Origin.Y;
            int supportedBottom = supportedOriginWorld.Y - oy + data.Height - 1;
            int requiredSupportRow = supportedBottom + 1;

            int supportedLeft = supportedOriginWorld.X - ox;
            int supportedRight = supportedLeft + data.Width - 1;

            int supporterMinY = int.MaxValue;
            int supporterMinX = int.MaxValue;
            int supporterMaxX = int.MinValue;
            for (int i = 0; i < supporter.Tiles.Count; i++)
            {
                Point world = supporter.Tiles[i].World;
                if (world.Y < supporterMinY)
                    supporterMinY = world.Y;

                if (world.X < supporterMinX)
                    supporterMinX = world.X;

                if (world.X > supporterMaxX)
                    supporterMaxX = world.X;
            }

            if (supporterMinY != requiredSupportRow)
                return false;

            if (supporterMaxX < supportedLeft || supporterMinX > supportedRight)
                return false;

            return SupporterPayloadSatisfiesAnchor(data, supporter);
        }

        /// <summary>Returns true when supporter payload type satisfies supported object's anchor restrictions.</summary>
        private static bool SupporterPayloadSatisfiesAnchor(TileObjectData data, FrameCandidate supporter)
        {
            PasteTile origin = supporter.OriginTile;
            if (!origin.PayloadCell.HasTile())
                return false;

            ushort supporterType = origin.PayloadCell.Block.Type;
            if (data.AnchorValidTiles is { Length: > 0 })
                return data.isValidTileAnchor(supporterType);

            return true;
        }

        private static bool HasOtherAnchorData(TileObjectData data) =>
            HasSpecialDirectionalAnchor(data)
            || (data.AnchorValidTiles is { Length: > 0 })
            || (data.AnchorInvalidTiles is { Length: > 0 });

        private static int ComparePlaceOrder(FrameCandidate a, FrameCandidate b)
        {
            int yCmp = b.OriginWorld.Y.CompareTo(a.OriginWorld.Y);
            if (yCmp != 0)
                return yCmp;

            return a.OriginWorld.X.CompareTo(b.OriginWorld.X);
        }

        private static int CompareClearOrder(FrameCandidate a, FrameCandidate b)
        {
            int yCmp = a.OriginWorld.Y.CompareTo(b.OriginWorld.Y);
            if (yCmp != 0)
                return yCmp;

            return a.OriginWorld.X.CompareTo(b.OriginWorld.X);
        }

        /// <summary>Builds a log label for the candidate origin tile.</summary>
        public static string FormatCandidateMessage(FrameCandidate candidate, bool forClear)
        {
            PasteTile origin = candidate.OriginTile;
            return forClear
                ? LayerLogFormat.FormatTile(origin.Target)
                : origin.PayloadCell.HasTile()
                    ? LayerLogFormat.FormatTile(origin.PayloadTile)
                    : LayerLogFormat.FormatFromSubject(in origin, PasteSubject.Payload);
        }

        /// <summary>Removes a completed candidate from the batch and requeues any remaining footprint cells.</summary>
        public static LayerResult RemoveCandidateFromBatch(
            TileBatch batch,
            FrameCandidate candidate,
            string? message,
            string? okLabel)
        {
            // --- Drop every world cell owned by the handled candidate and keep the rest of the batch.
            var removed = new HashSet<Point>();
            for (int i = 0; i < candidate.Tiles.Count; i++)
                removed.Add(candidate.Tiles[i].World);

            var remaining = new List<PasteTile>(batch.Tiles.Count);
            for (int i = 0; i < batch.Tiles.Count; i++)
            {
                if (!removed.Contains(batch.Tiles[i].World))
                    remaining.Add(batch.Tiles[i]);
            }

            return remaining.Count > 0
                ? LayerResult.OkWithRemaining(batch.WithRemainingTiles(remaining), message, okLabel)
                : LayerResult.Ok(message, okLabel);
        }

        /// <summary>Merges a partial place pass back into the operation batch while preserving other candidates.</summary>
        public static LayerResult MergePlaceRemaining(TileBatch batch, FrameCandidate candidate, LayerResult placeResult)
        {
            // --- Replace only the handled candidate cells with the tiles that still need a follow-up pass.
            var removed = new HashSet<Point>();
            for (int i = 0; i < candidate.Tiles.Count; i++)
                removed.Add(candidate.Tiles[i].World);

            var remaining = new List<PasteTile>(batch.Tiles.Count);
            for (int i = 0; i < batch.Tiles.Count; i++)
            {
                if (!removed.Contains(batch.Tiles[i].World))
                    remaining.Add(batch.Tiles[i]);
            }

            IReadOnlyList<PasteTile> partial = placeResult.RemainingTiles;
            for (int i = 0; i < partial.Count; i++)
                remaining.Add(partial[i]);

            return LayerResult.OkWithRemaining(
                batch.WithRemainingTiles(remaining),
                placeResult.Message,
                placeResult.OkLabel);
        }

        public static bool PasteFrame(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                int wx = t.World.X, wy = t.World.Y;
                if (t.PayloadCell.HasTile() && Main.tile[wx, wy].HasTile)
                {
                    var b = t.PayloadCell.Block;
                    if (!TileExecutor.EnqueueApplyTileFrame(wx, wy, b.FrameX, b.FrameY, in context))
                        return false;
                }
            }

            return true;
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

                if (!TileExecutor.EnqueueClearTile(wx, wy, in context))
                    return LayerResult.Fail(LayerFailReason.ClearBlocked, message);

                changed = true;
            }

            return changed ? LayerResult.Ok(message, LayerLogFormat.ClearedLabel) : LayerResult.Ok();
        }

        /// <summary>Clears one frame object by killing only its placement origin tile.</summary>
        public static LayerResult ClearOrigin(in PasteTile originTile, in PasteContext context, string? message = null)
        {
            // --- Let WorldGen framing remove the footprint after a single origin kill mutation is scheduled.
            message ??= LayerLogFormat.FormatTile(originTile.Target);
            int x = originTile.World.X;
            int y = originTile.World.Y;
            if (!Main.tile[x, y].HasTile)
                return LayerResult.Ok(message, LayerLogFormat.ClearedLabel);

            return TileExecutor.EnqueueClearTile(x, y, in context)
                ? LayerResult.Ok(message, LayerLogFormat.ClearedLabel)
                : LayerResult.Fail(LayerFailReason.ClearBlocked, message);
        }

        public static LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();

            var origin = GetPlacementOriginTile(tiles);
            int x = origin.World.X, y = origin.World.Y;
            if (!origin.PayloadCell.HasTile()) return LayerResult.Ok();

            string message = LayerLogFormat.FormatTile(origin.PayloadTile);
            string okLabel = LayerLogFormat.PlacedLabel(in context);

            ushort payloadType = (ushort)origin.PayloadCell.Block.Type;
            if (!TileLoaderSupport.IsKnownTileType(payloadType))
            {
                PastingDebug.LogPlacement(payloadType, 0, null, x, y, false, "PlaceObject");
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            FramePlacementVariant variant = ResolveFramePlacementVariant(origin.PayloadCell);
            int style = variant.Style;
            int alternate = variant.Alternate;
            int direction = variant.Direction;
            short frameX = origin.PayloadCell.Block.FrameX;
            short frameY = origin.PayloadCell.Block.FrameY;
            InteractionEntry? interaction = InteractionMapProvider.GetFirstTileInteraction(payloadType, style);
            ushort placeType = interaction?.GetPlaceMainId() is int placeMain && placeMain > 0
                ? (ushort)placeMain
                : payloadType;
            bool usesFake = interaction?.FakeId is int fakeId && fakeId > 0 && fakeId != payloadType;
            Tile dest = Main.tile[x, y];

            if (dest.HasTile && (dest.TileType == placeType || dest.TileType == payloadType))
            {
                if (usesFake || dest.TileType == placeType && placeType != payloadType)
                    return LayerResult.Ok(message, okLabel);

                if (!PasteFrame(tiles, in context))
                    return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
                return LayerResult.Ok(message, okLabel);
            }

            if (!TileLoaderSupport.IsKnownTileType(placeType))
            {
                PastingDebug.LogPlacement(placeType, style, null, x, y, false, "PlaceObject");
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            if (!TileLoaderSupport.IsReplaceAllowedAt(x, y, placeType, in context))
            {
                PastingDebug.LogPlacement(placeType, style, null, x, y, false, "PlaceObject", alternate: alternate, frameX: frameX, frameY: frameY);
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            if (context.UseReplacePlace && dest.HasTile)
                TileExecutor.EnqueueVoidKillTile(x, y, in context);

            if (!context.IsSweep
                && !TileObject.CanPlace(x, y, placeType, style, direction, out _, onlyCheck: true))
            {
                PastingDebug.LogPlacement(placeType, style, null, x, y, false, "PlaceObject", alternate: alternate, frameX: frameX, frameY: frameY);
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            // Item lookup stays on payload type so natural→fake rows resolve Rubblemaker + ammo.
            if (!TileExecutor.TryCheckTileItem(payloadType, style, in context, out LayerFailReason itemFailReason))
            {
                TileItemAvailability issue = TileExecutor.GetTileItemAvailability(payloadType, style, in context);
                PastingDebug.LogPlacement(placeType, style, null, x, y, false, "PlaceObject", placeType, issue, alternate, frameX, frameY);
                return LayerResult.Fail(itemFailReason, message);
            }

            if (!TileExecutor.EnqueuePlaceObject(
                    x,
                    y,
                    placeType,
                    style,
                    alternate,
                    in context,
                    consumeItem: true,
                    postPlaceFrameX: usesFake ? TileMutation.NoPostPlaceFrame : frameX,
                    postPlaceFrameY: usesFake ? TileMutation.NoPostPlaceFrame : frameY,
                    direction: direction))
            {
                PastingDebug.LogPlacement(placeType, style, null, x, y, false, "PlaceObject", alternate: alternate, frameX: frameX, frameY: frameY);
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            PastingDebug.LogPlacement(placeType, style, null, x, y, true, "PlaceObject", alternate: alternate, frameX: frameX, frameY: frameY);
            return LayerResult.Ok(message, okLabel);
        }
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

        public static LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context, bool styleAgnostic = false)
        {
            if (tiles.Count == 0) return LayerResult.Ok();

            var origin = FrameOperations.GetPlacementOriginTile(tiles);
            int x = origin.World.X, y = origin.World.Y;
            if (!origin.PayloadCell.HasTile()) return LayerResult.Ok();

            ushort payloadType = (ushort)origin.PayloadCell.Block.Type;
            if (!TileLoaderSupport.IsKnownTileType(payloadType))
            {
                PastingDebug.LogPlacement(payloadType, 0, null, x, y, false, "PlaceTile");
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, LayerLogFormat.FormatTile(origin.PayloadTile));
            }

            int style = origin.PayloadCell.ResolvePlacementStyle();
            InteractionEntry? interaction = InteractionMapProvider.GetFirstTileInteraction(payloadType, style);
            ushort placeType = interaction?.GetPlaceMainId() is int placeMain && placeMain > 0
                ? (ushort)placeMain
                : payloadType;
            bool usesFake = interaction?.FakeId is int fakeId && fakeId > 0 && fakeId != payloadType;
            var baseTileId = InteractionMapProvider.GetRequiredBaseTile(payloadType, style);
            PastingDebug.LogInteractionLookup(payloadType, style, baseTileId, null);

            Tile dest = Main.tile[x, y];
            if (context.EffectSession?.HasPendingOrInFlightPlacementAt(new Point(x, y), placeType) == true
                || context.EffectSession?.HasPendingOrInFlightPlacementAt(new Point(x, y), payloadType) == true)
            {
                return LayerResult.Ok(
                    LayerLogFormat.FormatTile(origin.PayloadTile),
                    LayerLogFormat.PlacedLabel(in context));
            }

            if (dest.HasTile && (dest.TileType == placeType || dest.TileType == payloadType))
            {
                if (usesFake || (dest.TileType == placeType && placeType != payloadType))
                {
                    return LayerResult.Ok(
                        LayerLogFormat.FormatTile(origin.PayloadTile),
                        LayerLogFormat.PlacedLabel(in context));
                }

                if (!FrameOperations.PasteFrame(tiles, in context))
                    return LayerResult.Fail(LayerFailReason.InvalidPlacement, LayerLogFormat.FormatTile(origin.PayloadTile));
                return LayerResult.Ok(
                    LayerLogFormat.FormatTile(origin.PayloadTile),
                    LayerLogFormat.PlacedLabel(in context));
            }

            return PlaceWithTile(origin, tiles, style, baseTileId, placeType, usesFake, in context);
        }

        private static LayerResult PlaceWithTile(
            in PasteTile origin,
            IReadOnlyList<PasteTile> tiles,
            int style,
            int? baseTileId,
            ushort placeType,
            bool usesFake,
            in PasteContext context)
        {
            int x = origin.World.X, y = origin.World.Y;
            ushort payloadType = (ushort)origin.PayloadCell.Block.Type;
            string message = LayerLogFormat.FormatTile(origin.PayloadTile);
            string okLabel = LayerLogFormat.PlacedLabel(in context);
            Tile dest = Main.tile[x, y];

            if (baseTileId.HasValue)
            {
                ushort baseType = (ushort)baseTileId.Value;
                if (!dest.HasTile || dest.TileType != baseType)
                {
                    if (context.EffectSession?.HasPendingOrInFlightPlacementAt(new Point(x, y), baseType) == true)
                    {
                        PastingDebug.LogPlacement(placeType, style, baseTileId, x, y, true, "PlaceBaseTile");
                        return LayerResult.OkWithRemaining(tiles, message, okLabel);
                    }

                    if (!TileLoaderSupport.IsCanPlaceAllowedAt(x, y, baseType, in context))
                    {
                        PastingDebug.LogPlacement(placeType, style, baseTileId, x, y, false, "PlaceTile");
                        return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
                    }

                    if (!TileExecutor.TryCheckTileItem(baseType, 0, in context, out LayerFailReason baseItemFailReason))
                    {
                        TileItemAvailability issue = TileExecutor.GetTileItemAvailability(baseType, 0, in context);
                        PastingDebug.LogPlacement(placeType, style, baseTileId, x, y, false, "PlaceBaseTile", baseType, issue);
                        return LayerResult.Fail(baseItemFailReason, message);
                    }

                    if (!TileExecutor.EnqueuePlaceTile(x, y, baseType, 0, in context, forced: context.IsSweep, consumeItem: true))
                    {
                        PastingDebug.LogPlacement(placeType, style, baseTileId, x, y, false, "PlaceTile");
                        return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
                    }

                    PastingDebug.LogPlacement(placeType, style, baseTileId, x, y, true, "PlaceBaseTile");
                    // Base-first conversion tiles (e.g., dirt -> grass) require a second pass:
                    // this pass lays support, next pass applies the conversion tile itself.
                    return LayerResult.OkWithRemaining(tiles, message, okLabel);
                }
            }
            else if (dest.HasTile && !context.IsSweep && context.RequireItems && !context.UseReplacePlace)
            {
                PastingDebug.LogPlacement(placeType, style, baseTileId, x, y, false, "PlaceTile");
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            bool conversionBaseReady = baseTileId.HasValue
                && dest.HasTile
                && dest.TileType == (ushort)baseTileId.Value;

            if (!conversionBaseReady)
            {
                if (!TileLoaderSupport.IsReplaceAllowedAt(x, y, placeType, in context))
                {
                    PastingDebug.LogPlacement(placeType, style, baseTileId, x, y, false, "PlaceTile");
                    return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
                }

                if (context.UseReplacePlace && dest.HasTile && dest.TileType != placeType)
                    TileExecutor.EnqueueVoidKillTile(x, y, in context);

                if (!context.IsSweep && !TileLoader.CanPlace(x, y, placeType))
                {
                    PastingDebug.LogPlacement(placeType, style, baseTileId, x, y, false, "PlaceTile");
                    return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
                }
            }

            if (!TileExecutor.TryCheckTileItem(payloadType, style, in context, out LayerFailReason itemFailReason, baseTileId))
            {
                TileItemAvailability issue = TileExecutor.GetTileItemAvailability(payloadType, style, in context, baseTileId);
                PastingDebug.LogPlacement(placeType, style, baseTileId, x, y, false, "PlaceTile", placeType, issue);
                return LayerResult.Fail(itemFailReason, message);
            }

            bool forcePlace = context.IsSweep || baseTileId.HasValue || context.UseReplacePlace;
            short frameX = usesFake ? TileMutation.NoPostPlaceFrame : origin.PayloadCell.Block.FrameX;
            short frameY = usesFake ? TileMutation.NoPostPlaceFrame : origin.PayloadCell.Block.FrameY;
            if (!TileExecutor.EnqueuePlaceTile(
                    x,
                    y,
                    placeType,
                    style,
                    in context,
                    forcePlace,
                    consumeItem: true,
                    postPlaceFrameX: frameX,
                    postPlaceFrameY: frameY))
            {
                PastingDebug.LogPlacement(placeType, style, baseTileId, x, y, false, "PlaceTile");
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            PastingDebug.LogPlacement(placeType, style, baseTileId, x, y, true, "PlaceTile");
            return LayerResult.Ok(message, okLabel);
        }
    }

    /// <summary>Shared interaction-aware tile item checks for placement layers.</summary>
    internal static class TileInteractionChecks
    {
        public static bool HasTilePlacementItems(ushort tileType, int style, int x, int y, in PasteContext context)
        {
            // --- Matched tiles need nothing; support underlays demand base+tool; else try live convert or direct place.

            Tile target = Main.tile[x, y];
            if (target.HasTile && target.TileType == tileType)
                return true;

            int? requiredBase = InteractionMapProvider.GetRequiredBaseTile(tileType, style);
            if (requiredBase.HasValue)
            {
                ushort baseType = (ushort)requiredBase.Value;
                if (!target.HasTile || target.TileType != baseType)
                {
                    if (!TileExecutor.HasItemForTile(baseType, 0, in context))
                        return false;
                    return TileExecutor.HasItemForTile(tileType, style, in context, baseType);
                }

                return TileExecutor.HasItemForTile(tileType, style, in context, baseType);
            }

            // Biome convert when the destination already is a known conversion base.
            if (target.HasTile
                && InteractionMapProvider.HasConversionFromBase(tileType, style, target.TileType)
                && TileExecutor.HasItemForTile(tileType, style, in context, target.TileType))
            {
                return true;
            }

            return TileExecutor.HasItemForTile(tileType, style, in context);
        }

        public static IReadOnlyList<int>? GetToolItemIds(ushort tileType, int style, int x, int y)
        {
            // --- Prefer support-base or live conversion filter, then direct no-base tool ids.

            Tile target = Main.tile[x, y];
            int? baseId = null;
            int? requiredBase = InteractionMapProvider.GetRequiredBaseTile(tileType, style);
            if (requiredBase.HasValue)
            {
                ushort baseType = (ushort)requiredBase.Value;
                if (target.HasTile && target.TileType == baseType)
                    baseId = requiredBase;
            }
            else if (target.HasTile
                && InteractionMapProvider.HasConversionFromBase(tileType, style, target.TileType))
            {
                baseId = target.TileType;
            }

            foreach (InteractionEntry entry in InteractionMapProvider.EnumerateTileInteractions(tileType, style, baseId))
            {
                if (entry.ItemId is int toolId && toolId > 0)
                    return [toolId];
            }

            return InteractionMapProvider.GetTileItemIds(tileType, style);
        }
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

            int style = payloadCell.ResolvePlacementStyle();
            return InteractionMapProvider.GetTileItemIds((ushort)payloadCell.Block.Type, style);
        }

    }
}
