using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ModLoader.UI.Elements;
using Terraria.UI;
using TheArchitectsBlueprintMod.UI.Style;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Panel container draggable from its padded border (see <see cref="EdgeDragPadding"/>).</summary>
    public sealed class TmodWindowPanel : TmodPanel
    {
        private const string ScrollLockKey = "TheArchitectsBlueprintMod/TmodWindowPanel";

        /// <summary>True while a descendant <see cref="UISearchBar"/> (or similar) is accepting keyboard input.</summary>
        internal static bool IsCapturingTextInput { get; private set; }

        internal static void ResetInputState() => IsCapturingTextInput = false;

        public TmodWindowPanel()
        {
            BackgroundColor = Theme.WindowPanelBackground;
            BorderColor = Theme.WindowPanelBorder;
        }

        private bool _dragging;
        private bool _usingAbsolutePosition;
        private Vector2 _dragOffset;
        /// <summary>Width of the non-client margin that starts a window drag on click.</summary>

        public float EdgeDragPadding { get; set; } = Theme.Padding;
        public bool IsDragging => _dragging;

        /// <summary>When set, returns whether a descendant text field is actively capturing keyboard input.</summary>
        public Func<bool>? IsTextInputActive { get; set; }
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
            if (!IsInEdgeDragZone(evt.MousePosition))
                return;
            if (BlocksWindowDrag(evt.Target))
                return;
            BeginDrag(evt);
        }

        private bool IsInEdgeDragZone(Vector2 mousePosition)
        {

            CalculatedStyle outer = GetOuterDimensions();
            float pad = EdgeDragPadding;
            float innerLeft = outer.X + pad;
            float innerTop = outer.Y + pad;
            float innerRight = outer.X + outer.Width - pad;
            float innerBottom = outer.Y + outer.Height - pad;
            float x = mousePosition.X;
            float y = mousePosition.Y;
            if (x < outer.X || x > outer.X + outer.Width || y < outer.Y || y > outer.Y + outer.Height)
                return false;
            return x < innerLeft || x > innerRight || y < innerTop || y > innerBottom;
        }

        /// <summary>Returns true when the click target is an interactive control that should keep its own handler.</summary>

        private static bool BlocksWindowDrag(UIElement? target)
        {
            for (UIElement? element = target; element != null; element = element.Parent)
            {

                if (element is UITextPanel<string>
                    or UITextBox
                    or UISearchBar
                    or TmodFramedControl
                    or TmodUITextField)
                {
                    return true;
                }
            }

            return false;
        }

        public override void LeftMouseUp(UIMouseEvent evt)
        {
            base.LeftMouseUp(evt);
            _dragging = false;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            ApplyPointerCapture();

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

        /// <summary>
        /// Non-modal capture: block world clicks only over this window; block all gameplay input while typing.
        /// </summary>
        private void ApplyPointerCapture()
        {
            Player? player = Main.LocalPlayer;
            if (player == null || !TmodGUI.IsOpen)
            {
                IsCapturingTextInput = false;
                return;
            }

            bool textInputActive = IsTextInputActive?.Invoke() ?? false;
            IsCapturingTextInput = textInputActive;
            bool mouseOverWindow = ContainsPoint(Main.MouseScreen);

            if (textInputActive)
            {
                Main.blockInput = true;
                player.mouseInterface = true;
                return;
            }

            Main.blockInput = false;

            if (!mouseOverWindow)
                return;

            player.mouseInterface = true;
            PlayerInput.LockVanillaMouseScroll(ScrollLockKey);
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