using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.UI.Preview.Layers;

namespace TheArchitectsBlueprintMod.UI.Preview
{
    /// <summary>Runs preview layers in draw order over a payload region.</summary>
    internal static class PreviewLayerSystem
    {
        private static readonly PreviewLayer[] DrawOrder =
        [
            WallPreviewLayer.Instance,
            FluidPreviewLayer.Instance,
            BlockPreviewLayer.Instance,
            VegetationPreviewLayer.Instance,
            WiringPreviewLayer.Instance,
            DetailPreviewLayer.Instance,
        ];

        public static void DrawRegion(
            SpriteBatch sb,
            IReadOnlyList<Payload.Entry> entries,
            int minX,
            int maxX,
            int minY,
            int maxY,
            Vector2 position,
            float scale,
            Color tint,
            Func<Point, bool>? includeCell = null,
            int? worldOriginX = null,
            int? worldOriginY = null)
        {
            var cellLookup = new Dictionary<Point, PayloadCell>(entries.Count);
            foreach (Payload.Entry entry in entries)
            {
                if (!entry.Cell.IsEmpty)
                    cellLookup[entry.Relative] = entry.Cell;
            }

            var ctx = new PreviewDrawContext
            {
                SpriteBatch = sb,
                Position = position,
                Scale = scale,
                Tint = tint,
                CellLookup = cellLookup,
                WorldOriginX = worldOriginX,
                WorldOriginY = worldOriginY,
            };

            for (int i = 0; i < DrawOrder.Length; i++)
            {
                PreviewLayer layer = DrawOrder[i];
                ForEachVisibleCell(entries, minX, maxX, minY, maxY, includeCell, (cell, relX, relY) =>
                    layer.Draw(in ctx, cell, relX, relY));
            }
        }

        private static void ForEachVisibleCell(
            IReadOnlyList<Payload.Entry> entries,
            int minX,
            int maxX,
            int minY,
            int maxY,
            Func<Point, bool>? includeCell,
            Action<PayloadCell, int, int> draw)
        {
            foreach (Payload.Entry entry in entries)
            {
                Point rel = entry.Relative;
                if (rel.X < minX || rel.X >= maxX || rel.Y < minY || rel.Y >= maxY)
                    continue;
                if (includeCell is not null && !includeCell(rel))
                    continue;

                PayloadCell cell = entry.Cell;
                if (cell.IsEmpty)
                    continue;

                draw(cell, rel.X, rel.Y);
            }
        }
    }
}
