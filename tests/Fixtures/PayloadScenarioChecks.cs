#if DEBUG
#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Tests;

namespace TheArchitectsBlueprintMod.Tests.Fixtures
{
    /// <summary>Payload and world-tile assertions used by test scenarios (not fixture setup).</summary>
    internal static class PayloadScenarioChecks
    {
        internal static bool AllCellsMatchTileType(Payload payload, int tileType, out string? error)
        {
            error = null;
            int expected = payload.Width * payload.Height;
            int matched = 0;
            for (int y = 0; y < payload.Height; y++)
            {
                for (int x = 0; x < payload.Width; x++)
                {
                    if (!payload.TryGetCell(new Point(x, y), out PayloadCell cell))
                    {
                        error = $"Missing payload cell ({x},{y}).";
                        return false;
                    }

                    if (!cell.HasTile() || cell.Block.Type != tileType)
                    {
                        error = $"Cell ({x},{y}) expected tile type {tileType}, got HasTile={cell.HasTile()} type={cell.Block.Type}.";
                        return false;
                    }

                    matched++;
                }
            }

            if (matched != expected)
            {
                error = $"Matched {matched} cells, expected {expected}.";
                return false;
            }

            return true;
        }

        internal static int CountCellsWithTile(Payload payload)
        {
            int count = 0;
            foreach (PayloadCell cell in payload.Cells)
            {
                if (cell.HasTile())
                    count++;
            }

            return count;
        }

        internal static bool HasAnyTileTypeOtherThan(Payload payload, int excludedType, out int foundType)
        {
            foundType = 0;
            foreach (PayloadCell cell in payload.Cells)
            {
                if (!cell.HasTile() || cell.Block.Type == excludedType)
                    continue;
                foundType = cell.Block.Type;
                return true;
            }

            return false;
        }

        internal static int CountDistinctTileTypes(Payload payload)
        {
            var types = new HashSet<ushort>();
            foreach (PayloadCell cell in payload.Cells)
            {
                if (cell.HasTile())
                    types.Add(cell.Block.Type);
            }

            return types.Count;
        }

        internal static int CountCellsWithWall(Payload payload)
        {
            int count = 0;
            foreach (PayloadCell cell in payload.Cells)
            {
                if (cell.Wall.Type != WallID.None)
                    count++;
            }

            return count;
        }

        internal static int CountCellsWithTileType(Payload payload, ushort tileType)
        {
            int count = 0;
            foreach (PayloadCell cell in payload.Cells)
            {
                if (cell.HasTile() && cell.Block.Type == tileType)
                    count++;
            }

            return count;
        }

        internal static bool TargetWorldMatchesPayload(Projection projection, out string? error) =>
            WorldAreaMatchesPayload(projection.TargetArea, projection.Payload, out error);

        internal static bool WorldAreaMatchesPayload(Rectangle worldArea, Payload payload, out string? error)
        {
            error = null;

            for (int y = 0; y < payload.Height; y++)
            {
                for (int x = 0; x < payload.Width; x++)
                {
                    var rel = new Point(x, y);
                    if (!payload.TryGetCell(rel, out PayloadCell cell))
                        continue;

                    int wx = worldArea.X + x;
                    int wy = worldArea.Y + y;
                    if (!WorldGen.InWorld(wx, wy))
                    {
                        error = $"Target ({wx},{wy}) is outside the world.";
                        return false;
                    }

                    Tile tile = Main.tile[wx, wy];
                    bool worldHasTile = tile.HasTile;
                    if (cell.HasTile())
                    {
                        if (!worldHasTile || tile.TileType != cell.Block.Type)
                        {
                            error =
                                $"Target ({wx},{wy}) expected tile {cell.Block.Type}, got HasTile={worldHasTile} type={(worldHasTile ? tile.TileType : -1)}.";
                            return false;
                        }
                    }
                    else if (worldHasTile)
                    {
                        error = $"Target ({wx},{wy}) expected empty, found tile type {tile.TileType}.";
                        return false;
                    }

                    ushort expectedWall = cell.Wall.Type;
                    if (expectedWall != WallID.None && tile.WallType != expectedWall)
                    {
                        error =
                            $"Target ({wx},{wy}) expected wall {expectedWall}, got {tile.WallType}.";
                        return false;
                    }
                }
            }

            return true;
        }

        internal static bool PayloadMatchesSize(Payload payload, int width, int height, out string? error)
        {
            error = null;
            if (payload.Width != width || payload.Height != height)
            {
                error = $"Expected payload {width}x{height}, got {payload.Width}x{payload.Height}.";
                return false;
            }

            return true;
        }

        /// <summary>Reports tile/wall/type counts from the payload grid (no sample-specific expectations).</summary>
        internal static bool VerifyPayloadContent(Payload payload, ITestReporter reporter, out string? error)
        {
            error = null;
            if (!payload.IsDefined)
            {
                error = "Payload is not defined.";
                return false;
            }

            int filled = CountCellsWithTile(payload);
            int volume = payload.Width * payload.Height;
            if (filled <= 0)
            {
                error = "Payload has no tile cells.";
                return false;
            }

            int walls = CountCellsWithWall(payload);
            int distinct = CountDistinctTileTypes(payload);
            reporter.Pass(
                $"content {payload.Width}×{payload.Height}: {filled}/{volume} tiles, {walls} wall(s), {distinct} tile type(s)");
            return true;
        }

        internal static bool VerifyImportedProjection(Payload source, Projection projection, ITestReporter reporter, out string? error)
        {
            if (!PayloadMatchesSize(projection.Payload, source.Width, source.Height, out error))
                return false;

            reporter.Pass($"projection size {source.Width}×{source.Height}");
            return VerifyPayloadContent(projection.Payload, reporter, out error);
        }

        /// <summary>Whether center-cell IO mutate applies to this payload (3×3+ grid; fill needs a corner tile).</summary>
        internal static bool SupportsCenterIoMutate(Payload payload, bool forFill, out string? skipReason)
        {
            skipReason = null;
            if (payload.Width < 3 || payload.Height < 3)
            {
                skipReason = $"payload is {payload.Width}×{payload.Height} (need ≥3×3 for center cell).";
                return false;
            }

            if (forFill && (!payload.TryGetCell(Point.Zero, out PayloadCell corner) || !corner.HasTile()))
            {
                skipReason = "fill needs a tile at payload cell (0,0).";
                return false;
            }

            return true;
        }

        internal static bool CutSourceEquals(Projection projection, bool expected, out string? error)
        {
            error = null;
            if (projection.CutSource != expected)
            {
                error = $"Expected CutSource={expected}, got {projection.CutSource}.";
                return false;
            }

            return true;
        }

        internal static bool SourceAreaMatches(Projection projection, Rectangle expected, out string? error)
        {
            error = null;
            if (projection.SourceArea != expected)
            {
                error = $"Expected source {expected}, got {projection.SourceArea}.";
                return false;
            }

            return true;
        }

        internal static bool WorldAreaHasNoTiles(Rectangle tileArea, out string? error)
        {
            error = null;
            for (int y = tileArea.Y; y < tileArea.Bottom; y++)
            {
                for (int x = tileArea.X; x < tileArea.Right; x++)
                {
                    if (!WorldGen.InWorld(x, y))
                        continue;
                    if (Main.tile[x, y].HasTile)
                    {
                        error = $"Expected no tile at ({x},{y}), found type {Main.tile[x, y].TileType}.";
                        return false;
                    }
                }
            }

            return true;
        }

        internal static bool WorldAreaAllTileType(Rectangle tileArea, int tileType, out string? error)
        {
            error = null;
            for (int y = tileArea.Y; y < tileArea.Bottom; y++)
            {
                for (int x = tileArea.X; x < tileArea.Right; x++)
                {
                    if (!WorldGen.InWorld(x, y))
                        continue;
                    Tile tile = Main.tile[x, y];
                    if (!tile.HasTile || tile.TileType != tileType)
                    {
                        error =
                            $"Expected tile {tileType} at ({x},{y}), got HasTile={tile.HasTile} type={(tile.HasTile ? tile.TileType : -1)}.";
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
#endif
