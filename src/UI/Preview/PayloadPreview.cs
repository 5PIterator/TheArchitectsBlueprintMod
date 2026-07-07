using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.UI.Preview
{
    /// <summary>
    /// Entry point for payload previews (in-world projection overlay and UI thumbnails).
    /// Rendering is delegated to <see cref="PreviewLayerSystem"/>.
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

            PreviewLayerSystem.DrawRegion(
                Main.spriteBatch,
                payload.Entries,
                minX,
                maxX,
                minY,
                maxY,
                origin,
                1f,
                PreviewTint,
                IncludeCell);
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

            PreviewLayerSystem.DrawRegion(
                Main.spriteBatch,
                payload.Entries,
                0,
                w,
                0,
                h,
                offset,
                scale,
                PreviewTint);

            Main.spriteBatch.End();
            Main.instance.GraphicsDevice.SetRenderTarget(null);

            var mergedTexture = new Texture2D(Main.graphics.GraphicsDevice, size, size);
            var content = new Color[size * size];
            renderTarget.GetData(content);
            mergedTexture.SetData(content);
            renderTarget.Dispose();
            return mergedTexture;
        }

        private static void ComputeThumbnailFit(int actualPixelWidth, int actualPixelHeight, int desiredSize, out float scale, out Vector2 offset)
        {
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

        private static bool TryGetIncludedCell(Projection proj, Payload payload, Point relative, out PayloadCell cell)
        {
            if (!proj.TryGetTileByRelative(relative, out Blueprint.BlueprintCell tile) || !tile.Included
                || !payload.TryGetCell(relative, out cell) || cell.IsEmpty)
            {
                cell = default;
                return false;
            }

            return true;
        }

        private static void GetVisibleRelativeRange(Rectangle area, int width, int height, out int minX, out int maxX, out int minY, out int maxY)
        {
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
    }
}
