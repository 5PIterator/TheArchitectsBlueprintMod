using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Terraria.ID;
using ArchitectsStencilMod.Core.Client.PayloadImport;

namespace ArchitectsStencilMod.Core.Client.PayloadImport.Migration
{
    /// <summary>
    /// CheatSheet brush JSON: root is a 2D array; implicit schema <see cref="MigrationSystem.CheatSheet"/> (no JSON field).
    /// Identification mirrors layer-style classify-then-act: <see cref="Identify"/> then <see cref="ImportToCurrent"/>.
    /// </summary>
    public static class CheatSheetPayloadFormat
    {
        private static readonly JsonSerializerOptions DeserializeOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>
        /// Whether <paramref name="root"/> is CheatSheet layout (jagged 2D array of component cells).
        /// </summary>
        public static bool Identify(JsonElement root) => root.ValueKind == JsonValueKind.Array;

        /// <summary>
        /// Deserializes CheatSheet JSON and returns a <see cref="PayloadDocument"/> at <see cref="PayloadJson.CurrentSchemaVersion"/>.
        /// </summary>
        public static PayloadDocument ImportToCurrent(string json, string? projectName = null, string? playerName = null)
        {
            List<List<CheatSheetCellDto?>>? grid = JsonSerializer.Deserialize<List<List<CheatSheetCellDto?>>>(json, DeserializeOptions);
            if (grid is null)
                throw new InvalidDataException("CheatSheet JSON deserialized to null.");
            return FromGrid(grid, projectName, playerName);
        }

        /// <summary>
        /// Same as <see cref="ImportToCurrent(string, string?, string?)"/> when <paramref name="root"/> is already known to match <see cref="Identify"/>.
        /// </summary>
        public static PayloadDocument ImportToCurrent(string json, JsonElement root, string? projectName = null, string? playerName = null)
        {
            if (!Identify(root))
                throw new InvalidDataException("Root JSON is not a CheatSheet 2D array.");
            return ImportToCurrent(json, projectName, playerName);
        }

        /// <summary>
        /// Converts an in-memory CheatSheet grid to the current facet document.
        /// </summary>
        public static PayloadDocument FromGrid(
            IReadOnlyList<IReadOnlyList<CheatSheetCellDto?>> grid,
            string? projectName = null,
            string? playerName = null)
        {
            if (grid is null || grid.Count == 0)
                throw new InvalidDataException("CheatSheet grid is empty.");

            int height = grid.Count;
            int width = 0;
            for (int y = 0; y < height; y++)
            {
                IReadOnlyList<CheatSheetCellDto?>? row = grid[y];
                if (row is not null && row.Count > width)
                    width = row.Count;
            }

            if (width <= 0)
                throw new InvalidDataException("CheatSheet grid has no columns.");

            var doc = new PayloadDocument
            {
                SchemaVersion = PayloadJson.CurrentSchemaVersion,
                ProjectName = projectName ?? string.Empty,
                PlayerName = playerName ?? string.Empty,
                Width = width,
                Height = height,
                Payload = [],
            };

            for (int y = 0; y < height; y++)
            {
                IReadOnlyList<CheatSheetCellDto?>? row = grid[y];
                if (row is null)
                    continue;

                for (int x = 0; x < row.Count; x++)
                {
                    CheatSheetCellDto? cell = row[x];
                    if (cell is null)
                        continue;

                    PayloadFacets? facets = ConvertCellToFacets(cell);
                    if (facets is null)
                        continue;

                    doc.Payload.Add(new PayloadCell { X = x, Y = y, Facets = facets });
                }
            }

            return doc;
        }

        private static PayloadFacets? ConvertCellToFacets(CheatSheetCellDto cell)
        {
            CheatSheetTileWallWireStateData? tw = cell.TileWallWireStateData;
            CheatSheetWallTypeData? wallData = cell.WallTypeData;
            CheatSheetTileTypeData? tileTypeData = cell.TileTypeData;
            CheatSheetLiquidData? liquid = cell.LiquidData;
            CheatSheetTileWallBrightnessInvisibilityData? bright = cell.TileWallBrightnessInvisibilityData;

            ushort wallType = wallData?.Type ?? WallID.None;
            bool hasWall = wallType != WallID.None;
            if (tw is not null && !hasWall && (tw.WallFrameX != 0 || tw.WallFrameY != 0 || tw.WallFrameNumber != 0))
                hasWall = true;

            ushort tileType = tileTypeData?.Type ?? 0;
            bool hasTile = tw?.HasTile == true || tileType != 0;

            bool hasLiquid = liquid is { Amount: > 0 };

            bool hasPaint = false;
            if (tw != null && (tw.TileColor != 0 || tw.WallColor != 0))
                hasPaint = true;
            else if (bright is not null && (bright.IsTileInvisible || bright.IsWallInvisible || bright.IsTileFullbright || bright.IsWallFullbright))
                hasPaint = true;

            bool hasWiring = tw != null && (tw.RedWire || tw.BlueWire || tw.GreenWire || tw.YellowWire || tw.HasActuator);

            bool hasDetail = tw != null && (tw.IsActuated || tw.IsHalfBlock || tw.Slope != 0 || tw.BlockType != 0 || tw.NonFrameBits != 0);

            if (!hasWall && !hasTile && !hasLiquid && !hasPaint && !hasWiring && !hasDetail)
                return null;

            var facets = new PayloadFacets();

            if (hasWall && wallType != WallID.None)
            {
                facets.Wall = new WallFacetDto
                {
                    Type = wallType,
                    Color = tw?.WallColor ?? 0,
                    FrameX = tw?.WallFrameX ?? 0,
                    FrameY = tw?.WallFrameY ?? 0,
                    FrameNumber = tw?.WallFrameNumber ?? 0,
                };
            }

            if (hasTile)
            {
                facets.Block = new BlockFacetDto
                {
                    HasTile = tw?.HasTile ?? (tileType != 0),
                    TileType = tileType,
                    FrameX = tw?.TileFrameX ?? 0,
                    FrameY = tw?.TileFrameY ?? 0,
                    FrameNumber = tw?.TileFrameNumber ?? 0,
                };
                if (tw is not null && !tw.HasTile)
                    facets.Block.HasTile = false;
            }

            if (hasLiquid && liquid is not null)
            {
                facets.Liquid = new LiquidFacetDto
                {
                    Amount = liquid.Amount,
                    LiquidType = liquid.LiquidType,
                    SkipLiquid = liquid.SkipLiquid,
                    CheckingLiquid = liquid.CheckingLiquid,
                };
            }

            if (hasPaint && (tw is not null || bright is not null))
            {
                facets.Paint = new PaintFacetDto
                {
                    TileColor = tw?.TileColor ?? 0,
                    WallColor = tw?.WallColor ?? 0,
                    IsTileInvisible = bright?.IsTileInvisible ?? false,
                    IsWallInvisible = bright?.IsWallInvisible ?? false,
                    IsTileFullbright = bright?.IsTileFullbright ?? false,
                    IsWallFullbright = bright?.IsWallFullbright ?? false,
                };
            }

            if (hasWiring && tw is not null)
            {
                facets.Wiring = new WiringFacetDto
                {
                    RedWire = tw.RedWire,
                    BlueWire = tw.BlueWire,
                    GreenWire = tw.GreenWire,
                    YellowWire = tw.YellowWire,
                    HasActuator = tw.HasActuator,
                    WireData = tw.WireData,
                };
            }

            if (hasDetail && tw is not null)
            {
                facets.Detail = new DetailFacetDto
                {
                    IsActuated = tw.IsActuated,
                    BlockType = tw.BlockType,
                    Slope = tw.Slope,
                    IsHalfBlock = tw.IsHalfBlock,
                    NonFrameBits = tw.NonFrameBits,
                };
            }

            return facets;
        }
    }
}
