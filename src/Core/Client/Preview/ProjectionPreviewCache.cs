using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core.Layers;

namespace TheArchitectsBlueprintMod.Core.Preview
{
    /// <summary>
    /// Lazy 64×64 tile RenderTarget cache for in-world projection previews.
    /// </summary>
    internal static class ProjectionPreviewCache
    {
        public const int ChunkTiles = 64;
        private const int MaxBakesPerFrame = 4;
        private const int TilePixelSize = 16;

        private sealed class ChunkTarget
        {
            public RenderTarget2D Target = null!;
            public int ChunkX;
            public int ChunkY;
            public int TileWidth;
            public int TileHeight;
        }

        private sealed class ProjectionCache
        {
            public int InclusionVersion;
            public int EntryCount;
            public int Width;
            public int Height;
            public readonly Dictionary<long, ChunkTarget> Chunks = new();
        }

        private static readonly Dictionary<int, ProjectionCache> Caches = new();
        private static int _bakesThisFrame;
        private static uint _bakeFrame;

        /// <summary>Disposes every cached chunk render target.</summary>
        public static void Clear()
        {
            // --- Detach GPU resources and queue main-thread dispose (OnWorldUnload may be off-thread).

            foreach (ProjectionCache cache in Caches.Values)
                DisposeChunks(cache);
            Caches.Clear();
            _bakesThisFrame = 0;
        }

        /// <summary>Removes one projection's chunk cache when that ghost is discarded.</summary>
        public static void Invalidate(int overlayInstanceKey)
        {
            // --- Dispose all chunks for a single overlay instance key.

            if (!Caches.Remove(overlayInstanceKey, out ProjectionCache? cache))
                return;

            DisposeChunks(cache);
        }

        /// <summary>
        /// Blits cached chunks for the visible relative window, baking missing chunks up to a per-frame cap.
        /// </summary>
        public static void DrawCached(
            SpriteBatch sb,
            Projection proj,
            Payload payload,
            int minX,
            int maxX,
            int minY,
            int maxY,
            Color tint,
            Func<Point, bool> includeCell)
        {
            // --- Ensure cache identity, bake a few missing chunks, then blit each visible chunk once.

            MainThreadGpuDispose.Flush();
            ResetBakeBudgetIfNewFrame();

            ProjectionCache cache = GetOrCreateCache(proj, payload);
            int chunkMinX = minX / ChunkTiles;
            int chunkMaxX = (maxX - 1) / ChunkTiles;
            int chunkMinY = minY / ChunkTiles;
            int chunkMaxY = (maxY - 1) / ChunkTiles;

            for (int cy = chunkMinY; cy <= chunkMaxY; cy++)
            {
                for (int cx = chunkMinX; cx <= chunkMaxX; cx++)
                {
                    long key = PackChunkKey(cx, cy);
                    if (!cache.Chunks.TryGetValue(key, out ChunkTarget? chunk)
                        || chunk.Target is null
                        || chunk.Target.IsDisposed)
                    {
                        if (_bakesThisFrame >= MaxBakesPerFrame)
                            continue;

                        if (!TryBakeChunk(proj, payload, cx, cy, includeCell, out chunk))
                            continue;

                        cache.Chunks[key] = chunk;
                        _bakesThisFrame++;
                    }

                    BlitChunk(sb, proj, chunk, minX, maxX, minY, maxY, tint);
                }
            }
        }

        /// <summary>Returns whether the local client prefers cached projection blits.</summary>
        public static bool IsCachedBlitEnabled()
        {
            // --- Read ClientPasteConfig.Preview without allocating.

            return ModContent.GetInstance<ClientPasteConfig>().Preview.UseCachedBlit;
        }

        /// <summary>Ensures the projection cache matches payload size and inclusion version.</summary>
        private static ProjectionCache GetOrCreateCache(Projection proj, Payload payload)
        {
            // --- Invalidate when inclusion, size, or sparse entry count changes.

            int key = proj.OverlayInstanceKey;
            if (Caches.TryGetValue(key, out ProjectionCache? cache)
                && cache.InclusionVersion == proj.InclusionVersion
                && cache.EntryCount == payload.Entries.Count
                && cache.Width == payload.Width
                && cache.Height == payload.Height)
            {
                return cache;
            }

            if (cache is not null)
                DisposeChunks(cache);

            cache = new ProjectionCache
            {
                InclusionVersion = proj.InclusionVersion,
                EntryCount = payload.Entries.Count,
                Width = payload.Width,
                Height = payload.Height,
            };
            Caches[key] = cache;
            return cache;
        }

        /// <summary>Bakes one chunk into a RenderTarget2D using LayerSystem.DrawRegion at scale 1.</summary>
        private static bool TryBakeChunk(
            Projection proj,
            Payload payload,
            int chunkX,
            int chunkY,
            Func<Point, bool> includeCell,
            out ChunkTarget chunk)
        {
            // --- Pause the world sprite batch, draw into an RT without world origin, then restore.

            chunk = null!;
            int originX = chunkX * ChunkTiles;
            int originY = chunkY * ChunkTiles;
            int tileW = Math.Min(ChunkTiles, payload.Width - originX);
            int tileH = Math.Min(ChunkTiles, payload.Height - originY);
            if (tileW <= 0 || tileH <= 0)
                return false;

            GraphicsDevice device = Main.graphics.GraphicsDevice;
            var target = new RenderTarget2D(
                device,
                tileW * TilePixelSize,
                tileH * TilePixelSize,
                false,
                SurfaceFormat.Color,
                DepthFormat.None);

            RenderTargetBinding[] previousTargets = device.GetRenderTargets();
            bool batchWasBegun = true;
            try
            {
                Main.spriteBatch.End();
            }
            catch
            {
                batchWasBegun = false;
            }

            device.SetRenderTarget(target);
            device.Clear(Color.Transparent);
            Main.spriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.PointClamp,
                DepthStencilState.None,
                RasterizerState.CullNone);

            // Translation-invariant bake: local origin at (0,0), no live world-host markers.
            Vector2 localOrigin = new(-originX * TilePixelSize, -originY * TilePixelSize);
            LayerSystem.DrawRegion(
                Main.spriteBatch,
                payload.Entries,
                originX,
                originX + tileW,
                originY,
                originY + tileH,
                localOrigin,
                1f,
                Color.White,
                includeCell,
                worldOriginX: null,
                worldOriginY: null,
                entities: payload.Entities,
                cellLookup: payload.CellIndex);

            Main.spriteBatch.End();
            device.SetRenderTargets(previousTargets);

            if (batchWasBegun)
            {
                Main.spriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    Main.DefaultSamplerState,
                    DepthStencilState.None,
                    Main.Rasterizer,
                    null,
                    Main.GameViewMatrix.TransformationMatrix);
            }

            chunk = new ChunkTarget
            {
                Target = target,
                ChunkX = chunkX,
                ChunkY = chunkY,
                TileWidth = tileW,
                TileHeight = tileH,
            };
            return true;
        }

        /// <summary>Draws one chunk texture clipped to the visible relative window.</summary>
        private static void BlitChunk(
            SpriteBatch sb,
            Projection proj,
            ChunkTarget chunk,
            int minX,
            int maxX,
            int minY,
            int maxY,
            Color tint)
        {
            // --- Crop source pixels to the intersection of the chunk and the visible relative clip.

            int chunkMinX = chunk.ChunkX * ChunkTiles;
            int chunkMinY = chunk.ChunkY * ChunkTiles;
            int chunkMaxX = chunkMinX + chunk.TileWidth;
            int chunkMaxY = chunkMinY + chunk.TileHeight;

            int visMinX = Math.Max(minX, chunkMinX);
            int visMaxX = Math.Min(maxX, chunkMaxX);
            int visMinY = Math.Max(minY, chunkMinY);
            int visMaxY = Math.Min(maxY, chunkMaxY);
            if (visMinX >= visMaxX || visMinY >= visMaxY)
                return;

            var source = new Rectangle(
                (visMinX - chunkMinX) * TilePixelSize,
                (visMinY - chunkMinY) * TilePixelSize,
                (visMaxX - visMinX) * TilePixelSize,
                (visMaxY - visMinY) * TilePixelSize);

            Vector2 worldTopLeft = new(
                (proj.Area.X + visMinX) * TilePixelSize,
                (proj.Area.Y + visMinY) * TilePixelSize);
            Vector2 screenPos = worldTopLeft - Main.screenPosition;
            sb.Draw(chunk.Target, screenPos, source, tint);
        }

        private static void ResetBakeBudgetIfNewFrame()
        {
            if (_bakeFrame == Main.GameUpdateCount)
                return;

            _bakeFrame = Main.GameUpdateCount;
            _bakesThisFrame = 0;
        }

        private static long PackChunkKey(int chunkX, int chunkY) =>
            ((long)chunkX << 32) | (uint)chunkY;

        private static void DisposeChunks(ProjectionCache cache)
        {
            foreach (ChunkTarget chunk in cache.Chunks.Values)
                MainThreadGpuDispose.Dispose(chunk.Target);
            cache.Chunks.Clear();
        }
    }
}
