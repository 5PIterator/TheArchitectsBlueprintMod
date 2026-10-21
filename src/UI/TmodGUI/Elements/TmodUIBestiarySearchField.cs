using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;
using TheArchitectsBlueprintMod.UI.Style;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Bestiary-style search field with decorative icon prefix and clear button.</summary>
    public sealed class TmodUIBestiarySearchField : UIElement
    {
        private static readonly Color FieldColor = new(35, 40, 83);

        private readonly UIPanel _fieldPanel;
        private readonly UISearchBar _searchBar;
        private readonly LocalizedText _placeholder;
        private readonly UIImageButton _clearButton;
        private string _normalizedContents = string.Empty;

        public TmodUIBestiarySearchField(
            LocalizedText placeholder,
            float width,
            float height = Theme.BrowserToolbarButtonSize)
        {
            // --- Build one clickable field: decorative search icon, text, and clear.
            _placeholder = placeholder;
            Width.Set(width, 0f);
            Height.Set(height, 0f);
            SetPadding(0f);

            AssetTextureSize searchSize = MeasureTexture(TmodUIAssets.Bestiary.ButtonSearchPath);
            float iconWidth = searchSize.Width;
            float iconLeftPad = 4f;
            float textLeft = iconLeftPad + iconWidth + 4f;

            _fieldPanel = new UIPanel
            {
                BackgroundColor = FieldColor,
                BorderColor = FieldColor,
                Width = new StyleDimension(0f, 1f),
                Height = new StyleDimension(0f, 1f),
                VAlign = 0.5f
            };
            _fieldPanel.SetPadding(0f);
            _fieldPanel.OnLeftClick += (_, _) => ToggleTakingText();
            Append(_fieldPanel);

            var searchIcon = new UIImage(TmodUIAssets.RequestTexture(TmodUIAssets.Bestiary.ButtonSearchPath))
            {
                IgnoresMouseInteraction = true,
                Left = new StyleDimension(iconLeftPad, 0f),
                VAlign = 0.5f
            };
            _fieldPanel.Append(searchIcon);

            _searchBar = new UISearchBar(placeholder, 0.8f)
            {
                Left = new StyleDimension(textLeft, 0f),
                Width = new StyleDimension(-(textLeft + 22f), 1f),
                Height = new StyleDimension(0f, 1f),
                VAlign = 0.5f,
                IgnoresMouseInteraction = true
            };
            _searchBar.OnContentsChanged += OnSearchContentsChanged;
            _searchBar.OnStartTakingInput += OnStartTakingInput;
            _searchBar.OnEndTakingInput += OnEndTakingInput;
            _fieldPanel.Append(_searchBar);

            _clearButton = new UIImageButton(TmodUIAssets.RequestTexture(TmodUIAssets.Shared.SearchCancelPath))
            {
                HAlign = 1f,
                VAlign = 0.5f,
                Left = new StyleDimension(-2f, 0f)
            };
            _clearButton.OnLeftClick += OnClearClicked;
            _clearButton.OnMouseOver += (_, _) => SoundEngine.PlaySound(SoundID.MenuTick);
            _fieldPanel.Append(_clearButton);
        }

        public event Action<string>? ContentsChanged;

        public event Action? StartTakingInput;

        public event Action? EndTakingInput;

        public bool IsWritingText => _searchBar.IsWritingText;

        public bool HasContents => _normalizedContents.Length > 0;

        /// <summary>Toggles whether the search bar captures keyboard input.</summary>
        public void ToggleTakingText()
        {
            // --- Delegate to the vanilla search bar toggle.
            _searchBar.ToggleTakingText();
        }

        /// <summary>Replaces the current search contents.</summary>
        public void SetContents(string? text, bool forced = false)
        {
            // --- Forward to the vanilla search bar and refresh local normalized text.
            _searchBar.SetContents(text, forced);
            _normalizedContents = TmodSearchField.NormalizeContents(text, _placeholder);
        }

        /// <summary>Returns normalized search text without the placeholder.</summary>
        public string GetNormalizedContents() => _normalizedContents;

        private void OnSearchContentsChanged(string contents)
        {
            // --- Normalize placeholder, track emptiness, and notify listeners.
            _normalizedContents = TmodSearchField.NormalizeContents(contents, _placeholder);
            ContentsChanged?.Invoke(_normalizedContents);
        }

        private void OnStartTakingInput()
        {
            // --- Highlight the field border while typing.
            _fieldPanel.BorderColor = Main.OurFavoriteColor;
            StartTakingInput?.Invoke();
        }

        private void OnEndTakingInput()
        {
            // --- Restore the idle field border color.
            _fieldPanel.BorderColor = FieldColor;
            EndTakingInput?.Invoke();
        }

        private void OnClearClicked(UIMouseEvent evt, UIElement listeningElement)
        {
            // --- Clear text when present; otherwise play the empty-cancel tick.
            if (HasContents)
            {
                SetContents(null, forced: true);
                SoundEngine.PlaySound(SoundID.MenuClose);
                ContentsChanged?.Invoke(string.Empty);
            }
            else
            {
                SoundEngine.PlaySound(SoundID.MenuTick);
            }
        }

        private readonly record struct AssetTextureSize(float Width, float Height);

        /// <summary>Reads width/height from a requested UI texture asset.</summary>
        private static AssetTextureSize MeasureTexture(string path)
        {
            // --- Request the asset and fall back to toolbar button size if unloaded.
            Texture2D? texture = TmodUIAssets.RequestTexture(path).Value;
            if (texture == null)
                return new AssetTextureSize(Theme.BrowserToolbarButtonSize, Theme.BrowserToolbarButtonSize);

            return new AssetTextureSize(texture.Width, texture.Height);
        }
    }
}
