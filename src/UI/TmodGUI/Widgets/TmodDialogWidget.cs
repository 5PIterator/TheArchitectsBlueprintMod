using TheArchitectsBlueprintMod.UI.TmodGUI.Elements;
using Terraria.UI;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Widgets
{
    /// <summary>Centered dialog with title bar, content area, and footer row.</summary>
    public abstract class TmodDialogWidget : TmodWidgetBase
    {
        private readonly string _title;
        private readonly float _width;
        private readonly float _height;

        protected TmodPanel RootPanel { get; private set; } = null!;
        protected UIElement ContentArea { get; private set; } = null!;
        protected UIElement FooterArea { get; private set; } = null!;

        protected TmodDialogWidget(string title, float width, float height)
        {
            _title = title;
            _width = width;
            _height = height;
        }

        protected sealed override void Build()
        {
            RootPanel = TmodPanel.CreateCentered(_width, _height);
            Append(RootPanel);

            var titleLabel = new TmodLabel(_title, textScale: 1f);
            TmodLayout.AnchorTopLeft(titleLabel, 0f, 0f, _width - TmodTheme.Padding * 2, TmodTheme.TitleHeight);
            RootPanel.Append(titleLabel);

            float contentTop = TmodTheme.TitleHeight + TmodTheme.Spacing;
            float footerTop = _height - TmodTheme.FooterHeight - TmodTheme.Padding;
            float contentHeight = footerTop - contentTop - TmodTheme.Spacing;

            ContentArea = new UIElement();
            TmodLayout.AnchorTopLeft(ContentArea, 0f, contentTop, _width - TmodTheme.Padding * 2, contentHeight);
            RootPanel.Append(ContentArea);

            FooterArea = new UIElement();
            TmodLayout.AnchorTopLeft(FooterArea, 0f, footerTop, _width - TmodTheme.Padding * 2, TmodTheme.FooterHeight);
            RootPanel.Append(FooterArea);

            BuildContent(ContentArea);
            BuildFooter(FooterArea);
        }

        protected abstract void BuildContent(UIElement content);
        protected abstract void BuildFooter(UIElement footer);
    }
}
