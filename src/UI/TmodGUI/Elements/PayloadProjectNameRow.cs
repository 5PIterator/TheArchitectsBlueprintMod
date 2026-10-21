using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.Localization;
using Terraria.UI;
using TheArchitectsBlueprintMod.UI.Style;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Bestiary nameplate with optional inline rename via search field.</summary>
    internal sealed class PayloadProjectNameRow : UIElement
    {
        private readonly UIElement _nameplateHost;
        private readonly UIText _label;
        private readonly TmodUIImageButton _editButton;
        private readonly LocalizedText _editorPlaceholder;
        private TmodUIBestiarySearchField? _editor;
        private bool _editing;
        private string _displayName = string.Empty;
        private string _editingName = string.Empty;

        public PayloadProjectNameRow(PayloadPreviewBuildContext ctx)
        {
            // --- Build idle nameplate + rename button; editor is created on demand.
            OverflowHidden = true;
            Height.Set(Theme.BrowserNameplateHeight, 0f);
            Width.Set(0f, 1f);
            _editorPlaceholder = Language.GetText("Mods.TheArchitectsBlueprintMod.UI.EditProjectName");

            float btn = Theme.BrowserToolbarButtonSize;
            _editButton = new TmodUIImageButton(
                btn,
                btn,
                TmodFrameStyle.Icon(
                    TmodUIAssets.Shared.ButtonRenamePath,
                    TmodUIAssets.Bestiary.ButtonBorderPath,
                    inactiveAlpha: 1f),
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.EditProjectName"),
                ctx.ToggleProjectNameEdit);
            _editButton.HAlign = 1f;
            _editButton.VAlign = 0.5f;
            Append(_editButton);

            _nameplateHost = new UIElement { OverflowHidden = true };
            _nameplateHost.Width.Set(-btn - 4f, 1f);
            _nameplateHost.Height.Set(0f, 1f);
            Append(_nameplateHost);

            _label = new UIText(string.Empty, 0.9f)
            {
                TextColor = Theme.PrimaryText,
                HAlign = 0.5f,
                VAlign = 0.5f,
                Top = new StyleDimension(2f, 0f),
                TextOriginX = 0.5f,
                TextOriginY = 0.5f,
                IgnoresMouseInteraction = true
            };
            _nameplateHost.Append(_label);

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

        /// <summary>Cancels edit and restores the last committed display name.</summary>
        public void CancelEditing()
        {
            // --- Drop in-progress text and return to the nameplate.
            _editingName = _displayName;
            SetEditing(false, force: false);
        }

        public void EndTextEntry()
        {
            if (_editor?.IsWritingText == true)
                _editor.ToggleTakingText();
        }

        public bool IsCapturingTextInput => _editor?.IsWritingText ?? false;

        private void SetEditing(bool editing, bool force)
        {
            if (!force && editing == _editing)
                return;

            _editing = editing;
            _nameplateHost.RemoveAllChildren();

            if (_editing)
            {
                EnsureEditor();
                SyncEditorContents();
                _nameplateHost.Append(_editor!);
                _editButton.HoverText = Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.EditProjectName");
            }
            else
            {
                if (_editor?.IsWritingText == true)
                    _editor.ToggleTakingText();
                _label.SetText(_displayName);
                _nameplateHost.Append(_label);
            }
        }

        private void EnsureEditor()
        {
            if (_editor != null)
                return;

            _editor = new TmodUIBestiarySearchField(
                _editorPlaceholder,
                width: 0f,
                height: Theme.BrowserNameplateHeight);
            _editor.Width.Set(0f, 1f);
            _editor.Height.Set(0f, 1f);
            _editor.ContentsChanged += value => _editingName = value;
            _editor.StartTakingInput += () => { };
        }

        private void SyncEditorContents()
        {
            EnsureEditor();
            _editor!.SetContents(_editingName ?? string.Empty, forced: true);
        }
    }
}
