using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.UI;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Panel container with a draggable title-bar region.</summary>
    public sealed class TmodWindowPanel : TmodPanel
    {
        private bool _dragging;
        private bool _usingAbsolutePosition;
        private Vector2 _dragOffset;

        public float DragHandleHeight { get; set; } = TmodTheme.TitleHeight + TmodTheme.Padding;
        public bool IsDragging => _dragging;

        public void BeginDrag(UIMouseEvent evt)
        {
            EnsureAbsolutePosition();
            CalculatedStyle outer = GetOuterDimensions();
            _dragging = true;
            _dragOffset = evt.MousePosition - new Vector2(outer.X, outer.Y);
        }

        public override void LeftMouseDown(UIMouseEvent evt)
        {
            base.LeftMouseDown(evt);

            if (evt.Target != this)
                return;

            CalculatedStyle outer = GetOuterDimensions();
            if (evt.MousePosition.Y > outer.Y + DragHandleHeight)
                return;

            BeginDrag(evt);
        }

        public override void LeftMouseUp(UIMouseEvent evt)
        {
            base.LeftMouseUp(evt);
            _dragging = false;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (_dragging && !Main.mouseLeft)
                _dragging = false;

            if (!_dragging || Parent == null)
                return;

            CalculatedStyle parentBounds = Parent.GetDimensions();
            CalculatedStyle outer = GetOuterDimensions();

            float left = Main.mouseX - _dragOffset.X - parentBounds.X;
            float top = Main.mouseY - _dragOffset.Y - parentBounds.Y;

            left = MathHelper.Clamp(left, 0f, Math.Max(0f, parentBounds.Width - outer.Width));
            top = MathHelper.Clamp(top, 0f, Math.Max(0f, parentBounds.Height - outer.Height));

            Left.Set(left, 0f);
            Top.Set(top, 0f);
            Recalculate();
        }

        private void EnsureAbsolutePosition()
        {
            if (_usingAbsolutePosition || Parent == null)
                return;

            CalculatedStyle outer = GetOuterDimensions();
            CalculatedStyle parent = Parent.GetDimensions();

            HAlign = 0f;
            VAlign = 0f;
            Left.Set(outer.X - parent.X, 0f);
            Top.Set(outer.Y - parent.Y, 0f);
            _usingAbsolutePosition = true;
            Recalculate();
        }
    }
}
