using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.Localization;
using Terraria.UI;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Project name label/editor row; toggles in place without rebuilding the preview stack.</summary>
    internal sealed class PayloadProjectNameRow : UIElement
    {
        private static readonly Color SearchFieldBackground = new(35, 40, 83);

        private readonly UIElement _nameSlot;
        private readonly UIPanel _labelPanel;
        private readonly UIText _label;
        private readonly UIPanel _editorPanel;
        private readonly UISearchBar _editor;
        private readonly LocalizedText _editorPlaceholder;
        private bool _editing;
        private string _displayName = string.Empty;
        private string _editingName = string.Empty;

        public PayloadProjectNameRow(PayloadPreviewBuildContext ctx)
        {
            OverflowHidden = true;
            Height.Set(TmodTheme.TextFieldHeight, 0f);
            Width.Set(0f, 1f);

            float btn = TmodTheme.TextFieldHeight;
            var editButton = new TmodUIImageButton(
                btn,
                btn,
                TmodFrameStyle.Icon(TmodUIAssets.Shared.ButtonRenamePath, TmodUIAssets.Bestiary.ButtonBorderPath),
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.EditProjectName"),
                ctx.ToggleProjectNameEdit);
            editButton.VAlign = 0.5f;
            Append(editButton);

            _nameSlot = new UIElement { OverflowHidden = true };
            _nameSlot.Left.Set(btn, 0f);
            _nameSlot.Width.Set(-btn, 1f);
            _nameSlot.Height.Set(0f, 1f);
            Append(_nameSlot);

            _labelPanel = new UIPanel
            {
                BackgroundColor = SearchFieldBackground,
                BorderColor = SearchFieldBackground,
                Width = new StyleDimension(0f, 1f),
                Height = new StyleDimension(0f, 1f)
            };
            _labelPanel.SetPadding(6f);
            _label = new UIText(string.Empty, 0.9f)
            {
                TextColor = TmodTheme.PrimaryText,
                HAlign = 0f,
                VAlign = 0.5f,
                TextOriginX = 0f,
                TextOriginY = 0.5f,
                IgnoresMouseInteraction = true
            };
            _labelPanel.Append(_label);
            _nameSlot.Append(_labelPanel);

            _editorPlaceholder = Language.GetText("Mods.TheArchitectsBlueprintMod.UI.EditProjectName");
            _editorPanel = new UIPanel
            {
                BackgroundColor = SearchFieldBackground,
                BorderColor = SearchFieldBackground,
                Width = new StyleDimension(0f, 1f),
                Height = new StyleDimension(0f, 1f)
            };
            _editorPanel.SetPadding(0f);

            _editor = new UISearchBar(_editorPlaceholder, 0.9f)
            {
                Width = new StyleDimension(0f, 1f),
                Height = new StyleDimension(0f, 1f),
                IgnoresMouseInteraction = true
            };
            _editor.OnContentsChanged += value => _editingName = TmodSearchField.NormalizeContents(value, _editorPlaceholder);
            _editorPanel.OnLeftClick += (_, _) => _editor.ToggleTakingText();
            _editorPanel.Append(_editor);
            _nameSlot.Append(_editorPanel);

            SetEditing(ctx.ProjectNameEditing, force: true);
        }

        public void ApplyState(string projectName, PayloadPreviewBuildContext ctx)
        {
            if (!ctx.ProjectNameEditing)
            {
                _displayName = projectName ?? string.Empty;
                if (!_editing)
                    _label.SetText(_displayName);
            }

            if (ctx.ProjectNameEditing != _editing)
                SetEditing(ctx.ProjectNameEditing, force: false);
            else if (_editing)
                SyncEditorContents();
        }

        public string GetCommittedName() => (_displayName ?? string.Empty).Trim();

        public string GetEditingName() => (_editingName ?? string.Empty).Trim();

        public void CommitEditing()
        {
            _displayName = GetEditingName();
            SetEditing(false, force: false);
        }

        public void BeginEditing()
        {
            _editingName = _displayName;
            SetEditing(true, force: false);
        }

        public void EndTextEntry()
        {
            if (_editor.IsWritingText)
                _editor.ToggleTakingText();
        }

        public bool IsCapturingTextInput => _editor.IsWritingText;

        private void SetEditing(bool editing, bool force)
        {
            if (!force && editing == _editing)
                return;

            _editing = editing;
            _nameSlot.RemoveAllChildren();
            if (_editing)
            {
                SyncEditorContents();
                _nameSlot.Append(_editorPanel);
            }
            else
            {
                if (_editor.IsWritingText)
                    _editor.ToggleTakingText();
                _label.SetText(_displayName);
                _nameSlot.Append(_labelPanel);
            }
        }

        private void SyncEditorContents()
        {
            _editor.SetContents(_editingName ?? string.Empty, forced: true);
        }
    }
}
