using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace ArchitectsStencilMod.UIKit
{
    /// <summary>
    /// Draggable screen-space handle used by blueprint views for hit testing and visual feedback.
    /// </summary>
    internal sealed class UIHandle : UIView
    {
        public Rectangle ScreenBounds { get; private set; }

        public UIHandle()
        {
            UpdateWhenOutOfBounds = true;
        }

        /// <summary>Sets the handle rectangle in screen-space pixels.</summary>
        public void SetBounds(Rectangle bounds)
        {
            // --- Mirror the absolute screen rectangle into UIView position and size.

            ScreenBounds = bounds;
            Position = new Vector2(bounds.X, bounds.Y);
            Width = bounds.Width;
            Height = bounds.Height;
        }

        /// <summary>Returns true when the point lies inside this handle.</summary>
        public bool Contains(Point screenPoint)
        {
            // --- Use the stored screen rectangle for stable hit testing.

            return Visible && ScreenBounds.Contains(screenPoint);
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible)
                return;

            var fill = MouseInside ? Color.White * 0.9f : Color.White * 0.55f;
            UIBlueprint.DrawScreenRectangle(spriteBatch, ScreenBounds, fill, Color.Black * 0.6f);
            base.Draw(spriteBatch);
        }
    }
}
