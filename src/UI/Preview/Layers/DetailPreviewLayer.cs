using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Enums;
using Terraria.GameContent.Drawing;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.UI.Preview.Layers
{
    /// <summary>Slope, half-block, actuation, and platform rendering for payload tile previews.</summary>
    internal sealed class DetailPreviewLayer : PreviewLayer
    {
        public static DetailPreviewLayer Instance { get; } = new();
        public override string Id => "Detail";

        public override void Draw(in PreviewDrawContext ctx, PayloadCell cell, int relX, int relY)
        {
            // --- Draw tiles whose sprite depends on detail state; plain blocks use BlockPreviewLayer.

            if (!cell.Detail.HasTile)
                return;

            Tile tile = cell.ToTile();
            if (!TileDrawing.IsVisible(tile))
                return;

            PayloadCell.DetailData detail = cell.Detail;
            if (!detail.IsActuated && !detail.IsHalfBlock && detail.Slope == SlopeType.Solid && detail.BlockType == BlockType.Solid)
                return;

            ushort tileType = cell.Block.Type;
            Main.instance.LoadTiles(tileType);

            // Preview tile draw data should not depend on wall animation/frame state.
            TileWallWireStateData state = tile.Get<TileWallWireStateData>();
            int tileFrameNumber = state.TileFrameNumber;
            state.WallFrameX = 0;
            state.WallFrameY = 0;
            state.WallFrameNumber = 0;
            state.TileFrameNumber = tileFrameNumber;
            tile.Get<TileWallWireStateData>() = state;
            tile.Get<WallTypeData>() = default;

            TileDrawing renderer = Main.instance.TilesRenderer;
            int payloadX = relX;
            int payloadY = relY;

            short frameX = tile.TileFrameX;
            short frameY = tile.TileFrameY;
            renderer.GetTileDrawData(
                payloadX,
                payloadY,
                tile,
                tileType,
                ref frameX,
                ref frameY,
                out int tileWidth,
                out int tileHeight,
                out int tileTop,
                out int halfBrickHeight,
                out int addFrX,
                out int addFrY,
                out SpriteEffects effects,
                out Texture2D glowTexture,
                out Rectangle glowSourceRect,
                out Color glowColor);

            Texture2D texture = renderer.GetTileDrawTexture(tile, payloadX, payloadY);
            var source = new Rectangle(frameX + addFrX, frameY + addFrY, tileWidth, tileHeight - halfBrickHeight);
            Vector2 pos = ctx.Position + new Vector2(
                relX * PreviewDrawContext.TilePixelSize - (tileWidth - PreviewDrawContext.TilePixelSize) / 2f,
                relY * PreviewDrawContext.TilePixelSize + tileTop + halfBrickHeight);

            Color drawTint = detail.IsActuated ? ctx.Tint * 0.4f : ctx.Tint;
            SlopeType slope = detail.Slope;
            if (slope != SlopeType.Solid)
                DrawSlopeTile(ctx.SpriteBatch, texture, source, pos, ctx.Scale, effects, slope, drawTint);
            else
                ctx.SpriteBatch.Draw(texture, pos * ctx.Scale, source, drawTint, 0f, Vector2.Zero, ctx.Scale, effects, 0f);
        }

        private static void DrawSlopeTile(
            SpriteBatch sb,
            Texture2D texture,
            Rectangle source,
            Vector2 pos,
            float scale,
            SpriteEffects effects,
            SlopeType slope,
            Color tint)
        {
            static void DrawColumn(
                SpriteBatch batch,
                Texture2D tex,
                Rectangle srcBase,
                Vector2 basePos,
                float drawScale,
                SpriteEffects spriteEffects,
                Color columnTint,
                int col,
                int startY,
                int height)
            {
                if (col < 0 || col >= srcBase.Width || startY < 0 || startY >= srcBase.Height || height <= 0)
                    return;

                if (startY + height > srcBase.Height)
                    height = srcBase.Height - startY;
                if (height <= 0)
                    return;

                var src = new Rectangle(srcBase.X + col, srcBase.Y + startY, 1, height);
                Vector2 dst = basePos + new Vector2(col, startY);
                batch.Draw(tex, dst * drawScale, src, columnTint, 0f, Vector2.Zero, drawScale, spriteEffects, 0f);
            }

            for (int col = 0; col < source.Width; col++)
            {
                int startY;
                int height;
                switch (slope)
                {
                    case SlopeType.SlopeDownLeft:
                        startY = col;
                        height = source.Height - startY;
                        break;
                    case SlopeType.SlopeDownRight:
                        startY = source.Width - 1 - col;
                        height = source.Height - startY;
                        break;
                    case SlopeType.SlopeUpLeft:
                        startY = 0;
                        height = source.Width - col;
                        break;
                    case SlopeType.SlopeUpRight:
                        startY = 0;
                        height = col + 1;
                        break;
                    default:
                        sb.Draw(texture, pos * scale, source, tint, 0f, Vector2.Zero, scale, effects, 0f);
                        return;
                }

                DrawColumn(sb, texture, source, pos, scale, effects, tint, col, startY, height);
            }
        }
    }
}
