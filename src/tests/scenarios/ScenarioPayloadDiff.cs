#if DEBUG
#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    internal readonly record struct ScenarioPayloadDiffItem(
        Point Relative,
        Point World,
        string Facet,
        string Expected,
        string Actual);

    internal readonly record struct ScenarioPayloadDiffResult(
        int ComparedCells,
        int MissingCells,
        int UnexpectedCells,
        IReadOnlyList<ScenarioPayloadDiffItem> Mismatches)
    {
        public int MismatchCount => Mismatches.Count + MissingCells + UnexpectedCells;
    }

    /// <summary>Strict payload comparer for scenario diagnostics.</summary>
    internal static class ScenarioPayloadDiff
    {
        public static ScenarioPayloadDiffResult Compare(Payload expected, Payload actual, Rectangle targetArea)
        {
            var mismatches = new List<ScenarioPayloadDiffItem>();
            int compared = 0;
            int missing = 0;
            int unexpected = 0;
            var seen = new HashSet<Point>();

            IReadOnlyList<Payload.Entry> expectedEntries = expected.Entries;
            for (int i = 0; i < expectedEntries.Count; i++)
            {
                Payload.Entry entry = expectedEntries[i];
                Point rel = entry.Relative;
                seen.Add(rel);
                compared++;

                if (!actual.TryGetCell(rel, out PayloadCell actualCell))
                {
                    missing++;
                    continue;
                }

                CompareCells(rel, targetArea, entry.Cell, actualCell, mismatches);
            }

            IReadOnlyList<Payload.Entry> actualEntries = actual.Entries;
            for (int i = 0; i < actualEntries.Count; i++)
            {
                Payload.Entry entry = actualEntries[i];
                Point rel = entry.Relative;
                if (seen.Contains(rel))
                    continue;
                unexpected++;
            }

            return new ScenarioPayloadDiffResult(compared, missing, unexpected, mismatches);
        }

        private static void CompareCells(
            Point rel,
            Rectangle targetArea,
            PayloadCell expected,
            PayloadCell actual,
            ICollection<ScenarioPayloadDiffItem> mismatches)
        {
            Point world = new(targetArea.X + rel.X, targetArea.Y + rel.Y);

            AddIfDifferent(rel, world, "Detail.HasTile", expected.Detail.HasTile, actual.Detail.HasTile, mismatches);
            AddIfDifferent(rel, world, "Block.Type", expected.Block.Type, actual.Block.Type, mismatches);
            AddIfDifferent(rel, world, "Block.FrameX", expected.Block.FrameX, actual.Block.FrameX, mismatches);
            AddIfDifferent(rel, world, "Block.FrameY", expected.Block.FrameY, actual.Block.FrameY, mismatches);
            AddIfDifferent(rel, world, "Block.FrameNumber", expected.Block.FrameNumber, actual.Block.FrameNumber, mismatches);
            AddIfDifferent(rel, world, "Detail.Style", expected.Detail.Style, actual.Detail.Style, mismatches);
            AddIfDifferent(rel, world, "Detail.Alternate", expected.Detail.Alternate, actual.Detail.Alternate, mismatches);
            AddIfDifferent(rel, world, "Detail.Slope", (int)expected.Detail.Slope, (int)actual.Detail.Slope, mismatches);
            AddIfDifferent(rel, world, "Detail.BlockType", (int)expected.Detail.BlockType, (int)actual.Detail.BlockType, mismatches);
            AddIfDifferent(rel, world, "Detail.IsHalfBlock", expected.Detail.IsHalfBlock, actual.Detail.IsHalfBlock, mismatches);
            AddIfDifferent(rel, world, "Detail.IsActuated", expected.Detail.IsActuated, actual.Detail.IsActuated, mismatches);
            AddIfDifferent(rel, world, "Detail.NonFrameBits", expected.Detail.NonFrameBits, actual.Detail.NonFrameBits, mismatches);

            AddIfDifferent(rel, world, "Wall.Type", expected.Wall.Type, actual.Wall.Type, mismatches);
            AddIfDifferent(rel, world, "Wall.FrameX", expected.Wall.FrameX, actual.Wall.FrameX, mismatches);
            AddIfDifferent(rel, world, "Wall.FrameY", expected.Wall.FrameY, actual.Wall.FrameY, mismatches);
            AddIfDifferent(rel, world, "Wall.FrameNumber", expected.Wall.FrameNumber, actual.Wall.FrameNumber, mismatches);

            AddIfDifferent(rel, world, "Liquid.Type", expected.Liquid.Type, actual.Liquid.Type, mismatches);
            AddIfDifferent(rel, world, "Liquid.Amount", expected.Liquid.Amount, actual.Liquid.Amount, mismatches);
            AddIfDifferent(rel, world, "Liquid.SkipLiquid", expected.Liquid.SkipLiquid, actual.Liquid.SkipLiquid, mismatches);
            AddIfDifferent(rel, world, "Liquid.CheckingLiquid", expected.Liquid.CheckingLiquid, actual.Liquid.CheckingLiquid, mismatches);

            AddIfDifferent(rel, world, "Wire.Data", expected.Wire.Data, actual.Wire.Data, mismatches);
            AddIfDifferent(rel, world, "Wire.HasActuator", expected.Wire.HasActuator, actual.Wire.HasActuator, mismatches);
            AddIfDifferent(rel, world, "Wire.RedWire", expected.Wire.RedWire, actual.Wire.RedWire, mismatches);
            AddIfDifferent(rel, world, "Wire.BlueWire", expected.Wire.BlueWire, actual.Wire.BlueWire, mismatches);
            AddIfDifferent(rel, world, "Wire.GreenWire", expected.Wire.GreenWire, actual.Wire.GreenWire, mismatches);
            AddIfDifferent(rel, world, "Wire.YellowWire", expected.Wire.YellowWire, actual.Wire.YellowWire, mismatches);

            AddIfDifferent(rel, world, "Paint.TileColor", expected.Paint.TileColor, actual.Paint.TileColor, mismatches);
            AddIfDifferent(rel, world, "Paint.WallColor", expected.Paint.WallColor, actual.Paint.WallColor, mismatches);
            AddIfDifferent(rel, world, "Paint.TileInvisible", expected.Paint.TileInvisible, actual.Paint.TileInvisible, mismatches);
            AddIfDifferent(rel, world, "Paint.WallInvisible", expected.Paint.WallInvisible, actual.Paint.WallInvisible, mismatches);
            AddIfDifferent(rel, world, "Paint.TileFullbright", expected.Paint.TileFullbright, actual.Paint.TileFullbright, mismatches);
            AddIfDifferent(rel, world, "Paint.WallFullbright", expected.Paint.WallFullbright, actual.Paint.WallFullbright, mismatches);
        }

        private static void AddIfDifferent<T>(
            Point rel,
            Point world,
            string facet,
            T expected,
            T actual,
            ICollection<ScenarioPayloadDiffItem> mismatches)
        {
            if (EqualityComparer<T>.Default.Equals(expected, actual))
                return;

            mismatches.Add(new ScenarioPayloadDiffItem(
                rel,
                world,
                facet,
                expected?.ToString() ?? "null",
                actual?.ToString() ?? "null"));
        }
    }
}
#endif
