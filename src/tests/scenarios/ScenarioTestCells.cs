#if DEBUG
#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Shared payload cell builders for scenario fixtures.</summary>
    internal static class ScenarioTestCells
    {
        /// <summary>Builds a stone cell via TagIO tile name Tile_1.</summary>
        public static bool TryCreateStone(out PayloadCell stone, out string? error)
        {
            // --- Parse the canonical stone tag body used across payload scenarios.
            stone = default;
            error = null;
            if (PayloadTagIO.TryCellFromTag(new TagCompound { ["tile"] = "Tile_1", ["style"] = 0 }, out stone))
                return true;

            error = "Failed to build stone test cell.";
            return false;
        }

        /// <summary>Builds a one-cell stone payload at (0,0).</summary>
        public static bool TryCreateStonePayload(out Payload payload, out PayloadCell stone, out string? error)
        {
            // --- Wrap TryCreateStone in a 1x1 sparse document.
            payload = Payload.Empty;
            if (!TryCreateStone(out stone, out error))
                return false;

            payload = Payload.FromSparse([new Payload.Entry(new Point(0, 0), stone)], width: 1, height: 1);
            return true;
        }

        /// <summary>Builds a cell whose tile type is outside TileLoader.TileCount (unresolvable).</summary>
        public static PayloadCell CreateUnresolvableTileCell()
        {
            // --- Force HasTile with a type past the loaded tile table.
            ushort badType = (ushort)Math.Min(ushort.MaxValue, TileLoader.TileCount + 100);
            if (badType < TileLoader.TileCount)
                badType = ushort.MaxValue;

            var data = new PayloadCell.TileData
            {
                TileType = default,
                WallType = default,
                LiquidData = default,
                TWBIData = default,
                TWWStateData = default,
                TileStyle = 0,
                TileAlternate = 0,
            };
            data.TileType.Type = badType;
            data.TWWStateData.HasTile = true;
            return PayloadCell.FromData(data);
        }
    }
}
#endif
