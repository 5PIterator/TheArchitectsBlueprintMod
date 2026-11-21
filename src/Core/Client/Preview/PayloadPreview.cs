using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.UI;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.Architect;
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

        private sealed class MapPreviewEntry
        {
            public Texture2D Texture = null!;
            public int InclusionVersion;
            public int Width;
            public int Height;
            public int EntryCount;
        }

        private static readonly Dictionary<int, MapPreviewEntry> MapTextures = new();

        public static void Draw(Projection proj)
        {
            // --- Clip to screen (and optional nearby radius), then cached blit or live DrawRegion.

            MainThreadGpuDispose.Flush();

            if (proj.IsLocked || !proj.HasPayload || !proj.IsDefined)
                return;

            Payload payload = proj.Payload;
            int w = payload.Width;
            int h = payload.Height;
            if (w <= 0 || h <= 0)
                return;

            GetVisibleRelativeRange(proj.Area, w, h, out int minX, out int maxX, out int minY, out int maxY);
            ApplyNearbyOnlyClip(proj.Area, w, h, ref minX, ref maxX, ref minY, ref maxY);
            if (minX >= maxX || minY >= maxY)
                return;

            bool IncludeCell(Point p) => TryGetIncludedCell(proj, payload, p, out _);

            if (ProjectionPreviewCache.IsCachedBlitEnabled())
            {
                ProjectionPreviewCache.DrawCached(
                    Main.spriteBatch,
                    proj,
                    payload,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    PreviewTint,
                    IncludeCell);
                EntityLayer.DrawLiveHostMarkers(
                    Main.spriteBatch,
                    proj.Area.X,
                    proj.Area.Y,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    PreviewTint,
                    IncludeCell);
                return;
            }

            Vector2 origin = new Vector2(proj.Area.X * TilePixelSize, proj.Area.Y * TilePixelSize) - Main.screenPosition;
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
                payload.Entities,
                payload.CellIndex);
        }

        /// <summary>
        /// Draws included payload cells as a single baked map-color blit on the fullscreen map.
        /// </summary>
        public static void DrawOnMap(ref MapOverlayDrawContext context, Projection proj)
        {
            // --- Blit one W×H map-color texture instead of scanning every sparse entry per frame.

            MainThreadGpuDispose.Flush();

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

            float pxPerTile = ArchitectMapTransform.PixelsPerTile;
            if (pxPerTile <= 0f)
                return;

            if (!TryGetOrBuildMapTexture(proj, payload, out Texture2D texture))
                return;

            // Source crop is the visible relative window; destination maps that window to screen.
            var source = new Rectangle(minX, minY, maxX - minX, maxY - minY);
            Vector2 topLeft = ArchitectMapTransform.TileToScreen(new Point(proj.Area.X + minX, proj.Area.Y + minY));
            var dest = new Rectangle(
                (int)topLeft.X,
                (int)topLeft.Y,
                (int)(source.Width * pxPerTile),
                (int)(source.Height * pxPerTile));

            float tintAlpha = PreviewTint.A / 255f;
            Color tint = Color.White * tintAlpha;
            tint.A = (byte)(255 * tintAlpha);

            // Match DrawBorderedRectOnMap: draw into the map overlay batch without restarting it.
            Main.spriteBatch.Draw(texture, dest, source, tint);
            _ = context;
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
                entities: payload.Entities,
                cellLookup: payload.CellIndex);

            Main.spriteBatch.End();
            Main.instance.GraphicsDevice.SetRenderTarget(null);

            var mergedTexture = new Texture2D(Main.graphics.GraphicsDevice, size, size);
            var content = new Color[size * size];
            renderTarget.GetData(content);
            mergedTexture.SetData(content);
            renderTarget.Dispose();
            return mergedTexture;
        }

        /// <summary>Disposes every cached map-color preview texture.</summary>
        public static void ClearMapTextureCache()
        {
            // --- Detach GPU resources and queue main-thread dispose (OnWorldUnload may be off-thread).

            foreach (MapPreviewEntry entry in MapTextures.Values)
                MainThreadGpuDispose.Dispose(entry.Texture);
            MapTextures.Clear();
        }

        /// <summary>Removes one projection's map-color cache entry when that ghost is discarded.</summary>
        public static void InvalidateMapTexture(int overlayInstanceKey)
        {
            // --- Queue dispose of a single entry so inclusion or payload edits can rebuild on next draw.

            if (!MapTextures.Remove(overlayInstanceKey, out MapPreviewEntry? entry))
                return;

            MainThreadGpuDispose.Dispose(entry.Texture);
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

        /// <summary>Returns a W×H map-color texture for the projection, rebuilding when inclusion or payload changes.</summary>
        private static bool TryGetOrBuildMapTexture(Projection proj, Payload payload, out Texture2D texture)
        {
            // --- Reuse the cached bitmap when size, inclusion version, and entry count still match.

            int key = proj.OverlayInstanceKey;
            int inclusionVersion = proj.InclusionVersion;
            int w = payload.Width;
            int h = payload.Height;
            int entryCount = payload.Entries.Count;

            if (MapTextures.TryGetValue(key, out MapPreviewEntry? entry)
                && entry.Texture is not null
                && !entry.Texture.IsDisposed
                && entry.InclusionVersion == inclusionVersion
                && entry.Width == w
                && entry.Height == h
                && entry.EntryCount == entryCount)
            {
                texture = entry.Texture;
                return true;
            }

            if (entry is not null)
            {
                MainThreadGpuDispose.Dispose(entry.Texture);
                MapTextures.Remove(key);
            }

            var colors = new Color[w * h];
            IReadOnlyList<Payload.Entry> entries = payload.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                Point rel = entries[i].Relative;
                if ((uint)rel.X >= (uint)w || (uint)rel.Y >= (uint)h)
                    continue;

                if (!TryGetIncludedCell(proj, payload, rel, out PayloadCell cell))
                    continue;

                if (!PayloadMapColor.TryGetMapColor(cell, out Color mapColor))
                    continue;

                colors[rel.Y * w + rel.X] = mapColor;
            }

            texture = new Texture2D(Main.graphics.GraphicsDevice, w, h);
            texture.SetData(colors);
            MapTextures[key] = new MapPreviewEntry
            {
                Texture = texture,
                InclusionVersion = inclusionVersion,
                Width = w,
                Height = h,
                EntryCount = entryCount,
            };
            return true;
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

            IntersectRelativeClip(
                area,
                width,
                height,
                leftTile,
                rightTile,
                topTile,
                bottomTile,
                out minX,
                out maxX,
                out minY,
                out maxY);
        }

        /// <summary>Further clips the relative window to the player Chebyshev nearby radius when enabled.</summary>
        private static void ApplyNearbyOnlyClip(
            Rectangle area,
            int width,
            int height,
            ref int minX,
            ref int maxX,
            ref int minY,
            ref int maxY)
        {
            // --- Intersect the current clip with ±NearbyRadiusTiles around the local player.

            ProjectionPreviewPreference preview = ModContent.GetInstance<ClientPasteConfig>().Preview;
            if (!preview.NearbyOnly)
                return;

            Point playerTile = Main.LocalPlayer.Center.ToTileCoordinates();
            int radius = Math.Clamp(preview.NearbyRadiusTiles, 8, 200);
            IntersectRelativeClip(
                area,
                width,
                height,
                playerTile.X - radius,
                playerTile.X + radius,
                playerTile.Y - radius,
                playerTile.Y + radius,
                out int nearMinX,
                out int nearMaxX,
                out int nearMinY,
                out int nearMaxY);

            minX = Math.Max(minX, nearMinX);
            maxX = Math.Min(maxX, nearMaxX);
            minY = Math.Max(minY, nearMinY);
            maxY = Math.Min(maxY, nearMaxY);
        }

        /// <summary>Intersects a world-tile window with the projection area and returns relative clip bounds.</summary>
        private static void IntersectRelativeClip(
            Rectangle area,
            int width,
            int height,
            int leftTile,
            int rightTile,
            int topTile,
            int bottomTile,
            out int minX,
            out int maxX,
            out int minY,
            out int maxY)
        {
            // --- Clamp world tiles to the projection footprint, then convert to relative indices.

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
