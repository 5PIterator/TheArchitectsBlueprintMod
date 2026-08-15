using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Enums;
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

            PayloadCell.DetailData detail = cell.Detail;
            if (!detail.IsActuated && !detail.IsHalfBlock && detail.Slope == SlopeType.Solid && detail.BlockType == BlockType.Solid)
                return;

            if (!PreviewTileDraw.TryPrepare(cell, out PreviewTileDraw.PreparedTile prepared))
                return;

            if (!PreviewTileDraw.TryResolve(in prepared, in ctx, relX, relY, out PreviewTileDraw.DrawSprite sprite))
                return;

            Vector2 pos = PreviewTileDraw.GetDrawPosition(in ctx, relX, relY, in sprite);
            Color drawTint = detail.IsActuated ? ctx.Tint * 0.4f : ctx.Tint;
            SlopeType slope = detail.Slope;
            if (slope != SlopeType.Solid)
                DrawSlopeTile(ctx.SpriteBatch, sprite.Texture, sprite.Source, pos, ctx.Scale, sprite.Effects, slope, drawTint);
            else
                ctx.SpriteBatch.Draw(sprite.Texture, pos * ctx.Scale, sprite.Source, drawTint, 0f, Vector2.Zero, ctx.Scale, sprite.Effects, 0f);
        }

        /// <summary>Draws a sloped tile by clipping each vertical column to the slope silhouette.</summary>
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
            // --- Slice the source into one-pixel columns and keep only the slope-visible span of each.

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
