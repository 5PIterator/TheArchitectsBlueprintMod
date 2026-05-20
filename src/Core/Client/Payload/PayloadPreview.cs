using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.Drawing;
using Terraria.GameContent.Liquid;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.UI
{
    /// <summary>
    /// Draws semi-transparent tile/wall/liquid sprites for payload previews (in-world projection overlay and UI thumbnails).
    /// </summary>
    internal static class PayloadPreview
    {
        private const int TilePixelSize = 16;
        private static readonly Color PreviewTint = new(255, 255, 255, 160);

        private static FieldInfo? _paintSystemFieldInfo;
        private static TilePaintSystemV2? _paintSystem;

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

            DrawPayloadRegion(Main.spriteBatch, payload, minX, maxX, minY, maxY, origin, 1f, IncludeCell);
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

            DrawPayloadRegion(Main.spriteBatch, payload, 0, w, 0, h, offset, scale);

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

        private static void DrawPayloadRegion(
            SpriteBatch sb,
            Payload payload,
            int minX,
            int maxX,
            int minY,
            int maxY,
            Vector2 position,
            float scale,
            Func<Point, bool>? includeCell = null)
        {
            for (int y = minY; y < maxY; y++)
            {
                for (int x = minX; x < maxX; x++)
                {
                    var point = new Point(x, y);
                    if (includeCell is not null && !includeCell(point))
                        continue;
                    if (!payload.TryGetCell(point, out PayloadCell cell) || cell.IsEmpty)
                        continue;
                    DrawWall(sb, cell, position, x, y, scale);
                }
            }

            for (int y = minY; y < maxY; y++)
            {
                for (int x = minX; x < maxX; x++)
                {
                    var point = new Point(x, y);
                    if (includeCell is not null && !includeCell(point))
                        continue;
                    if (!payload.TryGetCell(point, out PayloadCell cell) || cell.IsEmpty)
                        continue;
                    DrawLiquid(sb, cell, position, x, y, scale);
                }
            }

            for (int y = minY; y < maxY; y++)
            {
                for (int x = minX; x < maxX; x++)
                {
                    var point = new Point(x, y);
                    if (includeCell is not null && !includeCell(point))
                        continue;
                    if (!payload.TryGetCell(point, out PayloadCell cell) || cell.IsEmpty)
                        continue;
                    DrawTile(sb, cell, position, x, y, scale);
                }
            }
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

        private static void DrawWall(SpriteBatch sb, PayloadCell cell, Vector2 position, int x, int y, float scale)
        {
            if (cell.Wall.Type == 0 || cell.Paint.WallInvisible)
                return;

            ushort wallType = cell.Wall.Type;
            Main.instance.LoadWall(wallType);
            Texture2D textureWall = GetWallDrawTexture(wallType, cell.Paint.WallColor);
            int wallFrame = Main.wallFrame[wallType] * 180;
            var source = new Rectangle(cell.Wall.FrameX, cell.Wall.FrameY + wallFrame, 32, 32);
            Vector2 pos = position + new Vector2(x * TilePixelSize - 8, y * TilePixelSize - 8);
            sb.Draw(textureWall, pos * scale, source, PreviewTint, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private static void DrawLiquid(SpriteBatch sb, PayloadCell cell, Vector2 position, int x, int y, float scale)
        {
            if (cell.Liquid.Amount <= 14)
                return;

            Texture2D textureWater = cell.Liquid.Type switch
            {
                LiquidID.Honey => LiquidRenderer.Instance._liquidTextures[11].Value,
                LiquidID.Lava => LiquidRenderer.Instance._liquidTextures[1].Value,
                _ => LiquidRenderer.Instance._liquidTextures[0].Value,
            };
            int waterSize = (cell.Liquid.Amount + 1) / 16;
            var source = new Rectangle(16, 48 + 16 - waterSize, 16, waterSize);
            Vector2 pos = position + new Vector2(x * TilePixelSize, y * TilePixelSize + (TilePixelSize - waterSize));
            sb.Draw(textureWater, pos * scale, source, PreviewTint, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private static void DrawTile(SpriteBatch sb, PayloadCell cell, Vector2 position, int x, int y, float scale)
        {
            if (!cell.Detail.HasTile || cell.Paint.TileInvisible)
                return;

            ushort tileType = cell.Block.Type;
            Main.instance.LoadTiles(tileType);
            Texture2D texture = TextureAssets.Tile[tileType].Value;
            var source = new Rectangle(cell.Block.FrameX, cell.Block.FrameY, TilePixelSize, TilePixelSize);
            Vector2 pos = position + new Vector2(x * TilePixelSize, y * TilePixelSize);
            sb.Draw(texture, pos * scale, source, PreviewTint, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private static Texture2D GetWallDrawTexture(ushort wallType, byte wallColor)
        {
            if (_paintSystemFieldInfo is null)
            {
                _paintSystemFieldInfo = typeof(WallDrawing).GetField("_paintSystem", BindingFlags.Instance | BindingFlags.NonPublic);
                _paintSystem = (TilePaintSystemV2)_paintSystemFieldInfo.GetValue(Main.instance.WallsRenderer)!;
            }

            Texture2D result = TextureAssets.Wall[wallType].Value;
            Texture2D? painted = _paintSystem!.TryGetWallAndRequestIfNotReady(wallType, wallColor);
            if (painted is not null)
                result = painted;

            return result;
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
