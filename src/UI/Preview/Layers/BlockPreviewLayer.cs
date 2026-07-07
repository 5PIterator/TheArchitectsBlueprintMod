using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Enums;
using Terraria.GameContent.Drawing;
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

            if (!cell.Detail.HasTile)
                return;

            Tile tile = cell.ToTile();
            if (!TileDrawing.IsVisible(tile))
                return;

            PayloadCell.DetailData detail = cell.Detail;
            if (detail.IsActuated || detail.IsHalfBlock || detail.Slope != SlopeType.Solid || detail.BlockType != BlockType.Solid)
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

            ctx.SpriteBatch.Draw(texture, pos * ctx.Scale, source, ctx.Tint, 0f, Vector2.Zero, ctx.Scale, effects, 0f);
        }
    }
}
