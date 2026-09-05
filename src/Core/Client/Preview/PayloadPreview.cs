using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.Map;
using Terraria.UI;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.Layers;
using TheArchitectsBlueprintMod.Utils;

namespace TheArchitectsBlueprintMod.Core.Preview
{
    /// <summary>
    /// Entry point for payload previews (in-world projection overlay, map pixels, and UI thumbnails).
    /// World rendering is delegated to <see cref="LayerSystem.DrawRegion"/>.
    /// </summary>
    internal static class PayloadPreview
    {
        private const int TilePixelSize = 16;
        private static readonly Color PreviewTint = new(255, 255, 255, 160);

        public static void Draw(Projection proj)
        {
            if (proj.IsLocked || !proj.HasPayload || !proj.IsDefined)
                return;

            Payload payload = proj.Payload;
            int w = payload.Width;
            int h = payload.Height;
            if (w <= 0 || h <= 0)
                return;

            GetVisibleRelativeRange(proj.Area, w, h, out int minX, out int maxX, out int minY, out int maxY);
            if (minX >= maxX || minY >= maxY)
                return;

            Vector2 origin = new Vector2(proj.Area.X * TilePixelSize, proj.Area.Y * TilePixelSize) - Main.screenPosition;
            bool IncludeCell(Point p) => TryGetIncludedCell(proj, payload, p, out _);

            LayerSystem.DrawRegion(
                Main.spriteBatch,
                payload.Entries,
                minX,
                maxX,
                minY,
                maxY,
                origin,
                1f,
                PreviewTint,
                IncludeCell,
                proj.Area.X,
                proj.Area.Y,
                payload.Entities);
        }

        /// <summary>
        /// Draws included payload cells as vanilla map-colored pixels on the fullscreen map.
        /// </summary>
        public static void DrawOnMap(ref MapOverlayDrawContext context, Projection proj)
        {
            if (proj.IsLocked || !proj.HasPayload || !proj.IsDefined)
                return;

            Payload payload = proj.Payload;
            int w = payload.Width;
            int h = payload.Height;
            if (w <= 0 || h <= 0)
                return;

            GetVisibleRelativeRangeOnMap(proj.Area, w, h, out int minX, out int maxX, out int minY, out int maxY);
            if (minX >= maxX || minY >= maxY)
                return;

            float tileDrawScale = context.DrawScale > 0f
                ? context.MapScale / context.DrawScale
                : context.MapScale;
            if (tileDrawScale <= 0f)
                return;

            var pixel = ModUtils.DummyTexture;
            var frame = new SpriteFrame(1, 1, 0, 0);
            IReadOnlyList<Payload.Entry> entries = payload.Entries;

            for (int i = 0; i < entries.Count; i++)
            {
                Point rel = entries[i].Relative;
                if (rel.X < minX || rel.X >= maxX || rel.Y < minY || rel.Y >= maxY)
                    continue;

                if (!TryGetIncludedCell(proj, payload, rel, out PayloadCell cell))
                    continue;

                if (!PayloadMapColor.TryGetMapColor(cell, out Color mapColor))
                    continue;

                float tintAlpha = PreviewTint.A / 255f;
                Color drawColor = mapColor * tintAlpha;
                drawColor.A = (byte)(mapColor.A * tintAlpha);
                context.Draw(
                    pixel,
                    new Vector2(proj.Area.X + rel.X, proj.Area.Y + rel.Y),
                    drawColor,
                    frame,
                    tileDrawScale,
                    tileDrawScale,
                    Alignment.TopLeft);
            }
        }

        /// <summary>Bakes a square preview texture for UI thumbnails. Caller must Dispose the result.</summary>
        public static Texture2D? MakeThumbnail(Payload payload, int size = 100)
        {
            if (!payload.IsDefined)
                return null;

            int w = payload.Width;
            int h = payload.Height;
            if (w <= 0 || h <= 0)
                return null;

            ComputeThumbnailFit(w * TilePixelSize, h * TilePixelSize, size, out float scale, out Vector2 offset);

            var renderTarget = new RenderTarget2D(Main.graphics.GraphicsDevice, size, size);
            Main.instance.GraphicsDevice.SetRenderTarget(renderTarget);
            Main.instance.GraphicsDevice.Clear(Color.Transparent);
            Main.spriteBatch.Begin();

            LayerSystem.DrawRegion(
                Main.spriteBatch,
                payload.Entries,
                0,
                w,
                0,
                h,
                offset,
                scale,
                PreviewTint,
                entities: payload.Entities);

            Main.spriteBatch.End();
            Main.instance.GraphicsDevice.SetRenderTarget(null);

            var mergedTexture = new Texture2D(Main.graphics.GraphicsDevice, size, size);
            var content = new Color[size * size];
            renderTarget.GetData(content);
            mergedTexture.SetData(content);
            renderTarget.Dispose();
            return mergedTexture;
        }

        /// <summary>Fits a payload pixel size into a square thumbnail, centering the unused axis.</summary>
        private static void ComputeThumbnailFit(int actualPixelWidth, int actualPixelHeight, int desiredSize, out float scale, out Vector2 offset)
        {
            // --- Leave 1:1 when it fits; otherwise scale by the longer side and center the remainder.

            scale = 1f;
            offset = Vector2.Zero;

            if (actualPixelWidth <= desiredSize && actualPixelHeight <= desiredSize)
                return;

            if (actualPixelHeight > actualPixelWidth)
            {
                scale = (float)desiredSize / actualPixelHeight;
                offset.X = (desiredSize - actualPixelWidth * scale) / 2f;
            }
            else
            {
                scale = (float)desiredSize / actualPixelWidth;
                offset.Y = (desiredSize - actualPixelHeight * scale) / 2f;
            }

            offset /= scale;
        }

        /// <summary>Returns the payload cell when the projection includes that relative coordinate.</summary>
        private static bool TryGetIncludedCell(Projection proj, Payload payload, Point relative, out PayloadCell cell)
        {
            // --- Require an included blueprint cell and a non-empty payload cell at the same relative.

            if (!proj.TryGetTileByRelative(relative, out Blueprint.BlueprintCell tile) || !tile.Included
                || !payload.TryGetCell(relative, out cell) || cell.IsEmpty)
            {
                cell = default;
                return false;
            }

            return true;
        }

        /// <summary>Clips payload-relative draw bounds to the on-screen world tiles plus a small margin.</summary>
        private static void GetVisibleRelativeRange(Rectangle area, int width, int height, out int minX, out int maxX, out int minY, out int maxY)
        {
            // --- Convert screen pixels to world tiles, intersect with the projection area, then shift to relative.

            const int margin = 2;
            int leftTile = (int)(Main.screenPosition.X / TilePixelSize) - margin;
            int rightTile = (int)((Main.screenPosition.X + Main.screenWidth) / TilePixelSize) + margin;
            int topTile = (int)(Main.screenPosition.Y / TilePixelSize) - margin;
            int bottomTile = (int)((Main.screenPosition.Y + Main.screenHeight) / TilePixelSize) + margin;

            int minWorldX = Math.Max(area.Left, leftTile);
            int maxWorldX = Math.Min(area.Right - 1, rightTile);
            int minWorldY = Math.Max(area.Top, topTile);
            int maxWorldY = Math.Min(area.Bottom - 1, bottomTile);

            if (minWorldX > maxWorldX || minWorldY > maxWorldY)
            {
                minX = maxX = minY = maxY = 0;
                return;
            }

            minX = Math.Clamp(minWorldX - area.X, 0, width);
            maxX = Math.Clamp(maxWorldX - area.X + 1, 0, width);
            minY = Math.Clamp(minWorldY - area.Y, 0, height);
            maxY = Math.Clamp(maxWorldY - area.Y + 1, 0, height);
        }

        /// <summary>Clips payload-relative draw bounds to visible fullscreen-map tiles plus a margin.</summary>
        private static void GetVisibleRelativeRangeOnMap(Rectangle area, int width, int height, out int minX, out int maxX, out int minY, out int maxY)
        {
            const int margin = 2;

            if (!ArchitectMapTransform.TryScreenToTile(Vector2.Zero, out Point topLeft)
                || !ArchitectMapTransform.TryScreenToTile(new Vector2(Main.screenWidth, Main.screenHeight), out Point bottomRight))
            {
                minX = maxX = minY = maxY = 0;
                return;
            }

            int leftTile = Math.Min(topLeft.X, bottomRight.X) - margin;
            int rightTile = Math.Max(topLeft.X, bottomRight.X) + margin;
            int topTile = Math.Min(topLeft.Y, bottomRight.Y) - margin;
            int bottomTile = Math.Max(topLeft.Y, bottomRight.Y) + margin;

            int minWorldX = Math.Max(area.Left, leftTile);
            int maxWorldX = Math.Min(area.Right - 1, rightTile);
            int minWorldY = Math.Max(area.Top, topTile);
            int maxWorldY = Math.Min(area.Bottom - 1, bottomTile);

            if (minWorldX > maxWorldX || minWorldY > maxWorldY)
            {
                minX = maxX = minY = maxY = 0;
                return;
            }

            minX = Math.Clamp(minWorldX - area.X, 0, width);
            maxX = Math.Clamp(maxWorldX - area.X + 1, 0, width);
            minY = Math.Clamp(minWorldY - area.Y, 0, height);
            maxY = Math.Clamp(maxWorldY - area.Y + 1, 0, height);
        }
    }
}
