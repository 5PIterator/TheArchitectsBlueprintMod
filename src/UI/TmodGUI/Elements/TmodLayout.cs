using Terraria.UI;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Layout helpers for positioning TmodGUI elements.</summary>
    public static class TmodLayout
    {
        public static void CenterInParent(UIElement element, float width, float height)
        {
            element.Width.Set(width, 0f);
            element.Height.Set(height, 0f);
            element.HAlign = 0.5f;
            element.VAlign = 0.5f;
        }

        public static void FillWidth(UIElement element, float height, float top = 0f)
        {
            element.Top.Set(top, 0f);
            element.Left.Set(0f, 0f);
            element.Width.Set(0f, 1f);
            element.Height.Set(height, 0f);
        }

        public static void AnchorTopLeft(UIElement element, float left, float top, float width, float height)
        {
            element.Left.Set(left, 0f);
            element.Top.Set(top, 0f);
            element.Width.Set(width, 0f);
            element.Height.Set(height, 0f);
        }

        public static void AnchorBottomRight(UIElement element, float right, float bottom, float width, float height)
        {
            element.HAlign = 1f;
            element.VAlign = 1f;
            element.MarginRight = right;
            element.MarginBottom = bottom;
            element.Width.Set(width, 0f);
            element.Height.Set(height, 0f);
        }
    }
}
