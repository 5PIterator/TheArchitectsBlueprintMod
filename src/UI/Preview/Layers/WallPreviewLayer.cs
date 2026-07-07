using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.Drawing;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.UI.Preview.Layers
{
    internal sealed class WallPreviewLayer : PreviewLayer
    {
        public static WallPreviewLayer Instance { get; } = new();
        public override string Id => "Wall";

        private static FieldInfo? _paintSystemFieldInfo;
        private static TilePaintSystemV2? _paintSystem;

        public override void Draw(in PreviewDrawContext ctx, PayloadCell cell, int relX, int relY)
        {
            Tile tile = cell.ToTile();
            if (tile.WallType == WallID.None)
                return;

            if ((cell.Paint.WallInvisible || tile.WallType == WallID.EchoWall) && !Main.ShouldShowInvisibleWalls())
                return;

            ushort wallType = tile.WallType;
            Main.instance.LoadWall(wallType);

            // get the wall draw texture

            Texture2D textureWall;
            if (_paintSystemFieldInfo is null)
            {
                _paintSystemFieldInfo = typeof(WallDrawing).GetField("_paintSystem", BindingFlags.Instance | BindingFlags.NonPublic);
                _paintSystem = (TilePaintSystemV2)_paintSystemFieldInfo.GetValue(Main.instance.WallsRenderer)!;
            }

            textureWall = TextureAssets.Wall[wallType].Value;
            Texture2D? painted = _paintSystem!.TryGetWallAndRequestIfNotReady(wallType, tile.WallColor);
            if (painted is not null)
                textureWall = painted;

            int wallFrame = Main.wallFrame[wallType] * 180;
            var source = new Rectangle(tile.WallFrameX, tile.WallFrameY + wallFrame, 32, 32);
            Vector2 pos = ctx.Position + new Vector2(
                relX * PreviewDrawContext.TilePixelSize - 8,
                relY * PreviewDrawContext.TilePixelSize - 8);
            ctx.SpriteBatch.Draw(textureWall, pos * ctx.Scale, source, ctx.Tint, 0f, Vector2.Zero, ctx.Scale, SpriteEffects.None, 0f);
        }
    }
}
