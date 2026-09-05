using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core.Layers;

namespace TheArchitectsBlueprintMod.Core
{
    internal enum PayloadPrepareMode
    {
        ValidateOnly,
        SkipIncompatible,
    }

    internal readonly struct PayloadCompatibilityReport(
        int totalNonEmptyCells,
        int incompatibleCells,
        IReadOnlyList<string> missingModsFromMetadata)
    {
        public int TotalNonEmptyCells { get; } = totalNonEmptyCells;
        public int IncompatibleCells { get; } = incompatibleCells;
        public IReadOnlyList<string> MissingModsFromMetadata { get; } = missingModsFromMetadata;
    }

    /// <summary>Validates and sanitizes payloads before projection assignment.</summary>
    internal static class PayloadCompatibility
    {
        public static bool IsTileResolvable(ushort type)
        {
            if (type == 0)
                return true;
            if (type >= TileLoader.TileCount)
                return false;
            if (type < TileID.Count)
                return true;
            return TileLoader.GetTile(type) is not null;
        }

        public static bool IsWallResolvable(ushort type)
        {
            if (type == 0)
                return true;
            if (type >= WallLoader.WallCount)
                return false;
            if (type < WallID.Count)
                return true;
            return WallLoader.GetWall(type) is not null;
        }

        public static bool IsCellCompatible(PayloadCell cell)
        {
            if (cell.IsEmpty)
                return true;

            if (cell.Detail.HasTile && cell.Block.Type > 0 && !IsTileResolvable(cell.Block.Type))
                return false;

            if (cell.Wall.Type > 0 && !IsWallResolvable(cell.Wall.Type))
                return false;

            return true;
        }

        public static Payload Prepare(
            Payload source,
            PayloadPrepareMode mode,
            IReadOnlyList<string>? metadataModsLoaded,
            out PayloadCompatibilityReport report)
        {
            ArgumentNullException.ThrowIfNull(source);
            var missingMods = ComputeMissingMods(metadataModsLoaded);
            if (!source.IsDefined || mode == PayloadPrepareMode.ValidateOnly)
            {
                int total = CountNonEmpty(source);
                int incompatible = CountIncompatible(source);
                report = new PayloadCompatibilityReport(total, incompatible, missingMods);
                return source;
            }

            var entries = new List<Payload.Entry>();
            int totalCells = 0;
            int incompatibleCells = 0;
            IReadOnlyList<Payload.Entry> sourceEntries = source.Entries;

            for (int i = 0; i < sourceEntries.Count; i++)
            {
                Payload.Entry sourceEntry = sourceEntries[i];
                PayloadCell cell = sourceEntry.Cell;
                if (cell.IsEmpty)
                    continue;

                totalCells++;
                Point rel = sourceEntry.Relative;
                if (IsCellCompatible(cell))
                {
                    entries.Add(new Payload.Entry(rel, cell));
                    continue;
                }

                // Count as incompatible for the orange warning, but keep Unloaded placeholders.
                incompatibleCells++;
                if (TrySanitizeCell(cell, out PayloadCell sanitized) && !sanitized.IsEmpty)
                    entries.Add(new Payload.Entry(rel, sanitized));
            }

            report = new PayloadCompatibilityReport(totalCells, incompatibleCells, missingMods);
            return Payload.FromSparse(entries, source.Width, source.Height, source.Entities);
        }

        internal static IReadOnlyList<string> ComputeMissingMods(IReadOnlyList<string>? metadataModsLoaded)
        {
            if (metadataModsLoaded is null || metadataModsLoaded.Count == 0)
                return [];

            var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                for (int i = 0; i < ModLoader.Mods.Length; i++)
                {
                    Mod? m = ModLoader.Mods[i];
                    if (m is not null && !string.IsNullOrEmpty(m.Name))
                        current.Add(m.Name);
                }
            }
            catch
            {
                return [];
            }

            return metadataModsLoaded.Where(m => !current.Contains(m)).ToList();
        }

        private static int CountNonEmpty(Payload payload) => payload.Entries.Count;

        private static int CountIncompatible(Payload payload)
        {
            int count = 0;
            IReadOnlyList<Payload.Entry> entries = payload.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                PayloadCell cell = entries[i].Cell;
                if (!cell.IsEmpty && !IsCellCompatible(cell))
                    count++;
            }

            return count;
        }

        /// <summary>Remaps unresolvable tile/wall ids to tML Unloaded placeholders instead of clearing them.</summary>
        private static bool TrySanitizeCell(PayloadCell cell, out PayloadCell sanitized)
        {
            // --- Clone and replace missing mod content with UnloadedSolidTile / UnloadedWall.
            sanitized = default;
            if (cell.IsEmpty || cell.Data is null)
                return false;

            PayloadCell.TileData source = cell.Data;
            var data = new PayloadCell.TileData
            {
                TileType = source.TileType,
                WallType = source.WallType,
                LiquidData = source.LiquidData,
                TWBIData = source.TWBIData,
                TWWStateData = source.TWWStateData,
                TileStyle = source.TileStyle,
                TileAlternate = source.TileAlternate,
            };
            bool changed = false;

            if (data.TWWStateData.HasTile && data.TileType.Type > 0 && !IsTileResolvable(data.TileType.Type))
            {
                data.TileType.Type = TileLoaderSupport.ResolveUnloadableTileType(data.TileType.Type);
                changed = true;
            }

            if (data.WallType.Type > 0 && !IsWallResolvable(data.WallType.Type))
            {
                data.WallType.Type = TileLoaderSupport.ResolveUnloadableWallType(data.WallType.Type);
                changed = true;
            }

            if (!changed && IsCellCompatible(cell))
            {
                sanitized = cell;
                return true;
            }

            sanitized = PayloadCell.FromData(data);
            return !sanitized.IsEmpty;
        }
    }
}
