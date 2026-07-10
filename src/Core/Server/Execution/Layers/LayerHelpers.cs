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

        /// <summary>Resolves the placement or clearing origin world position for a single footprint cell.</summary>
        public static Point ResolveOriginWorld(in PasteTile tile, bool forClear)
        {
            // --- Derive TileObjectData origin offset from framing tile and map it to world coordinates.
            bool hasTile = forClear ? tile.Target.HasTile : tile.PayloadCell.HasTile();
            Tile framing = forClear ? tile.Target : tile.PayloadCell.ToTile();
            if (!hasTile || !TileLoaderSupport.IsKnownTileType(framing.TileType))
                return tile.World;

            var data = TileObjectData.GetTileData(framing);
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
            // --- Route grass, base-tile conversions, and 1x1 objects through TileOperations.
            if (!origin.PayloadCell.HasTile())
                return false;

            ushort tileType = origin.PayloadCell.Block.Type;
            if (!TileLoaderSupport.IsKnownTileType(tileType))
                return false;

            int style = origin.PayloadCell.Detail.Style;
            if (InteractionMapProvider.GetRequiredBaseTile(tileType, style).HasValue || TileID.Sets.Grass[tileType])
                return true;

            if (tiles.Count != 1)
                return false;

            var data = TileObjectData.GetTileData(tileType, style);
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
        public static bool CanPlaceFrameCandidate(FrameCandidate candidate, in PasteContext context)
        {
            // --- Validate replace rules, then use TileObject.CanPlace for full anchor-aware placement checks.
            PasteTile origin = candidate.OriginTile;
            if (!origin.PayloadCell.HasTile())
                return true;

            ushort tileType = origin.PayloadCell.Block.Type;
            if (!TileLoaderSupport.IsKnownTileType(tileType))
                return false;

            int x = origin.World.X;
            int y = origin.World.Y;
            int style = origin.PayloadCell.Detail.Style;
            Tile dest = Main.tile[x, y];
            if (dest.HasTile && dest.TileType == tileType)
                return true;

            if (UsesTilePlacement(candidate.Tiles, in origin))
                return TileLoaderSupport.IsCanPlaceAllowedAt(x, y, tileType, in context);

            if (!TileLoaderSupport.IsReplaceAllowedAt(x, y, tileType, in context))
                return false;

            if (context.IsSweepCheat)
                return true;

            return TileObject.CanPlace(x, y, tileType, style, 0, out _, onlyCheck: true);
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
                if (!CanPlaceFrameCandidate(candidate, in context))
                    continue;

                if (IsBlockedFromPlace(candidate, allPending))
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
            return IsSupportedByAnchorFootprint(framing, supported.OriginWorld, supporter);
        }

        /// <summary>Returns true when a payload object depends on another pending object for placement support.</summary>
        public static bool IsSupportedByPlacement(FrameCandidate supported, FrameCandidate supporter)
        {
            // --- Mirror clear support checks using payload intent so stacked place order is bottom-up.
            if (supported.OriginWorld == supporter.OriginWorld)
                return false;

            if (!supported.OriginTile.PayloadCell.HasTile())
                return false;

            Tile framing = supported.OriginTile.PayloadCell.ToTile();
            return IsSupportedByAnchorFootprint(framing, supported.OriginWorld, supporter);
        }

        /// <summary>Returns true when supporter footprint cells satisfy supported object's anchor requirements.</summary>
        private static bool IsSupportedByAnchorFootprint(
            Tile framingTile,
            Point supportedOriginWorld,
            FrameCandidate supporter)
        {
            // --- Walk supporter footprint cells and test each against the dependent object's anchor geometry.
            if (!framingTile.HasTile || !TileLoaderSupport.IsKnownTileType(framingTile.TileType))
                return false;

            var data = TileObjectData.GetTileData(framingTile);
            if (data is null)
                return false;

            for (int i = 0; i < supporter.Tiles.Count; i++)
            {
                if (IsAnchorSupportWorld(data, supportedOriginWorld, supporter.Tiles[i].World))
                    return true;
            }

            return false;
        }

        /// <summary>Returns true when another pending candidate must be placed before this one.</summary>
        public static bool IsBlockedFromPlace(FrameCandidate candidate, IReadOnlyList<FrameCandidate> pending)
        {
            // --- Defer placement while a lower pending object this candidate anchors to is not yet in the world.
            for (int i = 0; i < pending.Count; i++)
            {
                FrameCandidate other = pending[i];
                if (other.OriginWorld == candidate.OriginWorld)
                    continue;

                if (IsSupportedByPlacement(candidate, other) && !IsCandidatePresentInWorld(other))
                    return true;
            }

            return false;
        }

        /// <summary>Returns true when the candidate's payload object already exists in the live world.</summary>
        public static bool IsCandidatePresentInWorld(FrameCandidate candidate)
        {
            // --- Exact match is ideal; type presence at origin is enough for stacked-support ordering.
            if (IsCandidateAlreadyPlaced(candidate))
                return true;

            PasteTile origin = candidate.OriginTile;
            if (!origin.PayloadCell.HasTile())
                return true;

            Tile live = Main.tile[origin.World.X, origin.World.Y];
            return live.HasTile && live.TileType == origin.PayloadCell.Block.Type;
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

            return false;
        }

        /// <summary>Returns true when the live tile at the anchor check position satisfies the anchor type.</summary>
        private static bool AnchorAcceptsLiveTile(AnchorData anchor, Point supportWorld, TileObjectData data)
        {
            // --- Use solidity heuristics for standard anchors and tile-type tables for restricted anchors.
            const AnchorType StandardSupportMask =
                AnchorType.SolidTile |
                AnchorType.SolidWithTop |
                AnchorType.SolidSide |
                AnchorType.SolidBottom |
                AnchorType.EmptyTile;

            Tile support = Main.tile[supportWorld.X, supportWorld.Y];
            if ((anchor.type & StandardSupportMask) != AnchorType.None)
                return support.HasTile;

            if (data.AnchorValidTiles is { Length: > 0 })
                return support.HasTile && data.isValidTileAnchor(support.TileType);

            return anchor.type != AnchorType.None && support.HasTile;
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
                if (!PasteFrame(tiles, in context))
                    return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
                return LayerResult.Ok(message, okLabel);
            }

            if (!TileLoaderSupport.IsReplaceAllowedAt(x, y, tileType, in context))
            {
                PastingDebug.LogPlacement(tileType, style, null, x, y, false, "PlaceObject");
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            if (context.UseReplacePlace && dest.HasTile)
                TileExecutor.EnqueueVoidKillTile(x, y, in context);

            if (!context.IsSweepCheat
                && !TileObject.CanPlace(x, y, tileType, style, 0, out _, onlyCheck: true))
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
                if (!FrameOperations.PasteFrame(tiles, in context))
                    return LayerResult.Fail(LayerFailReason.InvalidPlacement, LayerLogFormat.FormatTile(origin.PayloadTile));
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

    }
}
