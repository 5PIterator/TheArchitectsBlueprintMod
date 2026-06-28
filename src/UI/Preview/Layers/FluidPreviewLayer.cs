using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent.Liquid;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.UI.Preview.Layers
{
    internal sealed class FluidPreviewLayer : PreviewLayer
    {
        public static FluidPreviewLayer Instance { get; } = new();
        public override string Id => "Fluid";

        public override void Draw(in PreviewDrawContext ctx, PayloadCell cell, int relX, int relY)
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
            Vector2 pos = ctx.Position + new Vector2(
                relX * PreviewDrawContext.TilePixelSize,
                relY * PreviewDrawContext.TilePixelSize + (PreviewDrawContext.TilePixelSize - waterSize));
            ctx.SpriteBatch.Draw(textureWater, pos * ctx.Scale, source, ctx.Tint, 0f, Vector2.Zero, ctx.Scale, SpriteEffects.None, 0f);
        }
    }
}
