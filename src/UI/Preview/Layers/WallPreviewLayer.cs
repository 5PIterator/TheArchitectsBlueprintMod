using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.Drawing;
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
            if (cell.Wall.Type == 0 || cell.Paint.WallInvisible)
                return;

            ushort wallType = cell.Wall.Type;
            Main.instance.LoadWall(wallType);
            Texture2D textureWall = GetWallDrawTexture(wallType, cell.Paint.WallColor);
            int wallFrame = Main.wallFrame[wallType] * 180;
            var source = new Rectangle(cell.Wall.FrameX, cell.Wall.FrameY + wallFrame, 32, 32);
            Vector2 pos = ctx.Position + new Vector2(
                relX * PreviewDrawContext.TilePixelSize - 8,
                relY * PreviewDrawContext.TilePixelSize - 8);
            ctx.SpriteBatch.Draw(textureWall, pos * ctx.Scale, source, ctx.Tint, 0f, Vector2.Zero, ctx.Scale, SpriteEffects.None, 0f);
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
    }
}
