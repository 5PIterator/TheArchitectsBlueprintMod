using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;
using TheArchitectsBlueprintMod.UI.Style;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>
    /// Column options popup: dim overlay, blue panel, stacked <see cref="GroupOptionButton{T}"/> rows.
    /// Used for sort and filter dropdowns (vanilla Bestiary sort-menu layout).
    /// </summary>
    public sealed class TmodUITextOptionsPopup : UIPanel
    {
        private static readonly Color SelectedColor = new(152, 175, 235);
        private const float PanelWidth = 126f;
        private const float RowHeight = 26f;
        private const float RowSpacing = 2f;
        private const float RowInset = 5f;
        private const float PanelPadding = 8f;

        private readonly List<GroupOptionButton<int>> _buttons = [];
        private readonly Action<int> _onSelected;
        private int _currentIndex;

        public event Action? OnRequestClose;

        public TmodUITextOptionsPopup(
            IReadOnlyList<(int optionIndex, LocalizedText label)> options,
            int currentIndex,
            Action<int> onSelected,
            float anchorLeftPixels = Theme.BrowserOptionsPopupAnchorLeft)
        {
            _onSelected = onSelected;
            _currentIndex = currentIndex;

            Width.Set(0f, 1f);
            Height.Set(0f, 1f);
            BackgroundColor = new Color(35, 40, 83) * 0.5f;
            BorderColor = new Color(35, 40, 83) * 0.5f;
            SetPadding(0f);

            int rowStep = (int)(RowHeight + RowSpacing);
            int panelHeight = (int)(options.Count * rowStep + PanelPadding);
            var container = new UIPanel
            {
                Width = new StyleDimension(PanelWidth, 0f),
                Height = new StyleDimension(panelHeight, 0f),
                HAlign = 1f,
                VAlign = 0f,
                Left = new StyleDimension(anchorLeftPixels, 0f),
                Top = new StyleDimension(0f, 0f)
            };
            container.BorderColor = Theme.PanelBorder * 0.9f;
            container.BackgroundColor = Theme.PanelBackground * 0.9f;
            container.SetPadding(0f);

            for (int i = 0; i < options.Count; i++)
            {
                (int optionIndex, LocalizedText label) = options[i];
                var button = new GroupOptionButton<int>(optionIndex, label, null, Color.White, null, 0.8f)
                {
                    Width = new StyleDimension(114f, 0f),
                    Height = new StyleDimension(RowHeight, 0f),
                    HAlign = 0.5f,
                    Top = new StyleDimension(RowInset + rowStep * i, 0f)
                };
                button.ShowHighlightWhenSelected = false;
                button.OnLeftClick += OnOptionClick;
                container.Append(button);
                _buttons.Add(button);
            }

            Append(container);
            RefreshButtonColors();

            OnLeftClick += OnOverlayClick;
        }

        private void OnOverlayClick(UIMouseEvent evt, UIElement listeningElement)
        {
            if (evt.Target == this)
                OnRequestClose?.Invoke();
        }

        private void OnOptionClick(UIMouseEvent evt, UIElement listeningElement)
        {
            int index = ((GroupOptionButton<int>)listeningElement).OptionValue;
            _currentIndex = index;
            RefreshButtonColors();
            _onSelected(index);
            OnRequestClose?.Invoke();
        }

        private void RefreshButtonColors()
        {
            foreach (GroupOptionButton<int> button in _buttons)
            {
                bool selected = button.OptionValue == _currentIndex;
                button.SetCurrentOption(selected ? button.OptionValue : -1);
                if (selected)
                    button.SetColor(SelectedColor, 1f);
                else
                    button.SetColor(Colors.InventoryDefaultColor, 0.7f);
            }
        }
    }
}
