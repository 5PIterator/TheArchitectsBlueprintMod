using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Enums;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.UI.Preview.Layers
{
    internal sealed class BlockPreviewLayer : PreviewLayer
    {
        public static BlockPreviewLayer Instance { get; } = new();
        public override string Id => "Block";

        public override void Draw(in PreviewDrawContext ctx, PayloadCell cell, int relX, int relY)
        {
            // --- Draw plain solid tiles; detail-modified tiles are handled by DetailPreviewLayer.

            PayloadCell.DetailData detail = cell.Detail;
            if (detail.IsActuated || detail.IsHalfBlock || detail.Slope != SlopeType.Solid || detail.BlockType != BlockType.Solid)
                return;

            if (!PreviewTileDraw.TryPrepare(cell, out PreviewTileDraw.PreparedTile prepared))
                return;

            if (!PreviewTileDraw.TryResolve(in prepared, out PreviewTileDraw.DrawSprite sprite))
                return;

            Vector2 pos = PreviewTileDraw.GetDrawPosition(in ctx, relX, relY, in sprite);
            ctx.SpriteBatch.Draw(sprite.Texture, pos * ctx.Scale, sprite.Source, ctx.Tint, 0f, Vector2.Zero, ctx.Scale, sprite.Effects, 0f);
        }
    }
}
