using System;
using System.Collections.Generic;
using Terraria.UI;
using TheArchitectsBlueprintMod.UI.Style;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    public enum TmodStackOrientation
    {
        Horizontal,
        Vertical
    }

    /// <summary>Parent container that lays out children in a row or column with uniform spacing.</summary>
    public sealed class TmodStackPanel : UIElement
    {
        public TmodStackOrientation Orientation { get; }
        public float Spacing { get; set; }
        public bool CrossAxisCenter { get; set; } = true;

        public TmodStackPanel(TmodStackOrientation orientation, float spacing = Theme.Spacing)
        {
            Orientation = orientation;
            Spacing = spacing;
        }

        public static TmodStackPanel Horizontal(float spacing = Theme.Spacing, bool crossAxisCenter = true) =>
            new(TmodStackOrientation.Horizontal, spacing) { CrossAxisCenter = crossAxisCenter };

        public static TmodStackPanel Vertical(float spacing = Theme.Spacing, bool crossAxisCenter = false) =>
            new(TmodStackOrientation.Vertical, spacing) { CrossAxisCenter = crossAxisCenter };

        public TmodStackGrow AppendGrow()
        {
            var grow = new TmodStackGrow();
            Append(grow);
            return grow;
        }

        public void ClearChildren()
        {
            while (Elements.Count > 0)
                RemoveChild(Elements[0]);
        }

        public override void Recalculate()
        {
            base.Recalculate();
            ApplyStackLayout();
        }

        private void ApplyStackLayout()
        {
            CalculatedStyle inner = GetInnerDimensions();
            List<UIElement> children = CollectLayoutChildren();
            if (children.Count == 0)
                return;

            if (Orientation == TmodStackOrientation.Horizontal)
                LayoutHorizontal(inner, children);
            else
                LayoutVertical(inner, children);
        }

        private List<UIElement> CollectLayoutChildren()
        {
            var children = new List<UIElement>();
            foreach (UIElement child in Elements)
                children.Add(child);

            return children;
        }

        private void LayoutHorizontal(CalculatedStyle inner, List<UIElement> children)
        {
            float growWidth = MeasureHorizontalGrowWidth(inner, children);
            float cursor = 0f;
            bool first = true;

            foreach (UIElement child in children)
            {
                if (!first)
                    cursor += Spacing;
                first = false;

                CalculatedStyle dims = child.GetOuterDimensions();
                float childWidth = child is TmodStackGrow ? growWidth : dims.Width;
                float childHeight = dims.Height;
                float top = CrossAxisCenter
                    ? (inner.Height - childHeight) * 0.5f
                    : 0f;

                child.Left.Set(cursor, 0f);
                child.Top.Set(top, 0f);

                if (child is TmodStackGrow && growWidth > 0f)
                    child.Width.Set(growWidth, 0f);

                cursor += childWidth;
            }
        }

        private void LayoutVertical(CalculatedStyle inner, List<UIElement> children)
        {
            float cursor = 0f;
            bool first = true;

            foreach (UIElement child in children)
            {
                if (!first)
                    cursor += Spacing;
                first = false;

                CalculatedStyle dims = child.GetOuterDimensions();
                float childWidth = dims.Width;
                float childHeight = dims.Height;
                float left = CrossAxisCenter
                    ? (inner.Width - childWidth) * 0.5f
                    : 0f;

                child.Left.Set(left, 0f);
                child.Top.Set(cursor, 0f);
                cursor += childHeight;
            }
        }

        private float MeasureHorizontalGrowWidth(CalculatedStyle inner, List<UIElement> children)
        {
            float fixedWidth = 0f;
            int growCount = 0;
            int visibleCount = children.Count;

            foreach (UIElement child in children)
            {
                if (child is TmodStackGrow)
                {
                    growCount++;
                    continue;
                }

                fixedWidth += child.GetOuterDimensions().Width;
            }

            if (growCount == 0)
                return 0f;

            float gap = Math.Max(0, visibleCount - 1) * Spacing;
            return Math.Max(0f, inner.Width - fixedWidth - gap);
        }
    }

    /// <summary>Expands along the main axis of a horizontal <see cref="TmodStackPanel"/>.</summary>
    public sealed class TmodStackGrow : UIElement
    {
        public TmodStackGrow()
        {
            IgnoresMouseInteraction = true;
            Width.Set(0f, 0f);
            Height.Set(0f, 0f);
        }
    }
}
