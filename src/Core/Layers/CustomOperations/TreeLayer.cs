using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.UI.Preview;
using Terraria;
using Terraria.Enums;
using Terraria.GameContent.Drawing;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    [CustomPasteLayer]
    /// <summary>Identifies and places tree instances (one TileBatch per trunk-ground anchor).</summary>
    internal sealed class TreeLayer : Layer
    {
        public static readonly TreeLayer Definition = new();

        /// <summary>Trunk-ground identity used to split instances and assign segments.</summary>
        private readonly struct TreeInstanceKey(Point trunkGroundRel, ushort trunkType, ushort supportType)
        {
            public Point TrunkGroundRel { get; } = trunkGroundRel;
            public ushort TrunkType { get; } = trunkType;
            public ushort SupportType { get; } = supportType;
        }

        private TreeLayer() : base("Tree", LayerType.Custom) { }

        /// <summary>Checks type and trunk/branch frames between payload/source intent and target world.</summary>
        public override bool Match(in PasteTile tile) => TileComparison.MatchExact(in tile);

        /// <summary>Returns true when this tile type is handled by tree logic.</summary>
        public static bool IsTreeType(ushort tileType)
        {
            if (tileType >= TileLoader.TileCount)
                return false;

            // Cactus and natural bamboo stalks are column plants pasted like tree clusters.
            if (tileType == TileID.Cactus || tileType == TileID.Bamboo)
                return true;

            return TileID.Sets.IsATreeTrunk[tileType]
                || TileID.Sets.CountsAsGemTree[tileType]
                || TileID.Sets.GetsCheckedForLeaves[tileType];
        }

        /// <summary>Returns one tree instance batch when <paramref name="tile"/> is a trunk-ground anchor.</summary>
        public override LayerResult Identify(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context, PasteSubject identifySubject)
        {
            // --- Only trunk-ground anchors identify; each emits one instance batch for plan deduplication.
            if (!IsTrunkGroundAnchor(tile.Relative, identifySubject, in lookup))
                return LayerResult.IdentifyNone();

            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            if (!subjectTile.HasTile || !IsTreeType(subjectTile.TileType))
                return LayerResult.IdentifyNone();

            var relatives = new List<Point>();
            CollectTreeInstanceRelatives(tile.Relative, identifySubject, in lookup, relatives);
            if (relatives.Count == 0)
                return LayerResult.IdentifyNone();

            bool sourceCut = TileComparison.IsSourceCutIntent(in tile);
            var cells = new List<Blueprint.BlueprintCell>(relatives.Count);
            for (int i = 0; i < relatives.Count; i++)
            {
                Point rel = relatives[i];
                if (!lookup.TryGetPasteTile(rel, out PasteTile neighbor))
                    continue;

                if (identifySubject == PasteSubject.Payload && !neighbor.PayloadCell.HasTile())
                    continue;

                cells.Add(new Blueprint.BlueprintCell(
                    rel,
                    ClusterCellWorld(in tile, rel, in neighbor, sourceCut),
                    true));
            }

            return cells.Count > 0
                ? LayerResult.Identified(
                    TileBatch.FromCells(cells, TileBatchClusterContext.FromRelatives(relatives)),
                    LayerLogFormat.FormatTile(subjectTile))
                : LayerResult.IdentifyNone();
        }

        /// <summary>Determines whether this target tile still needs clearing.</summary>
        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context) =>
            Main.tile[tile.World.X, tile.World.Y].HasTile;

        /// <summary>Validates live trunk support only at trunk-ground anchors.</summary>
        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context)
        {
            // --- Only trunk-ground anchors require live base support; branches defer to bottom-up placement.
            if (!tile.PayloadCell.HasTile())
                return true;

            if (!IsTrunkGroundAnchor(tile.Relative, PasteSubject.Payload, in lookup))
                return true;

            return HasValidTrunkBaseBelow(in tile, in context);
        }

        /// <summary>
        /// Resolves the Living Wood Wand interaction for this tree/cactus segment by tile style.
        /// </summary>
        public static bool TryResolveTreeInteraction(
            in PasteTile tile,
            PasteTileLookup? lookup,
            out InteractionEntry? entry)
        {
            // --- Derive Trees/Palm style from support when possible, then take the styled map row.

            entry = null;
            if (!tile.PayloadCell.HasTile())
                return false;

            ushort tileType = (ushort)tile.PayloadCell.Block.Type;
            int style = ResolveTreeStyle(in tile, lookup);

            foreach (InteractionEntry candidate in InteractionMapProvider.EnumerateTileInteractions(tileType, style, baseId: null))
            {
                entry = candidate;
                return true;
            }

            // Fall back to style 0 when a styled row is missing (older maps / mod trees).
            if (style != 0)
            {
                foreach (InteractionEntry candidate in InteractionMapProvider.EnumerateTileInteractions(tileType, 0, baseId: null))
                {
                    entry = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Resolves the placement style used for Living Wood Wand demand and map lookup.</summary>
        public static int ResolveTreeStyle(in PasteTile tile, PasteTileLookup? lookup)
        {
            // --- Prefer payload-support style when the blueprint includes soil; never read Main.tile here.

            ushort tileType = (ushort)tile.PayloadCell.Block.Type;
            if ((tileType == TileID.Trees || tileType == TileID.PalmTree)
                && TryGetPayloadSupportTileType(in tile, lookup, out ushort supportType, out int supportY)
                && TryMapSupportTileToTreeStyle(tileType, supportType, supportY, out int derivedStyle))
            {
                return derivedStyle;
            }

            return tile.PayloadCell.ResolvePlacementStyle();
        }

        /// <summary>Derives Trees/PalmTree wiki style from the live world using native tree helpers.</summary>
        public static bool TryResolveTreeStyleAtWorld(int x, int y, ushort tileType, out int style)
        {
            // --- Trees: TileDrawing.GetTreeVariant; palms: GetTreeBottom + sand under the foot.

            style = 0;
            if (tileType == TileID.Trees)
                return TryResolveTreesStyleFromTreeVariant(x, y, out style);

            if (tileType != TileID.PalmTree || !WorldGen.InWorld(x, y, 0))
                return false;

            WorldGen.GetTreeBottom(x, y, out int bottomX, out int bottomY);
            int soilY = bottomY + 1;
            if (!WorldGen.InWorld(bottomX, soilY, 0))
                return false;

            Tile soil = Main.tile[bottomX, soilY];
            if (!soil.HasTile || IsTreeType(soil.TileType))
                return false;

            return TryMapSupportTileToTreeStyle(tileType, soil.TileType, soilY, out style);
        }

        /// <summary>
        /// Reads <see cref="TileDrawing.GetTreeVariant"/> for a live Trees tile and maps it onto wiki style.
        /// </summary>
        public static bool TryResolveTreesStyleFromTreeVariant(int x, int y, out int style)
        {
            // --- Draw variants 0-5 match wiki styles; mushroom draw index 6 is wiki style 7.
            // --- Underground mahogany shares draw variant 2 with surface (wiki style 6 is not distinguished).

            style = 0;
            if (!WorldGen.InWorld(x, y, 0))
                return false;

            Tile tile = Main.tile[x, y];
            if (!tile.HasTile || tile.TileType != TileID.Trees)
                return false;

            int variant = TileDrawing.GetTreeVariant(x, y);
            if (variant < 0)
                return false;

            style = variant == 6 ? 7 : variant;
            return style is >= 0 and <= 7;
        }

        /// <summary>Yields wiki style → Living Wood Wand ammo pairs for <see cref="TileID.Trees"/>.</summary>
        public static IEnumerable<(int Style, int AmmoItemId)> EnumerateTreesStyleAmmo()
        {
            // --- Styles match the Terraria wiki Tile ID table for tile 5.

            yield return (0, ItemID.Wood);
            yield return (1, ItemID.Ebonwood);
            yield return (2, ItemID.RichMahogany);
            yield return (3, ItemID.Pearlwood);
            yield return (4, ItemID.BorealWood);
            yield return (5, ItemID.Shadewood);
            yield return (6, ItemID.RichMahogany);
            yield return (7, ItemID.GlowingMushroom);
        }

        /// <summary>Yields palm beach-biome styles → Palm Wood ammo for <see cref="TileID.PalmTree"/>.</summary>
        public static IEnumerable<(int Style, int AmmoItemId)> EnumeratePalmStyleAmmo()
        {
            // --- Styles align with GetPalmTreeVariant / sand biome: beach, corrupt, hallow, crimson.

            yield return (0, ItemID.PalmWood);
            yield return (1, ItemID.PalmWood);
            yield return (2, ItemID.PalmWood);
            yield return (3, ItemID.PalmWood);
        }

        /// <summary>Maps a trunk-support tile onto a Trees or PalmTree wiki style.</summary>
        public static bool TryMapSupportTileToTreeStyle(
            ushort treeTileType,
            ushort supportType,
            int supportY,
            out int style)
        {
            // --- Trees use grass/snow styles; palms use sand biome styles.

            style = 0;
            if (treeTileType == TileID.PalmTree)
            {
                style = supportType switch
                {
                    TileID.Ebonsand => 1,
                    TileID.Pearlsand => 2,
                    TileID.Crimsand => 3,
                    TileID.Sand => 0,
                    _ => -1,
                };
                return style >= 0;
            }

            if (treeTileType != TileID.Trees)
                return false;

            style = supportType switch
            {
                TileID.Grass => 0,
                TileID.CorruptGrass => 1,
                TileID.JungleGrass => supportY >= Main.rockLayer ? 6 : 2,
                TileID.HallowedGrass => 3,
                TileID.SnowBlock => 4,
                TileID.IceBlock => 4,
                TileID.CrimsonGrass => 5,
                TileID.MushroomGrass => 7,
                _ => -1,
            };
            return style >= 0;
        }

        /// <summary>Resolves the wood, cactus, or gem ammo item for a tree/cactus payload segment.</summary>
        public static int ResolveTreeAmmoItemId(in PasteTile tile, PasteTileLookup? lookup)
        {
            // --- Prefer the styled map interaction; fall back to fixed tile-type families.

            if (TryResolveTreeInteraction(in tile, lookup, out InteractionEntry? entry)
                && entry?.AmmoId is int ammoId
                && ammoId > 0)
            {
                return ammoId;
            }

            ushort tileType = (ushort)tile.PayloadCell.Block.Type;

            if (tileType == TileID.Cactus)
                return ItemID.Cactus;

            if (tileType == TileID.Bamboo)
                return ItemID.BambooBlock;

            if (TryResolveGemTreeAmmo(tileType, out int gemAmmo))
                return gemAmmo;

            if (tileType == TileID.TreeAsh)
                return ItemID.AshWood;

            if (tileType == TileID.PalmTree)
                return ItemID.PalmWood;

            return ItemID.Wood;
        }

        /// <summary>Maps gem-tree tile types onto their gem ammo item ids.</summary>
        private static bool TryResolveGemTreeAmmo(ushort tileType, out int ammoItemId)
        {
            // --- Each gem tree tile id places with its matching gem as Living Wood Wand ammo.
            ammoItemId = tileType switch
            {
                TileID.TreeAmethyst => ItemID.Amethyst,
                TileID.TreeTopaz => ItemID.Topaz,
                TileID.TreeSapphire => ItemID.Sapphire,
                TileID.TreeEmerald => ItemID.Emerald,
                TileID.TreeRuby => ItemID.Ruby,
                TileID.TreeDiamond => ItemID.Diamond,
                TileID.TreeAmber => ItemID.Amber,
                _ => 0,
            };
            return ammoItemId > 0;
        }

        /// <summary>Finds non-tree support below this segment from payload lookup only.</summary>
        private static bool TryGetPayloadSupportTileType(
            in PasteTile tile,
            PasteTileLookup? lookup,
            out ushort supportType,
            out int supportWorldY)
        {
            // --- Walk payload relatives downward; do not consult Main.tile.

            supportType = 0;
            supportWorldY = tile.World.Y;
            if (lookup is null)
                return false;

            Point rel = tile.Relative;
            for (int dy = 1; dy < 64; dy++)
            {
                Point belowRel = new(rel.X, rel.Y + dy);
                if (!lookup.TryGetPasteTile(belowRel, out PasteTile below))
                    break;

                if (!below.PayloadCell.HasTile())
                    continue;

                ushort belowType = (ushort)below.PayloadCell.Block.Type;
                if (IsTreeType(belowType))
                    continue;

                supportType = belowType;
                supportWorldY = below.World.Y;
                return true;
            }

            return false;
        }

        /// <summary>Clears one cluster tile per pass: branches top-down, then trunk column top-down.</summary>
        public override LayerResult Clear(TileBatch batch, in PasteContext context)
        {
            // --- Remove branches before the trunk column; within each column clear top-down.
            if (batch.IsEmpty)
                return LayerResult.Ok();

            int trunkMedX = ResolveTrunkColumnX(batch);
            int index = SelectNextClearIndex(batch.Tiles, trunkMedX);
            PasteTile segment = batch.Tiles[index];
            LayerResult result = TileOperations.Clear(segment, in context);
            if (!result.Success)
                return result;

            return RemoveSegmentAt(batch, index, result.Message, result.OkLabel);
        }

        /// <summary>Places one cluster tile per pass: trunk column first, then branches, each bottom-up.</summary>
        public override LayerResult Place(TileBatch batch, in PasteContext context)
        {
            // --- Pick the next trunk-column or branch segment using trunk-first, bottom-up ordering.
            if (batch.IsEmpty)
                return LayerResult.Ok();

            int trunkMedX = ResolveTrunkColumnX(batch);
            int index = SelectNextPlaceIndex(batch.Tiles, trunkMedX);
            PasteTile segment = batch.Tiles[index];
            string message = LayerLogFormat.FormatTile(segment.PayloadTile);
            string okLabel = LayerLogFormat.PlacedLabel(in context);

            if (!segment.PayloadCell.HasTile() || IsSegmentAlreadyPlaced(in segment))
                return RemoveSegmentAt(batch, index, message, okLabel);

            if (!CanPlaceSegment(in segment, in context))
            {
                if (context.RetryOnInvalidPlacement)
                    return LayerResult.OkWithRemaining(batch, message, okLabel);
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            LayerResult result = PlaceTreeSegment(in segment, in context);
            if (!result.Success)
            {
                if (result.FailReason == LayerFailReason.InvalidPlacement && context.RetryOnInvalidPlacement)
                    return LayerResult.OkWithRemaining(batch, message, okLabel);
                return result;
            }

            return RemoveSegmentAt(batch, index, result.Message ?? message, result.OkLabel ?? okLabel);
        }

        public override bool IsCustom(ushort tileType) => IsTreeType(tileType);

        /// <summary>Returns true for cactus and bamboo column plants.</summary>
        private static bool IsColumnPlantType(ushort tileType) =>
            tileType == TileID.Cactus || tileType == TileID.Bamboo;

        /// <summary>Returns true when this relative is the trunk-ground anchor for its column.</summary>
        private static bool IsTrunkGroundAnchor(Point rel, PasteSubject subject, in PasteTileLookup lookup)
        {
            // --- Column plants anchor at column bottom; branched trees anchor at the trunk foot column cell.

            if (!lookup.TryGetPasteTile(rel, out PasteTile pasteTile))
                return false;

            Tile view = PasteTile.GetTileForIdentify(in pasteTile, subject);
            if (!view.HasTile || !IsTreeType(view.TileType))
                return false;

            if (!TryFindTrunkGroundInColumn(rel.X, subject, in lookup, out Point ground) || ground != rel)
                return false;

            if (IsColumnPlantType(view.TileType))
                return true;

            if (!HasNonTreeBelow(rel, subject, in lookup))
                return false;

            if (HasTreeAboveInColumn(rel, subject, in lookup))
                return true;

            return !HasHorizontalTreeNeighborAtSameY(rel, subject, in lookup);
        }

        /// <summary>Finds the deepest tree tile in a lookup column for the identify subject.</summary>
        private static bool TryFindTrunkGroundInColumn(
            int columnX,
            PasteSubject subject,
            in PasteTileLookup lookup,
            out Point trunkGround)
        {
            // --- Scan the column and keep the relative with the largest Y that carries a tree tile.

            trunkGround = default;
            PasteTile[,] grid = lookup.PasteTiles;
            int width = grid.GetLength(0);
            int height = grid.GetLength(1);
            if ((uint)columnX >= (uint)width)
                return false;

            int bestY = int.MinValue;
            bool found = false;
            for (int y = 0; y < height; y++)
            {
                if (!lookup.TryGetPasteTile(new Point(columnX, y), out PasteTile pasteTile))
                    continue;

                Tile view = PasteTile.GetTileForIdentify(in pasteTile, subject);
                if (!view.HasTile || !IsTreeType(view.TileType))
                    continue;

                if (y >= bestY)
                {
                    bestY = y;
                    trunkGround = new Point(columnX, y);
                    found = true;
                }
            }

            return found;
        }

        /// <summary>Returns true when the cell below is not a tree tile in the identify subject.</summary>
        private static bool HasNonTreeBelow(Point rel, PasteSubject subject, in PasteTileLookup lookup)
        {
            // --- Treat paste edges and air as valid ground below the trunk foot.

            Point belowRel = new(rel.X, rel.Y + 1);
            if (!lookup.TryGetPasteTile(belowRel, out PasteTile below))
                return true;

            Tile view = PasteTile.GetTileForIdentify(in below, subject);
            return !view.HasTile || !IsTreeType(view.TileType);
        }

        /// <summary>Returns true when the cell above in the same column is a tree tile.</summary>
        private static bool HasTreeAboveInColumn(Point rel, PasteSubject subject, in PasteTileLookup lookup)
        {
            Point aboveRel = new(rel.X, rel.Y - 1);
            if (!lookup.TryGetPasteTile(aboveRel, out PasteTile above))
                return false;

            Tile view = PasteTile.GetTileForIdentify(in above, subject);
            return view.HasTile && IsTreeType(view.TileType);
        }

        /// <summary>Returns true when a tree tile sits on the left or right at the same Y.</summary>
        private static bool HasHorizontalTreeNeighborAtSameY(Point rel, PasteSubject subject, in PasteTileLookup lookup)
        {
            for (int dx = -1; dx <= 1; dx += 2)
            {
                Point neighborRel = new(rel.X + dx, rel.Y);
                if (!lookup.TryGetPasteTile(neighborRel, out PasteTile neighbor))
                    continue;

                Tile view = PasteTile.GetTileForIdentify(in neighbor, subject);
                if (view.HasTile && IsTreeType(view.TileType))
                    return true;
            }

            return false;
        }

        /// <summary>Resolves support soil for identify: payload scan on paste, subject tile below foot on clear.</summary>
        private static bool TryGetSupportTileTypeForIdentify(
            Point trunkGroundRel,
            PasteSubject subject,
            in PasteTileLookup lookup,
            out ushort supportType)
        {
            // --- Payload uses blueprint soil; Target/Source use the identify subject tile below the trunk foot.

            supportType = 0;
            if (!lookup.TryGetPasteTile(trunkGroundRel, out PasteTile anchor))
                return false;

            if (subject == PasteSubject.Payload)
                return TryGetPayloadSupportTileType(in anchor, lookup, out supportType, out _);

            Point belowRel = new(trunkGroundRel.X, trunkGroundRel.Y + 1);
            if (!lookup.TryGetPasteTile(belowRel, out PasteTile belowTile))
            {
                if (subject != PasteSubject.Target)
                    return false;

                int wx = anchor.World.X;
                int wy = anchor.World.Y + 1;
                if (!WorldGen.InWorld(wx, wy, 0))
                    return false;

                Tile live = Main.tile[wx, wy];
                if (!live.HasTile || IsTreeType(live.TileType))
                    return false;

                supportType = live.TileType;
                return true;
            }

            Tile view = PasteTile.GetTileForIdentify(in belowTile, subject);
            if (!view.HasTile || IsTreeType(view.TileType))
                return false;

            supportType = view.TileType;
            return true;
        }

        /// <summary>Builds the instance key for a trunk-ground anchor.</summary>
        private static bool TryBuildInstanceKey(
            Point anchorRel,
            PasteSubject subject,
            in PasteTileLookup lookup,
            out TreeInstanceKey key)
        {
            key = default;
            if (!lookup.TryGetPasteTile(anchorRel, out PasteTile anchor))
                return false;

            Tile view = PasteTile.GetTileForIdentify(in anchor, subject);
            if (!view.HasTile || !IsTreeType(view.TileType))
                return false;

            TryGetSupportTileTypeForIdentify(anchorRel, subject, in lookup, out ushort supportType);
            key = new TreeInstanceKey(anchorRel, view.TileType, supportType);
            return true;
        }

        /// <summary>Enumerates every trunk-ground anchor in the paste lookup.</summary>
        private static void EnumerateTrunkGroundAnchors(
            PasteSubject subject,
            in PasteTileLookup lookup,
            List<Point> outAnchors)
        {
            // --- Scan the full lookup grid so touching trees get distinct instance roots.

            outAnchors.Clear();
            PasteTile[,] grid = lookup.PasteTiles;
            int width = grid.GetLength(0);
            int height = grid.GetLength(1);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Point rel = new(x, y);
                    if (IsTrunkGroundAnchor(rel, subject, in lookup))
                        outAnchors.Add(rel);
                }
            }
        }

        /// <summary>Assigns tree segments to the nearest compatible trunk-ground anchor.</summary>
        private static void CollectTreeInstanceRelatives(
            Point anchorRel,
            PasteSubject subject,
            in PasteTileLookup lookup,
            List<Point> outPoints)
        {
            // --- Map each tree cell to the closest anchor with matching trunk type, then take this anchor's set.

            outPoints.Clear();
            if (!TryBuildInstanceKey(anchorRel, subject, in lookup, out TreeInstanceKey anchorKey))
                return;

            var anchors = new List<Point>();
            EnumerateTrunkGroundAnchors(subject, in lookup, anchors);
            if (anchors.Count == 0)
                return;

            var anchorKeys = new TreeInstanceKey[anchors.Count];
            for (int i = 0; i < anchors.Count; i++)
            {
                if (!TryBuildInstanceKey(anchors[i], subject, in lookup, out anchorKeys[i]))
                    anchorKeys[i] = default;
            }

            PasteTile[,] grid = lookup.PasteTiles;
            int width = grid.GetLength(0);
            int height = grid.GetLength(1);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Point rel = new(x, y);
                    if (!lookup.TryGetPasteTile(rel, out PasteTile pasteTile))
                        continue;

                    Tile view = PasteTile.GetTileForIdentify(in pasteTile, subject);
                    if (!view.HasTile || !IsTreeType(view.TileType) || view.TileType != anchorKey.TrunkType)
                        continue;

                    int bestIndex = -1;
                    int bestDistance = int.MaxValue;
                    for (int i = 0; i < anchors.Count; i++)
                    {
                        if (anchorKeys[i].TrunkType != view.TileType)
                            continue;

                        Point aRel = anchors[i];
                        int distance = Math.Abs(rel.X - aRel.X) + Math.Abs(rel.Y - aRel.Y);
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            bestIndex = i;
                        }
                    }

                    if (bestIndex >= 0 && anchors[bestIndex] == anchorRel)
                        outPoints.Add(rel);
                }
            }
        }

        /// <summary>Resolves world coordinates for identified cluster cells during normal and source-cut passes.</summary>
        private static Point ClusterCellWorld(in PasteTile anchor, Point relative, in PasteTile lookupTile, bool sourceCut) =>
            sourceCut
                ? new Point(anchor.World.X + relative.X - anchor.Relative.X, anchor.World.Y + relative.Y - anchor.Relative.Y)
                : lookupTile.World;

        /// <summary>Returns the bottom-most trunk relative position using trunk-column tie-breaking.</summary>
        private static Point SelectTrunkGroundRelative(IReadOnlyList<Point> relatives)
        {
            // --- Deepest Y wins; ties break toward the cluster trunk column.
            int medX = ResolveTrunkColumnXFromRelatives(relatives);
            int best = 0;
            for (int i = 1; i < relatives.Count; i++)
            {
                if (CompareTrunkGroundPriority(relatives[i], relatives[best], medX) < 0)
                    best = i;
            }

            return relatives[best];
        }

        /// <summary>Returns true when live world support allows placing this tree segment.</summary>
        internal static bool CanPlaceSegment(in PasteTile segment, in PasteContext context) =>
            HasValidTrunkBaseBelow(in segment, in context) || HasConnectedTrunkSupport(in segment);

        /// <summary>Returns true when a live tree trunk sits below or horizontally adjacent to this segment.</summary>
        private static bool HasConnectedTrunkSupport(in PasteTile segment)
        {
            // --- Branches attach to the trunk column via same-Y neighbors or a trunk segment directly below.
            int x = segment.World.X;
            int y = segment.World.Y;

            int belowY = y + 1;
            if (WorldGen.InWorld(x, belowY, 0))
            {
                Tile below = Main.tile[x, belowY];
                if (below.HasTile && IsTreeType(below.TileType))
                    return true;
            }

            for (int dx = -1; dx <= 1; dx += 2)
            {
                int nx = x + dx;
                if (!WorldGen.InWorld(nx, y, 0))
                    continue;

                Tile neighbor = Main.tile[nx, y];
                if (neighbor.HasTile && IsTreeType(neighbor.TileType))
                    return true;
            }

            return false;
        }

        /// <summary>Resolves the trunk column X from the batch cluster context.</summary>
        private static int ResolveTrunkColumnX(TileBatch batch)
        {
            // --- Use the immutable full-cluster footprint so branch-only remaining queues stay ordered correctly.
            IReadOnlyList<Point>? relatives = batch.ClusterContext?.AllRelatives;
            if (relatives is not null && relatives.Count > 0)
                return ResolveTrunkColumnXFromRelatives(relatives);

            return ResolveTrunkColumnXFromPending(batch.Tiles);
        }

        /// <summary>Picks the trunk column from cluster relatives by tile count, then deepest reach.</summary>
        private static int ResolveTrunkColumnXFromRelatives(IReadOnlyList<Point> relatives)
        {
            // --- The tallest column with the most segments is treated as the trunk column.
            var countByX = new Dictionary<int, int>();
            var maxYByX = new Dictionary<int, int>();
            for (int i = 0; i < relatives.Count; i++)
            {
                int x = relatives[i].X;
                int y = relatives[i].Y;
                countByX[x] = countByX.GetValueOrDefault(x) + 1;
                if (!maxYByX.TryGetValue(x, out int maxY) || y > maxY)
                    maxYByX[x] = y;
            }

            int bestX = relatives[0].X;
            int bestCount = -1;
            int bestMaxY = -1;
            foreach (var kv in countByX)
            {
                int x = kv.Key;
                int count = kv.Value;
                int maxY = maxYByX[x];
                if (count > bestCount || (count == bestCount && maxY > bestMaxY))
                {
                    bestCount = count;
                    bestMaxY = maxY;
                    bestX = x;
                }
            }

            return bestX;
        }

        /// <summary>Picks the trunk column from a pending batch when lookup is unavailable.</summary>
        private static int ResolveTrunkColumnXFromPending(IReadOnlyList<PasteTile> tiles)
        {
            // --- Fall back to pending geometry using the same count-and-depth heuristic.
            var relatives = new List<Point>(tiles.Count);
            for (int i = 0; i < tiles.Count; i++)
                relatives.Add(tiles[i].Relative);

            return ResolveTrunkColumnXFromRelatives(relatives);
        }

        /// <summary>Removes one segment from the batch and returns remaining work or completion.</summary>
        private static LayerResult RemoveSegmentAt(
            TileBatch batch,
            int index,
            string? message,
            string? okLabel)
        {
            // --- Drop the handled segment and continue with the rest of the cluster batch.
            var remaining = new List<PasteTile>(batch.Tiles.Count - 1);
            for (int i = 0; i < batch.Tiles.Count; i++)
            {
                if (i != index)
                    remaining.Add(batch.Tiles[i]);
            }

            return remaining.Count > 0
                ? LayerResult.OkWithRemaining(batch.WithRemainingTiles(remaining), message, okLabel)
                : LayerResult.Ok(message, okLabel);
        }

        /// <summary>Returns true when the live tile already matches the payload tree segment.</summary>
        private static bool IsSegmentAlreadyPlaced(in PasteTile segment)
        {
            if (!segment.PayloadCell.HasTile())
                return false;

            Tile live = Main.tile[segment.World.X, segment.World.Y];
            if (!live.HasTile || live.TileType != segment.PayloadCell.Block.Type)
                return false;

            return live.TileFrameX == segment.PayloadCell.Block.FrameX
                && live.TileFrameY == segment.PayloadCell.Block.FrameY;
        }

        /// <summary>Places one tree trunk tile by writing payload framing directly.</summary>
        private static LayerResult PlaceTreeSegment(in PasteTile segment, in PasteContext context)
        {
            // --- Support is validated before this call; blit exact frames (no TileFrame hooks — they would reframe branches).
            int x = segment.World.X;
            int y = segment.World.Y;
            if (!segment.PayloadCell.HasTile())
                return LayerResult.Ok();

            string message = LayerLogFormat.FormatTile(segment.PayloadTile);
            string okLabel = LayerLogFormat.PlacedLabel(in context);
            ushort tileType = (ushort)segment.PayloadCell.Block.Type;
            Tile dest = Main.tile[x, y];

            if (dest.HasTile && dest.TileType != tileType && context.UseReplacePlace)
            {
                if (!TileExecutor.EnqueueVoidKillTile(x, y, in context))
                    return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            if (!TileExecutor.EnqueueApplyPayloadBlock(x, y, in segment.PayloadCell, in context))
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);

            if (!TryReserveTreeSegmentItems(in segment, in context))
                return LayerResult.Fail(LayerFailReason.MissingItems, message);

            return LayerResult.Ok(message, okLabel);
        }

        /// <summary>Reserves Living Wood Wand ammo; ApplyPayloadBlock does not consume on its own.</summary>
        private static bool TryReserveTreeSegmentItems(in PasteTile segment, in PasteContext context)
        {
            if (!segment.PayloadCell.HasTile() || !context.RequireItems || context.InventoryLookup is null || context.IsSweep)
                return true;
            if (!TryResolveTreeInteraction(in segment, context.Lookup, out InteractionEntry? entry) || entry is null)
                return false;

            return context.InventoryLookup.TryReserveForInteraction(
                entry,
                context.InteractionBudget,
                context.Player,
                out _);
        }

        /// <summary>Returns the next clear index: branch columns top-down, then trunk column top-down.</summary>
        private static int SelectNextClearIndex(IReadOnlyList<PasteTile> pending, int trunkMedX)
        {
            // --- Clear branches before the trunk column; within each column remove the highest segment first.
            int selected = -1;
            for (int i = 0; i < pending.Count; i++)
            {
                if (HasPendingSegmentAboveInColumn(pending, pending[i]))
                    continue;

                if (selected < 0
                    || CompareClearOrder(pending[i].Relative, pending[selected].Relative, trunkMedX) < 0)
                    selected = i;
            }

            if (selected >= 0)
                return selected;

            selected = 0;
            for (int i = 1; i < pending.Count; i++)
            {
                if (CompareClearOrder(pending[i].Relative, pending[selected].Relative, trunkMedX) < 0)
                    selected = i;
            }

            return selected;
        }

        /// <summary>Returns true when a higher segment in the same column still needs clearing.</summary>
        private static bool HasPendingSegmentAboveInColumn(IReadOnlyList<PasteTile> pending, in PasteTile segment)
        {
            for (int i = 0; i < pending.Count; i++)
            {
                if (pending[i].Relative.X == segment.Relative.X
                    && pending[i].Relative.Y < segment.Relative.Y)
                    return true;
            }

            return false;
        }

        /// <summary>Negative if <paramref name="aRel"/> should be cleared before <paramref name="bRel"/>.</summary>
        private static int CompareClearOrder(Point aRel, Point bRel, int trunkMedX) =>
            -ComparePlaceOrder(aRel, bRel, trunkMedX);

        /// <summary>Returns the next place index: trunk column bottom-up, then branch columns bottom-up.</summary>
        private static int SelectNextPlaceIndex(IReadOnlyList<PasteTile> pending, int trunkMedX)
        {
            // --- Exhaust the median trunk column before any branch column; within each column go bottom-up.
            int selected = -1;
            for (int i = 0; i < pending.Count; i++)
            {
                if (HasPendingSegmentBelowInColumn(pending, pending[i]))
                    continue;

                if (selected < 0
                    || ComparePlaceOrder(pending[i].Relative, pending[selected].Relative, trunkMedX) < 0)
                    selected = i;
            }

            if (selected >= 0)
                return selected;

            selected = 0;
            for (int i = 1; i < pending.Count; i++)
            {
                if (ComparePlaceOrder(pending[i].Relative, pending[selected].Relative, trunkMedX) < 0)
                    selected = i;
            }

            return selected;
        }

        /// <summary>Returns true when a deeper segment in the same column still needs placement.</summary>
        private static bool HasPendingSegmentBelowInColumn(IReadOnlyList<PasteTile> pending, in PasteTile segment)
        {
            for (int i = 0; i < pending.Count; i++)
            {
                if (pending[i].Relative.X == segment.Relative.X
                    && pending[i].Relative.Y > segment.Relative.Y)
                    return true;
            }

            return false;
        }

        /// <summary>Negative if <paramref name="aRel"/> should be placed before <paramref name="bRel"/>.</summary>
        private static int ComparePlaceOrder(Point aRel, Point bRel, int trunkMedX)
        {
            bool aTrunk = aRel.X == trunkMedX;
            bool bTrunk = bRel.X == trunkMedX;
            if (aTrunk != bTrunk)
                return aTrunk ? -1 : 1;

            int y = bRel.Y.CompareTo(aRel.Y);
            if (y != 0)
                return y;

            return aRel.X.CompareTo(bRel.X);
        }

        /// <summary>Returns true when the tile below provides valid support for the bottom-most trunk.</summary>
        private static bool HasValidTrunkBaseBelow(in PasteTile tile, in PasteContext context)
        {
            // --- Mapped base tiles when required; generic solid only when no base row exists; cheat sweep may force.
            int x = tile.World.X;
            int y = tile.World.Y;
            ushort tileType = (ushort)tile.PayloadCell.Block.Type;
            int style = ResolveTreeStyle(in tile, context.Lookup);

            if (IsValidSupportTile(Main.tile[x, y], tileType, style))
                return true;

            int belowY = y + 1;
            if (!WorldGen.InWorld(x, belowY, 0))
                return false;

            if (IsValidSupportTile(Main.tile[x, belowY], tileType, style))
                return true;

            int? baseTileId = InteractionMapProvider.GetRequiredBaseTile(tileType, style);
            if (!baseTileId.HasValue && WorldGen.SolidOrSlopedTile(x, belowY))
                return true;

            return context.IsSweep && TileLoader.CanPlace(x, y, tileType);
        }

        /// <summary>Returns true when a live tile satisfies tree ground support for the payload type.</summary>
        private static bool IsValidSupportTile(in Tile support, ushort treeTileType, int treeStyle)
        {
            if (!support.HasTile || IsTreeType(support.TileType))
                return false;

            int? baseTileId = InteractionMapProvider.GetRequiredBaseTile(treeTileType, treeStyle);
            if (baseTileId.HasValue)
                return support.TileType == (ushort)baseTileId.Value;

            return WorldGen.SolidTile(support);
        }

        /// <summary>Negative if <paramref name="aRel"/> is a better trunk anchor than <paramref name="bRel"/>.</summary>
        private static int CompareTrunkGroundPriority(Point aRel, Point bRel, int medX)
        {
            int y = bRel.Y.CompareTo(aRel.Y);
            if (y != 0)
                return y;

            int ax = Math.Abs(aRel.X - medX);
            int bx = Math.Abs(bRel.X - medX);
            int tie = ax.CompareTo(bx);
            return tie != 0 ? tie : aRel.X.CompareTo(bRel.X);
        }

        public override bool DrawsCells => true;

        public override void DrawCell(in PreviewDrawContext ctx, PayloadCell cell, int relX, int relY)
        {
            if (!cell.Detail.HasTile)
                return;

            ushort tileType = cell.Block.Type;
            if (IsTreeType(tileType) && !LayerPreviewSprites.HasDetailModifiedSprite(cell))
            {
                if (LayerPreviewSprites.TryResolveSprite(in ctx, cell, relX, relY, out PreviewTileDraw.DrawSprite sprite))
                    LayerPreviewSprites.DrawSprite(in ctx, relX, relY, in sprite);
            }

            TreeFoliagePreviewDraw.Draw(in ctx, cell, relX, relY);
        }
    }
}
