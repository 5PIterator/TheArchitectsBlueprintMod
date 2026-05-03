using ArchitectsStencilMod.ModUtils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ArchitectsStencilMod.UIKit
{
	internal class UIRect : UIView
	{
		public UIRect()
		{
			this.Width = 10;
			this.Height = 10;
		}

		public UIRect(Vector2 position, float width, float height)
		{
			this.Position = position;
			this.Width = width;
			this.Height = height;
		}

		public override void Draw(SpriteBatch spriteBatch)
		{
            Texture2D texture = ModUtils.ModUtils.DummyTexture;
			spriteBatch.Draw(texture, new Rectangle((int)(DrawPosition.X - Origin.X), (int)(DrawPosition.Y - Origin.Y), (int)Width, (int)Height), ForegroundColor);
			base.Draw(spriteBatch);
		}
	}
}