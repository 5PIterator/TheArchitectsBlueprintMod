using System;
using Terraria;
using Terraria.ID;

namespace ArchitectsStencilMod.Core.Client.PayloadImport
{
    /// <summary>
    /// Merges layer facets into a <see cref="Tile"/> using property writes on <c>new Tile()</c> (same pattern as
    /// network <c>ReadTile</c>), extended for wall frames, paint, and liquid flags.
    /// </summary>
    public static class PayloadToTile
    {
        public static Tile MergeToTile(PayloadFacets? facets)
        {
            var tile = new Tile();
            if (facets is null)
                return tile;

            ApplyBlock(facets.Block, ref tile);
            ApplyWall(facets.Wall, ref tile);
            ApplyLiquid(facets.Liquid, ref tile);
            ApplyPaint(facets.Paint, ref tile);
            ApplyWiring(facets.Wiring, ref tile);
            ApplyDetail(facets.Detail, ref tile);
            // Frame facet: metadata only in v1 (AnchorKind); no Tile fields to set.

            return tile;
        }

        public static PayloadFacets? SplitFromTile(Tile tile)
        {
            bool hasWall = tile.WallType != WallID.None;
            bool hasBlock = tile.HasTile;
            bool hasLiquid = tile.LiquidAmount > 0;
            bool hasPaint = tile.TileColor != PaintID.None || tile.WallColor != PaintID.None
                || tile.IsTileInvisible || tile.IsWallInvisible || tile.IsTileFullbright || tile.IsWallFullbright;
            bool hasWiring = tile.RedWire || tile.BlueWire || tile.GreenWire || tile.YellowWire || tile.HasActuator;
            bool hasDetail = tile.IsActuated || tile.BlockType != BlockType.Solid || tile.Slope != SlopeType.Solid
                || tile.IsHalfBlock;

            ref var tws = ref tile.Get<TileWallWireStateData>();
            int nonFrame = tws.NonFrameBits;
            bool hasBits = nonFrame != 0;

            if (!hasWall && !hasBlock && !hasLiquid && !hasPaint && !hasWiring && !hasDetail && !hasBits)
                return null;

            var f = new PayloadFacets();
            if (hasWall)
            {
                f.Wall = new WallFacetDto
                {
                    Type = tile.WallType,
                    Color = tile.WallColor,
                    FrameX = tile.WallFrameX,
                    FrameY = tile.WallFrameY,
                    FrameNumber = tile.WallFrameNumber,
                };
            }

            if (hasBlock)
            {
                f.Block = new BlockFacetDto
                {
                    HasTile = tile.HasTile,
                    TileType = tile.TileType,
                    FrameX = tile.TileFrameX,
                    FrameY = tile.TileFrameY,
                    FrameNumber = tile.TileFrameNumber,
                };
            }

            if (hasLiquid)
            {
                f.Liquid = new LiquidFacetDto
                {
                    Amount = tile.LiquidAmount,
                    LiquidType = tile.LiquidType,
                    SkipLiquid = tile.SkipLiquid,
                    CheckingLiquid = tile.CheckingLiquid,
                };
            }

            if (hasPaint)
            {
                f.Paint = new PaintFacetDto
                {
                    TileColor = tile.TileColor,
                    WallColor = tile.WallColor,
                    IsTileInvisible = tile.IsTileInvisible,
                    IsWallInvisible = tile.IsWallInvisible,
                    IsTileFullbright = tile.IsTileFullbright,
                    IsWallFullbright = tile.IsWallFullbright,
                };
            }

            if (hasWiring)
            {
                f.Wiring = new WiringFacetDto
                {
                    RedWire = tile.RedWire,
                    BlueWire = tile.BlueWire,
                    GreenWire = tile.GreenWire,
                    YellowWire = tile.YellowWire,
                    HasActuator = tile.HasActuator,
                    WireData = tws.WireData,
                };
            }

            if (hasDetail)
            {
                f.Detail = new DetailFacetDto
                {
                    IsActuated = tile.IsActuated,
                    BlockType = (byte)tile.BlockType,
                    Slope = (byte)tile.Slope,
                    IsHalfBlock = tile.IsHalfBlock,
                    NonFrameBits = nonFrame,
                };
            }
            else if (hasBits)
            {
                f.Detail = new DetailFacetDto { NonFrameBits = nonFrame };
            }

            return f;
        }

        private static void ApplyBlock(BlockFacetDto? b, ref Tile tile)
        {
            if (b is null)
                return;

            if (b.HasTile)
            {
                tile.HasTile = true;
                tile.TileType = b.TileType;
                tile.TileFrameX = b.FrameX;
                tile.TileFrameY = b.FrameY;
                tile.TileFrameNumber = b.FrameNumber;
            }
            else
            {
                tile.HasTile = false;
            }
        }

        private static void ApplyWall(WallFacetDto? w, ref Tile tile)
        {
            if (w is null)
                return;

            tile.WallType = w.Type;
            tile.WallColor = w.Color;
            tile.WallFrameX = w.FrameX;
            tile.WallFrameY = w.FrameY;
            tile.WallFrameNumber = w.FrameNumber;
        }

        private static void ApplyLiquid(LiquidFacetDto? l, ref Tile tile)
        {
            if (l is null)
                return;

            tile.LiquidAmount = l.Amount;
            tile.LiquidType = l.LiquidType;
            tile.SkipLiquid = l.SkipLiquid;
            tile.CheckingLiquid = l.CheckingLiquid;
        }

        private static void ApplyPaint(PaintFacetDto? p, ref Tile tile)
        {
            if (p is null)
                return;

            tile.TileColor = p.TileColor;
            tile.WallColor = p.WallColor;
            tile.IsTileInvisible = p.IsTileInvisible;
            tile.IsWallInvisible = p.IsWallInvisible;
            tile.IsTileFullbright = p.IsTileFullbright;
            tile.IsWallFullbright = p.IsWallFullbright;
        }

        private static void ApplyWiring(WiringFacetDto? w, ref Tile tile)
        {
            if (w is null)
                return;

            tile.RedWire = w.RedWire;
            tile.BlueWire = w.BlueWire;
            tile.GreenWire = w.GreenWire;
            tile.YellowWire = w.YellowWire;
            tile.HasActuator = w.HasActuator;

            // WireData is packed state; individual wire bools above are the supported write path in tML.
        }

        private static void ApplyDetail(DetailFacetDto? d, ref Tile tile)
        {
            if (d is null)
                return;

            tile.IsActuated = d.IsActuated;
            tile.BlockType = (BlockType)d.BlockType;
            tile.Slope = (SlopeType)d.Slope;
            tile.IsHalfBlock = d.IsHalfBlock;
            // NonFrameBits is read-only on TileWallWireStateData; omit direct assign on merge.
        }
    }
}
