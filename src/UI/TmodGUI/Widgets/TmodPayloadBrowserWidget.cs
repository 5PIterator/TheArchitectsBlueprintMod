using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.Localization;
using Terraria.ModLoader.UI.Elements;
using Terraria.UI;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.Preview;
using TheArchitectsBlueprintMod.UI.TmodGUI.Elements;
using TheArchitectsBlueprintMod.UI.Style;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Widgets
{
    /// <summary>Unified non-modal payload browser window for preview/import/export.</summary>
    public sealed class TmodPayloadBrowserWidget : TmodWidgetBase
    {
        private readonly PayloadBrowser _browser;
        private readonly List<Texture2D> _gridThumbnails = [];

        private TmodWindowPanel _window = null!;
        private UIElement _entryScrollHost = null!;
        private UIGrid _entryGrid = null!;
        private UIScrollbar _scrollbar = null!;
        private TmodUILabel _emptyEntriesLabel = null!;
        private UIList _previewList = null!;
        private TmodUIBestiarySearchField _searchField = null!;
        private TmodUICycleButton _sortButton = null!;
        private TmodUICycleButton _filterButton = null!;
        private TmodUITextActionButton _refreshButton = null!;
        private UIElement _browserPanel = null!;
        private TmodUITextOptionsPopup? _sortOptionsPopup;
        private TmodUITextOptionsPopup? _filterOptionsPopup;
        private TmodUITextActionButton _revertButton = null!;
        private TmodUITextActionButton _applyButton = null!;
        private TmodUITextActionButton _exportButton = null!;
        private TmodUICycleButton _exportFormatButton = null!;
        private TmodUILabel _statusLabel = null!;

        private Texture2D? _payloadThumbnail;
        private Projection? _activeProjection;
        private int _lastPanelRevision = -1;
        private bool _projectNameEditing;
        private string _committedProjectName = string.Empty;
        private PayloadProjectNameRow? _projectNameRow;
        private LocalizedText _searchPlaceholder = null!;
        private readonly List<PayloadFileEntry> _entryElements = [];

        private TmodPayloadBrowserWidget(PayloadBrowser browser) => _browser = browser;

        public static void Open(Projection? projection = null)
        {
            if (Main.dedServ)
                return;

            if (TmodGUI.GetActiveWidget() is TmodPayloadBrowserWidget)
                TmodGUI.Close();

            if (PayloadBrowser.Open(projection) is not PayloadBrowser browser)
                return;

            var widget = new TmodPayloadBrowserWidget(browser)
            {
                OnClosed = () => PayloadBrowser.Close(commitActivePreview: false)
            };
            TmodGUI.Show(widget);
        }

        public override void OnDeactivate()
        {
            _projectNameEditing = false;
            _projectNameRow?.EndTextEntry();
            CloseOptionsPopups();
            if (_searchField != null && _searchField.IsWritingText)
                _searchField.ToggleTakingText();
            ReleaseThumbnailTextures();
            base.OnDeactivate();
        }

        private void ReleaseThumbnailTextures()
        {
            DisposeGridThumbnails();

            if (_payloadThumbnail != null)
            {
                DisposeGpuTexture(_payloadThumbnail);
                _payloadThumbnail = null;
            }
        }

        /// <summary>Disposes textures owned by the entry grid.</summary>
        private void DisposeGridThumbnails()
        {
            // --- Release every tracked grid thumbnail, then clear the list.
            for (int i = 0; i < _gridThumbnails.Count; i++)
                DisposeGpuTexture(_gridThumbnails[i]);
            _gridThumbnails.Clear();
        }

        private static void DisposeGpuTexture(Texture2D texture)
        {
            try
            {
                texture.Dispose();
            }
            catch (System.Threading.ThreadStateException)
            {
            }
        }

        protected override void Build()
        {
            _window = new TmodWindowPanel();
            TmodLayout.CenterInParent(_window, Theme.BrowserWidth, Theme.BrowserHeight);
            Append(_window);

            var toolbarPanel = TmodLayout.CreateHorizontalStack();
            toolbarPanel.OverflowHidden = true;
            TmodLayout.FillWidth(toolbarPanel, Theme.BrowserToolbarHeight, 0f);
            toolbarPanel.OnLeftMouseDown += OnToolbarMouseDown;
            _window.Append(toolbarPanel);

            _sortButton = new TmodUICycleButton(
                GetSortValue(),
                GetSortHover(),
                ToggleSortOptions,
                TmodUIAssets.Bestiary.ButtonSortingPath);
            toolbarPanel.Append(_sortButton);

            _filterButton = new TmodUICycleButton(
                GetFilterValue(),
                GetFilterHover(),
                ToggleFilterOptions,
                TmodUIAssets.Bestiary.ButtonFilteringPath);
            toolbarPanel.Append(_filterButton);

            float closeSize = MeasureTextureSize(TmodUIAssets.Shared.SearchCancelPath).Width;
            float searchWidth = Math.Max(
                120f,
                Theme.BrowserWidth
                    - Theme.Padding * 2f
                    - _sortButton.Width.Pixels
                    - _filterButton.Width.Pixels
                    - closeSize
                    - Theme.Spacing * 3f);

            _searchPlaceholder = Language.GetText("Mods.TheArchitectsBlueprintMod.UI.SearchEntries");
            _searchField = new TmodUIBestiarySearchField(
                _searchPlaceholder,
                searchWidth,
                Theme.BrowserToolbarHeight);
            _searchField.ContentsChanged += text =>
            {
                _browser.SetSearch(text);
                RebuildEntryElements();
            };
            _searchField.StartTakingInput += () =>
            {
                if (_projectNameEditing)
                {
                    _projectNameEditing = false;
                    _projectNameRow?.CommitEditing();
                    _committedProjectName = _projectNameRow?.GetCommittedName() ?? _committedProjectName;
                }
            };
            toolbarPanel.Append(_searchField);

            var closeButton = new TmodUIImageButton(
                closeSize,
                closeSize,
                TmodFrameStyle.Icon(
                    TmodUIAssets.Shared.SearchCancelPath,
                    TmodUIAssets.Bestiary.ButtonBorderPath,
                    inactiveAlpha: 1f),
                "X",
                RequestClose);
            toolbarPanel.Append(closeButton);

            float contentTop = Theme.BrowserToolbarHeight;
            float contentHeight = Theme.BrowserContentHeight;

            _browserPanel = new UIElement { OverflowHidden = true };
            TmodLayout.AnchorTopLeft(_browserPanel, 0f, contentTop, Theme.BrowserEntryPanelWidth, contentHeight);
            _window.Append(_browserPanel);

            _entryScrollHost = new UIElement { OverflowHidden = true };
            TmodLayout.AnchorTopLeft(
                _entryScrollHost,
                Theme.BrowserScrollbarWidth,
                0f,
                Theme.BrowserGridInnerWidth,
                contentHeight);
            _browserPanel.Append(_entryScrollHost);

            _entryGrid = new UIGrid
            {
                OverflowHidden = true,
                ListPadding = Theme.Spacing
            };
            _entryGrid.Width.Set(0f, 1f);
            _entryGrid.Height.Set(0f, 1f);
            _entryScrollHost.Append(_entryGrid);

            _scrollbar = new UIScrollbar();
            _scrollbar.Width.Set(Theme.BrowserScrollbarWidth, 0f);
            _scrollbar.Top.Set(Theme.BrowserScrollbarInset, 0f);
            _scrollbar.Height.Set(-Theme.BrowserScrollbarInset * 2f, 1f);
            _scrollbar.HAlign = 0f;
            _browserPanel.Append(_scrollbar);
            _entryGrid.SetScrollbar(_scrollbar);

            _emptyEntriesLabel = new TmodUILabel(
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.NoPayloadEntries"),
                textScale: 0.85f,
                secondary: true);
            TmodLayout.AnchorTopLeft(
                _emptyEntriesLabel,
                Theme.BrowserScrollbarWidth,
                24f,
                Theme.BrowserGridInnerWidth,
                20f);
            _emptyEntriesLabel.IgnoresMouseInteraction = true;
            _browserPanel.Append(_emptyEntriesLabel);

            float payloadLeft = Theme.BrowserEntryPanelWidth;
            var payloadPanel = new UIPanel
            {
                BackgroundColor = Theme.InfoPageBackground,
                BorderColor = Theme.InfoPageBorder,
                OverflowHidden = true
            };
            payloadPanel.SetPadding(0f);
            TmodLayout.AnchorTopLeft(payloadPanel, payloadLeft, contentTop, Theme.BrowserPayloadPanelWidth, contentHeight);
            _window.Append(payloadPanel);

            _previewList = new UIList
            {
                Width = StyleDimension.FromPixelsAndPercent(0f, 1f),
                Height = StyleDimension.FromPixelsAndPercent(0f, 1f),
                ListPadding = 4f,
                PaddingBottom = 4f,
                PaddingTop = 4f
            };
            _previewList.SetPadding(2f);
            payloadPanel.Append(_previewList);

            var infoScrollbar = new UIScrollbar();
            infoScrollbar.SetView(100f, 1000f);
            infoScrollbar.Height.Set(-20f, 1f);
            infoScrollbar.HAlign = 1f;
            infoScrollbar.VAlign = 0.5f;
            infoScrollbar.Left.Set(-6f, 0f);
            payloadPanel.Append(infoScrollbar);
            _previewList.SetScrollbar(infoScrollbar);
            _previewList.Width.Set(-20f, 1f);

            var borderOverlay = new UIPanel
            {
                Width = new StyleDimension(0f, 1f),
                Height = new StyleDimension(0f, 1f),
                IgnoresMouseInteraction = true,
                BackgroundColor = Color.Transparent,
                BorderColor = Theme.InfoPageBorder
            };
            payloadPanel.Append(borderOverlay);

            _committedProjectName = PayloadBrowser.BuildDefaultProjectName();

            float footerTop = contentTop + contentHeight + Theme.Spacing;
            var footerPanel = TmodLayout.CreateHorizontalStack(crossAxisCenter: true);
            footerPanel.OverflowHidden = true;
            TmodLayout.AnchorTopLeft(
                footerPanel,
                0f,
                footerTop,
                Theme.BrowserWidth - Theme.Padding * 2f,
                Theme.BrowserFooterHeight);
            _window.Append(footerPanel);

            _refreshButton = new TmodUITextActionButton(
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.RefreshEntries"),
                ReloadEntries,
                Theme.BrowserFooterButtonWidth,
                Theme.BrowserFooterButtonHeight,
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.RefreshEntries"));
            footerPanel.Append(_refreshButton);

            _revertButton = new TmodUITextActionButton(
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.BrowserRevert"),
                OnCancelPreview,
                Theme.BrowserFooterButtonWidth,
                Theme.BrowserFooterButtonHeight,
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.BrowserRevertHover"));
            footerPanel.Append(_revertButton);

            _applyButton = new TmodUITextActionButton(
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.BrowserApply"),
                OnImportCommit,
                Theme.BrowserFooterButtonWidth,
                Theme.BrowserFooterButtonHeight,
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.BrowserApplyHover"));
            footerPanel.Append(_applyButton);

            footerPanel.AppendGrow();

            _exportFormatButton = new TmodUICycleButton(
                GetExportFormatValue(),
                GetExportFormatHover(),
                ToggleExportFormat,
                Theme.BrowserFooterFormatWidth,
                Theme.BrowserFooterButtonHeight);
            footerPanel.Append(_exportFormatButton);

            _exportButton = new TmodUITextActionButton(
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.BrowserExport"),
                OnExport,
                Theme.BrowserFooterButtonWidth,
                Theme.BrowserFooterButtonHeight,
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.BrowserExportHover"));
            footerPanel.Append(_exportButton);

            _statusLabel = new TmodUILabel(string.Empty, textScale: 0.75f, secondary: true);
            TmodLayout.AnchorTopLeft(
                _statusLabel,
                0f,
                footerTop + Theme.BrowserFooterHeight,
                Theme.BrowserWidth - Theme.Padding * 2f,
                Theme.BrowserStatusHeight);
            _window.Append(_statusLabel);

            _window.IsTextInputActive = () =>
                _searchField.IsWritingText || (_projectNameRow?.IsCapturingTextInput ?? false);

            RebuildEntryElements();
            RefreshPayloadSection(force: true);
            RefreshActionAvailability();
        }

        private void OnToolbarMouseDown(UIMouseEvent evt, UIElement listeningElement)
        {
            if (IsToolbarInteractiveTarget(evt.Target))
                return;

            _window.BeginDrag(evt);
        }

        private bool IsToolbarInteractiveTarget(UIElement? target)
        {
            for (UIElement? element = target; element != null; element = element.Parent)
            {
                if (element == _searchField
                    || element is TmodFramedControl
                    or TmodUIBestiarySearchField
                    or UISearchBar
                    or UITextBox
                    or UIImageButton)
                {
                    return true;
                }
            }

            return false;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Main.keyState.IsKeyDown(Keys.Escape) && !Main.oldKeyState.IsKeyDown(Keys.Escape))
            {
                if (_projectNameEditing)
                {
                    _projectNameEditing = false;
                    _projectNameRow?.CancelEditing();
                    RefreshPayloadSection(force: true);
                    return;
                }

                RequestClose();
                return;
            }

            _browser.SyncActiveProjectionFromTopmost();
            RefreshPayloadSection(force: false);
            RefreshActionAvailability();
            RefreshEntrySelectionVisuals();
        }

        /// <summary>Enables footer actions from the current browser capabilities.</summary>
        private void RefreshActionAvailability()
        {
            // --- Mirror browser capability flags onto footer buttons.
            _revertButton.Enabled = _browser.CanCancelPreview;
            _applyButton.Enabled = _browser.CanCommitImport;
            _exportButton.Enabled = _browser.CanExport;
        }

        /// <summary>Keeps grid selection glow in sync with the pending preview path.</summary>
        private void RefreshEntrySelectionVisuals()
        {
            // --- Ask each entry cell whether its path is the active preview.
            for (int i = 0; i < _entryElements.Count; i++)
                _entryElements[i].RefreshSelection(_browser);
        }

        private void ToggleSortOptions()
        {
            if (_sortOptionsPopup?.Parent != null)
            {
                CloseOptionsPopups();
                return;
            }

            CloseOptionsPopups();
            _sortOptionsPopup = new TmodUITextOptionsPopup(
                BuildSortOptions(),
                (int)_browser.SortMode,
                index =>
                {
                    _browser.SetSort((PayloadSortMode)index);
                    _sortButton.SetValue(GetSortValue());
                    RebuildEntryElements();
                },
                anchorLeftPixels: Theme.BrowserOptionsPopupAnchorLeft);
            _sortOptionsPopup.OnRequestClose += CloseOptionsPopups;
            _browserPanel.Append(_sortOptionsPopup);
        }

        private void ToggleFilterOptions()
        {
            if (_filterOptionsPopup?.Parent != null)
            {
                CloseOptionsPopups();
                return;
            }

            CloseOptionsPopups();
            _filterOptionsPopup = new TmodUITextOptionsPopup(
                BuildFilterOptions(),
                (int)_browser.FilterMode,
                index =>
                {
                    _browser.SetFilter((PayloadFilterMode)index);
                    _filterButton.SetValue(GetFilterValue());
                    RebuildEntryElements();
                },
                anchorLeftPixels: Theme.BrowserOptionsPopupAnchorLeft);
            _filterOptionsPopup.OnRequestClose += CloseOptionsPopups;
            _browserPanel.Append(_filterOptionsPopup);
        }

        /// <summary>Cycles export format between compact .tab and readable .json.</summary>
        private void ToggleExportFormat()
        {
            // --- Flip the browser flag and refresh the cycle button label/hover.
            _browser.ExportAsReadableJson = !_browser.ExportAsReadableJson;
            _exportFormatButton.SetValue(GetExportFormatValue());
            _exportFormatButton.HoverText = GetExportFormatHover();
        }

        private void CloseOptionsPopups()
        {
            if (_sortOptionsPopup != null)
            {
                _browserPanel.RemoveChild(_sortOptionsPopup);
                _sortOptionsPopup = null;
            }

            if (_filterOptionsPopup != null)
            {
                _browserPanel.RemoveChild(_filterOptionsPopup);
                _filterOptionsPopup = null;
            }
        }

        private static List<(int optionIndex, LocalizedText label)> BuildSortOptions()
        {
            var options = new List<(int, LocalizedText)>();
            foreach (PayloadSortMode mode in Enum.GetValues<PayloadSortMode>())
                options.Add(((int)mode, Language.GetText($"Mods.TheArchitectsBlueprintMod.UI.Sort.{mode}")));

            return options;
        }

        private static List<(int optionIndex, LocalizedText label)> BuildFilterOptions()
        {
            var options = new List<(int, LocalizedText)>();
            foreach (PayloadFilterMode mode in Enum.GetValues<PayloadFilterMode>())
                options.Add(((int)mode, Language.GetText($"Mods.TheArchitectsBlueprintMod.UI.Filter.{mode}")));

            return options;
        }

        private string GetSortValue() =>
            Language.GetTextValue($"Mods.TheArchitectsBlueprintMod.UI.Sort.{_browser.SortMode}");

        private string GetSortHover() => $"Sort: {GetSortValue()}";

        private string GetFilterValue() =>
            Language.GetTextValue($"Mods.TheArchitectsBlueprintMod.UI.Filter.{_browser.FilterMode}");

        private string GetFilterHover() => $"Filter: {GetFilterValue()}";

        private string GetExportFormatValue() =>
            Language.GetTextValue(
                _browser.ExportAsReadableJson
                    ? "Mods.TheArchitectsBlueprintMod.UI.ExportFormatReadableJson"
                    : "Mods.TheArchitectsBlueprintMod.UI.ExportFormatCompactTab");

        private string GetExportFormatHover() =>
            Language.GetTextValue(
                _browser.ExportAsReadableJson
                    ? "Mods.TheArchitectsBlueprintMod.UI.ExportReadableJsonHoverOn"
                    : "Mods.TheArchitectsBlueprintMod.UI.ExportReadableJsonHoverOff");

        private void ReloadEntries()
        {
            _browser.ReloadEntries();
            RebuildEntryElements();
            SetStatus(Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.BrowserStatusRefreshed"));
        }

        private void RebuildEntryElements()
        {
            DisposeGridThumbnails();
            _entryElements.Clear();
            _entryGrid.Clear();
            for (int i = 0; i < _browser.EntryCount; i++)
            {
                PayloadImportListEntry entry = _browser.Entries[i];
                var element = new PayloadFileEntry(owner: this, entry: entry);
                _entryElements.Add(element);
                _entryGrid.Add(element);
            }

            _entryGrid.Recalculate();

            float viewHeight = _entryScrollHost.GetInnerDimensions().Height;
            float gridHeight = _entryGrid.GetTotalHeight();
            _scrollbar.SetView(viewHeight, Math.Max(viewHeight, gridHeight));

            _emptyEntriesLabel.SetText(
                _browser.EntryCount == 0
                    ? Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.NoPayloadEntries")
                    : string.Empty,
                0.85f,
                false);

            RefreshEntrySelectionVisuals();
        }

        private void RefreshPayloadSection(bool force)
        {
            if (!force && _lastPanelRevision == _browser.PanelRevision)
                return;

            _lastPanelRevision = _browser.PanelRevision;

            if (!_browser.TryBuildPanelState(out PayloadBrowserPanelState panel))
            {
                _activeProjection = null;
                ApplyPayloadPanel(PayloadBrowser.EmptyPanelState);
                return;
            }

            _browser.TryGetActiveProjection(out Projection? projection);
            _activeProjection = projection;
            ApplyPayloadPanel(panel);
        }

        private void ApplyPayloadPanel(PayloadBrowserPanelState panel)
        {
            // Adopt catalog entry names when a preview is active; keep user export name otherwise.
            if (!_projectNameEditing && _browser.HasPendingPreview)
                _committedProjectName = panel.ProjectName;

            if (_payloadThumbnail != null)
            {
                DisposeGpuTexture(_payloadThumbnail);
                _payloadThumbnail = null;
            }

            if (panel.EntryThumbnailPng is { Length: > 0 } previewThumb)
            {
                _payloadThumbnail = PayloadTagIO.TryDecodeThumbnailPng(previewThumb);
            }
            else if (panel.MakeThumbnailFromPayload && _activeProjection is { HasPayload: true } projection)
            {
                _payloadThumbnail = PayloadPreview.MakeThumbnail(
                    projection.Payload,
                    (int)Theme.BrowserPayloadThumbnailSize);
            }

            _projectNameRow = PayloadPreviewLayouts.Rebuild(
                _previewList,
                panel,
                CreatePreviewContext(),
                _projectNameRow);
        }

        private PayloadPreviewBuildContext CreatePreviewContext() =>
            new()
            {
                DisplayProjectName = _committedProjectName,
                ProjectNameEditing = _projectNameEditing,
                ThumbnailTexture = _payloadThumbnail,
                ToggleProjectNameEdit = ToggleProjectNameEdit
            };

        private void ToggleProjectNameEdit()
        {
            if (_projectNameEditing)
            {
                // Commit typed text into the sticky export name, then restore the label.
                _projectNameRow?.CommitEditing();
                string committed = _projectNameRow?.GetCommittedName() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(committed))
                    _committedProjectName = committed;
                _projectNameEditing = false;
                RefreshPayloadSection(force: true);
                return;
            }

            _projectNameEditing = true;
            _projectNameRow?.BeginEditing();
            RefreshPayloadSection(force: true);
        }

        private string GetExportProjectName()
        {
            if (_projectNameEditing)
                return _projectNameRow?.GetEditingName() ?? string.Empty;

            return _committedProjectName;
        }

        private void OnCancelPreview()
        {
            if (!_browser.CanCancelPreview)
                return;

            _browser.CancelPreview();
            RefreshPayloadSection(force: true);
            RefreshEntrySelectionVisuals();
            SetStatus(Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.BrowserStatusReverted"));
        }

        private void OnEntrySelected(PayloadImportListEntry entry)
        {
            if (!_browser.TrySelectEntry(entry, out string? error))
            {
                string message = string.IsNullOrWhiteSpace(error)
                    ? Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.BrowserStatusPreviewFailed")
                    : error;
                Main.NewText($"Import preview failed: {message}", Color.OrangeRed);
                SetStatus(message);
                return;
            }

            RefreshPayloadSection(force: true);
            RefreshEntrySelectionVisuals();
            SetStatus(Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.BrowserStatusPreviewReady"));
        }

        private void OnImportCommit()
        {
            if (!_browser.CommitImport(out string? error))
            {
                string message = string.IsNullOrWhiteSpace(error)
                    ? Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.BrowserStatusApplyFailed")
                    : error;
                Main.NewText(message, Color.OrangeRed);
                SetStatus(message);
                return;
            }

            Main.NewText("Import applied to projection.", Color.LightGreen);
            RefreshPayloadSection(force: true);
            RefreshEntrySelectionVisuals();
            SetStatus(Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.BrowserStatusApplied"));
        }

        private void OnExport()
        {
            string projectName = GetExportProjectName();
            if (!_browser.Export(projectName, out string? error))
            {
                string message = string.IsNullOrWhiteSpace(error)
                    ? Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.BrowserStatusExportFailed")
                    : error;
                Main.NewText($"Export failed: {message}", Color.OrangeRed);
                SetStatus(message);
                return;
            }

            _projectNameEditing = false;
            _projectNameRow?.EndTextEntry();
            _committedProjectName = PayloadBrowser.BuildDefaultProjectName();
            if (_searchField.IsWritingText)
                _searchField.ToggleTakingText();
            RebuildEntryElements();
            RefreshPayloadSection(force: true);
            SetStatus(Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.BrowserStatusExported"));
        }

        /// <summary>Updates the in-window status line.</summary>
        private void SetStatus(string message)
        {
            // --- Replace the secondary status label text.
            _statusLabel.SetText(message ?? string.Empty, 0.75f, false);
        }

        internal void TrackGridThumbnail(Texture2D texture) => _gridThumbnails.Add(texture);

        private readonly record struct TextureSize(float Width, float Height);

        /// <summary>Measures a UI texture asset, falling back to toolbar button size.</summary>
        private static TextureSize MeasureTextureSize(string path)
        {
            // --- Read the loaded texture dimensions for layout math.
            Texture2D? texture = TmodUIAssets.RequestTexture(path).Value;
            if (texture == null)
                return new TextureSize(Theme.BrowserToolbarButtonSize, Theme.BrowserToolbarButtonSize);

            return new TextureSize(texture.Width, texture.Height);
        }

        private sealed class PayloadFileEntry : UIElement
        {
            private readonly TmodPayloadBrowserWidget _owner;
            private readonly PayloadImportListEntry _entry;
            private readonly PayloadEntryCell _cell;
            private bool _thumbnailLoaded;

            public PayloadFileEntry(TmodPayloadBrowserWidget owner, PayloadImportListEntry entry)
            {
                _owner = owner;
                _entry = entry;
                OverflowHidden = true;

                Width.Set(Theme.BrowserCompactCellSize, 0f);
                Height.Set(Theme.BrowserCompactCellSize, 0f);

                _cell = new PayloadEntryCell(
                    () => PayloadBrowser.BuildEntryHoverText(_entry),
                    () => _owner.OnEntrySelected(_entry));
                bool hasMissingMods = entry.ModsLoaded is { Count: > 0 }
                    && PayloadCompatibility.ComputeMissingMods(entry.ModsLoaded).Count > 0;
                _cell.SetStatus(entry.IsValid, entry.IsLegacy, hasMissingMods);
                Append(_cell);
            }

            /// <summary>Refreshes the selection glow from the browser preview path.</summary>
            public void RefreshSelection(PayloadBrowser browser)
            {
                // --- Mark this cell selected when its path matches the pending preview.
                _cell.SetSelected(browser.IsEntryPreviewed(_entry.FullPath));
            }

            public override void Update(GameTime gameTime)
            {
                base.Update(gameTime);
                TryLoadThumbnail();
            }

            private void TryLoadThumbnail()
            {
                if (_thumbnailLoaded || !_entry.IsValid)
                    return;

                _thumbnailLoaded = true;
                if (_entry.ThumbnailPng is not { Length: > 0 })
                    return;

                Texture2D? texture = PayloadTagIO.TryDecodeThumbnailPng(_entry.ThumbnailPng);
                if (texture == null)
                    return;

                _cell.SetTexture(texture);
                _owner.TrackGridThumbnail(texture);
            }
        }
    }
}
