using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.Layers;

namespace TheArchitectsBlueprintMod.UI.Preview
{
    /// <summary>Resolves biome wood columns and TreeTop framing from payload soil, not live Main.tile.</summary>
    internal static class TreePreviewResolve
    {
        private const int DefaultTopSize = 80;
        private const int JungleTopWidth = 114;
        private const int JungleAltTopWidth = 116;
        private const int JungleTopHeight = 96;
        private const int HallowTopHeight = 140;
        private const int HallowAltTopWidth = 120;
        private const int GemAshTopWidth = 116;
        private const int GemAshTopHeight = 96;
        private const int VanityTopWidth = 118;
        private const int VanityTopHeight = 96;

        /// <summary>Biome wood, palm, and foliage parameters for one payload tree cell.</summary>
        internal readonly struct Style
        {
            /// <summary>GetTreeVariant value used as the Tiles_5 column; -1 is forest.</summary>
            public int WoodVariant { get; init; }

            /// <summary>Palm sand biome 0-3, or -1 when this is not a palm.</summary>
            public int PalmBiome { get; init; }

            /// <summary>TextureAssets.TreeTop / TreeBranch index.</summary>
            public int FoliageIndex { get; init; }

            /// <summary>TreeTop source width in pixels.</summary>
            public int TopWidth { get; init; }

            /// <summary>TreeTop source height in pixels.</summary>
            public int TopHeight { get; init; }

            /// <summary>Extra TreeTop frame added for Hallow color variants.</summary>
            public int TreeFrameBonus { get; init; }
        }

        /// <summary>Builds wood and foliage style for a payload tree cell from its supporting soil.</summary>
        public static Style Resolve(in PreviewDrawContext ctx, ushort tileType, short frameX, short frameY, int relX, int relY)
        {
            // --- Map tile id first, then soil under the trunk column, matching vanilla foliage tables.

            if (TryResolveDedicatedTree(tileType, out Style dedicated))
                return dedicated;

            int lookupX = relX;
            if (tileType == TileID.Trees)
                AdjustTrunkColumn(frameX, frameY, ref lookupX);

            ushort supportType = GetSupportTileType(in ctx, lookupX, relY, out int supportRelY);
            int? worldX = ShiftWorldCoord(ctx.WorldOriginX, lookupX);
            int? worldY = ShiftWorldCoord(ctx.WorldOriginY, supportRelY);

            if (tileType == TileID.PalmTree)
                return ResolvePalm(supportType);

            return ResolveCommonTree(supportType, worldX, worldY, relX);
        }

        /// <summary>Returns true for forest, palm, gem, ash, and vanity trunks, excluding cactus.</summary>
        public static bool IsFoliageTreeType(ushort tileType)
        {
            // --- Reuse TreeLayer detection, then drop cactus which is not a TreeTop overlay.

            // Cactus uses its own tile sheet and is not a TreeTop overlay.
            if (tileType == TileID.Cactus)
                return false;

            return TreeLayer.IsTreeType(tileType);
        }

        /// <summary>Maps gem, ash, and vanity tile ids onto their fixed TreeTop indices and sizes.</summary>
        private static bool TryResolveDedicatedTree(ushort tileType, out Style style)
        {
            // --- These trees are their own tile types, so foliage does not come from grass biome.

            style = default;
            switch (tileType)
            {
                case TileID.TreeAsh:
                    style = Dedicated(31, GemAshTopWidth, GemAshTopHeight);
                    return true;
                case TileID.VanityTreeSakura:
                    style = Dedicated(29, VanityTopWidth, VanityTopHeight);
                    return true;
                case TileID.VanityTreeYellowWillow:
                    style = Dedicated(30, VanityTopWidth, VanityTopHeight);
                    return true;
                case TileID.TreeAmethyst:
                    style = Dedicated(22, GemAshTopWidth, GemAshTopHeight);
                    return true;
                case TileID.TreeTopaz:
                    style = Dedicated(23, GemAshTopWidth, GemAshTopHeight);
                    return true;
                case TileID.TreeSapphire:
                    style = Dedicated(24, GemAshTopWidth, GemAshTopHeight);
                    return true;
                case TileID.TreeEmerald:
                    style = Dedicated(25, GemAshTopWidth, GemAshTopHeight);
                    return true;
                case TileID.TreeRuby:
                    style = Dedicated(26, GemAshTopWidth, GemAshTopHeight);
                    return true;
                case TileID.TreeDiamond:
                    style = Dedicated(27, GemAshTopWidth, GemAshTopHeight);
                    return true;
                case TileID.TreeAmber:
                    style = Dedicated(28, GemAshTopWidth, GemAshTopHeight);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Builds palm beach-biome wood and TreeTop 15 parameters from the supporting sand.</summary>
        private static Style ResolvePalm(ushort supportType)
        {
            // --- Map sand onto GetPalmTreeVariant 0-3; oasis size needs world X so preview stays beach-sized.

            int palmBiome = supportType switch
            {
                TileID.Ebonsand => 1,
                TileID.Pearlsand => 2,
                TileID.Crimsand => 3,
                _ => 0,
            };

            return new Style
            {
                WoodVariant = -1,
                PalmBiome = palmBiome,
                FoliageIndex = 15,
                TopWidth = DefaultTopSize,
                TopHeight = DefaultTopSize,
            };
        }

        /// <summary>Maps grass/snow soil onto Tiles_5 wood columns and GetCommonTreeFoliageData TreeTop frames.</summary>
        private static Style ResolveCommonTree(ushort supportType, int? worldX, int? worldY, int relX)
        {
            // --- Wood uses GetTreeVariant indices; foliage uses TreeTop indices and per-biome source sizes.

            int woodVariant = supportType switch
            {
                TileID.CorruptGrass or TileID.CorruptJungleGrass => 0,
                TileID.JungleGrass => IsUnderground(worldY) ? 5 : 1,
                TileID.HallowedGrass or TileID.GolfGrassHallowed => 2,
                TileID.SnowBlock => 3,
                TileID.CrimsonGrass or TileID.CrimsonJungleGrass => 4,
                TileID.MushroomGrass => 6,
                _ => -1,
            };

            Style foliage = supportType switch
            {
                TileID.CorruptGrass or TileID.CorruptJungleGrass =>
                    Common(foliageIndex: 1, DefaultTopSize, DefaultTopSize),
                TileID.MushroomGrass =>
                    Common(foliageIndex: 14, DefaultTopSize, DefaultTopSize),
                TileID.CrimsonGrass or TileID.CrimsonJungleGrass =>
                    Common(foliageIndex: 5, DefaultTopSize, DefaultTopSize),
                TileID.JungleGrass => ResolveJungleFoliage(worldY),
                TileID.SnowBlock => ResolveSnowFoliage(worldX),
                TileID.HallowedGrass or TileID.GolfGrassHallowed => ResolveHallowFoliage(worldX, relX),
                TileID.Grass or TileID.GolfGrass => ResolveForestFoliage(worldX),
                _ => ResolveForestFoliage(worldX),
            };

            return new Style
            {
                WoodVariant = woodVariant,
                PalmBiome = -1,
                FoliageIndex = foliage.FoliageIndex,
                TopWidth = foliage.TopWidth,
                TopHeight = foliage.TopHeight,
                TreeFrameBonus = foliage.TreeFrameBonus,
            };
        }

        /// <summary>Picks jungle TreeTop 2/11/13 and the matching 114 or 116 pixel crown width.</summary>
        private static Style ResolveJungleFoliage(int? worldY)
        {
            // --- Surface uses jungle TreeTops style; below world surface vanilla switches to style 13.

            if (IsUnderground(worldY))
                return Common(13, JungleAltTopWidth, JungleTopHeight);

            int jungleTops = WorldGen.TreeTops.GetTreeStyle(5);
            if (jungleTops == 1)
                return Common(11, JungleAltTopWidth, JungleTopHeight);

            return Common(2, JungleTopWidth, JungleTopHeight);
        }

        /// <summary>Picks snow TreeTop 4 or the world TreeTops variants 12/16/17/18.</summary>
        private static Style ResolveSnowFoliage(int? worldX)
        {
            // --- Default boreal tops are index 4; world snow TreeTops style can swap in alt sheets.

            int snowTops = WorldGen.TreeTops.GetTreeStyle(6);
            if (snowTops == 0)
            {
                int style = worldX is int x && x % 10 == 0 ? 18 : 12;
                return Common(style, DefaultTopSize, DefaultTopSize);
            }

            if (snowTops is 2 or 3 or 32 or 4 or 42 or 5 or 7)
            {
                bool left = worldX is int x && x < Main.maxTilesX / 2;
                int style = snowTops % 2 == 0
                    ? (left ? 16 : 17)
                    : (left ? 17 : 16);
                return Common(style, DefaultTopSize, DefaultTopSize);
            }

            return Common(4, DefaultTopSize, DefaultTopSize);
        }

        /// <summary>Picks Hallow TreeTop style and the extra color-frame offset from world X.</summary>
        private static Style ResolveHallowFoliage(int? worldX, int relX)
        {
            // --- Hollow tops are taller than forest; style 20 and 19 add extra TreeTop columns.

            int foliageIndex = WorldGen.GetHollowTreeFoliageStyle();
            int width = foliageIndex == 19 ? HallowAltTopWidth : DefaultTopSize;
            int frameBonus = 0;
            int x = worldX ?? relX;
            if (foliageIndex == 20)
            {
                int slot = x % 6;
                if (slot is >= 1 and <= 5)
                    frameBonus = slot * 3;
            }
            else
            {
                int slot = x % 3;
                if (slot is 1 or 2)
                    frameBonus = slot * 3;
            }

            return new Style
            {
                FoliageIndex = foliageIndex,
                TopWidth = width,
                TopHeight = HallowTopHeight,
                TreeFrameBonus = frameBonus,
            };
        }

        /// <summary>Picks forest TreeTop 0/10/5+style from the world forest region at this X.</summary>
        private static Style ResolveForestFoliage(int? worldX)
        {
            // --- Forest crowns follow TreeTopsInfo for the world strip containing this X.

            int regionStyle = 0;
            if (worldX is int x)
            {
                regionStyle = x <= Main.treeX[0]
                    ? WorldGen.TreeTops.GetTreeStyle(0)
                    : x <= Main.treeX[1]
                        ? WorldGen.TreeTops.GetTreeStyle(1)
                        : x > Main.treeX[2]
                            ? WorldGen.TreeTops.GetTreeStyle(3)
                            : WorldGen.TreeTops.GetTreeStyle(2);
            }

            int foliageIndex = regionStyle switch
            {
                0 => 0,
                5 => 10,
                _ => 5 + regionStyle,
            };
            return Common(foliageIndex, DefaultTopSize, DefaultTopSize);
        }

        /// <summary>Shifts a tree lookup X to the trunk column using vanilla GetTreeBiome frame rules.</summary>
        private static void AdjustTrunkColumn(short frameX, short frameY, ref int relX)
        {
            // --- Branches and jogs sit beside the trunk; soil is under the trunk, not the overlay cell.

            if (frameX == 66 && frameY <= 45)
                relX++;
            if (frameX == 88 && frameY >= 66 && frameY <= 110)
                relX--;

            if (frameY >= 198)
            {
                switch (frameX)
                {
                    case 66:
                        relX--;
                        break;
                    case 44:
                        relX++;
                        break;
                }
            }
            else if (frameY >= 132)
            {
                switch (frameX)
                {
                    case 22:
                        relX--;
                        break;
                    case 44:
                        relX++;
                        break;
                }
            }
        }

        /// <summary>Walks down the payload column until a non-tree support tile is found.</summary>
        private static ushort GetSupportTileType(in PreviewDrawContext ctx, int relX, int relY, out int supportRelY)
        {
            // --- Scan below the trunk in the sparse payload map, stopping at air or a non-tree block.

            supportRelY = relY;
            if (ctx.CellLookup is null)
                return 0;

            for (int dy = 1; dy < 64; dy++)
            {
                supportRelY = relY + dy;
                if (!ctx.CellLookup.TryGetValue(new Point(relX, supportRelY), out PayloadCell below))
                    return 0;

                if (!below.Detail.HasTile)
                    return 0;

                if (!IsFoliageTreeType(below.Block.Type))
                    return below.Block.Type;
            }

            return 0;
        }

        /// <summary>Adds a relative payload coordinate onto an optional world origin.</summary>
        private static int? ShiftWorldCoord(int? origin, int relative)
        {
            // --- World-space X/Y are only available for in-world projections, not UI thumbnails.

            return origin is int value ? value + relative : null;
        }

        /// <summary>Returns whether the soil sits below the world surface, matching GetTreeVariant.</summary>
        private static bool IsUnderground(int? worldY)
        {
            // --- Underground mahogany uses a different Tiles_5 column and TreeTop 13.

            return worldY is int y && y > Main.worldSurface;
        }

        /// <summary>Creates foliage-only style fields with forest wood defaults.</summary>
        private static Style Common(int foliageIndex, int width, int height) => new()
        {
            WoodVariant = -1,
            PalmBiome = -1,
            FoliageIndex = foliageIndex,
            TopWidth = width,
            TopHeight = height,
        };

        /// <summary>Creates a dedicated gem/ash/vanity style with no biome wood column.</summary>
        private static Style Dedicated(int foliageIndex, int width, int height) => new()
        {
            WoodVariant = -1,
            PalmBiome = -1,
            FoliageIndex = foliageIndex,
            TopWidth = width,
            TopHeight = height,
        };
    }
}
