using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.UI;
using Terraria.ModLoader.UI.Elements;
using Terraria.UI;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.UI.Preview;
using TheArchitectsBlueprintMod.UI.TmodGUI.Elements;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Widgets
{
    /// <summary>Unified non-modal payload browser window for preview/import/export.</summary>
    public sealed class TmodPayloadBrowserWidget : TmodWidgetBase
    {
        private readonly PayloadBrowser _browser;
        private readonly List<Texture2D> _decodedThumbnails = [];

        private TmodWindowPanel _window = null!;
        private UIElement _entryScrollHost = null!;
        private UIGrid _entryGrid = null!;
        private UIScrollbar _scrollbar = null!;
        private TmodUILabel _emptyEntriesLabel = null!;
        private TmodStackPanel _previewStack = null!;
        private UIPanel _searchBoxPanel = null!;
        private UISearchBar _searchBar = null!;
        private TmodUICycleButton _sortButton = null!;
        private TmodUICycleButton _filterButton = null!;
        private TmodUIImageButton _refreshButton = null!;
        private TmodUICheckbox _exportJsonCheckbox = null!;
        private UIElement _browserPanel = null!;
        private TmodUITextOptionsPopup? _sortOptionsPopup;
        private TmodUITextOptionsPopup? _filterOptionsPopup;

        private Texture2D? _payloadThumbnail;
        private Projection? _activeProjection;
        private int _lastPanelRevision = -1;
        private bool _projectNameEditing;
        private string _committedProjectName = string.Empty;
        private PayloadProjectNameRow? _projectNameRow;
        private LocalizedText _searchPlaceholder = null!;

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
            if (_searchBar != null && _searchBar.IsWritingText)
                _searchBar.ToggleTakingText();
            ReleaseThumbnailTextures();
            base.OnDeactivate();
        }

        private void ReleaseThumbnailTextures()
        {
            for (int i = 0; i < _decodedThumbnails.Count; i++)
                DisposeGpuTexture(_decodedThumbnails[i]);
            _decodedThumbnails.Clear();

            if (_payloadThumbnail != null)
            {
                DisposeGpuTexture(_payloadThumbnail);
                _payloadThumbnail = null;
            }
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
            TmodLayout.CenterInParent(_window, TmodTheme.BrowserWidth, TmodTheme.BrowserHeight);
            Append(_window);

            const float toolbarSortWidth = 118f;
            const float toolbarFilterWidth = 108f;
            float btn = TmodTheme.BrowserToolbarButtonSize;

            var toolbarPanel = TmodLayout.CreateHorizontalStack();
            toolbarPanel.OverflowHidden = true;
            TmodLayout.FillWidth(toolbarPanel, TmodTheme.BrowserToolbarHeight, 0f);
            toolbarPanel.OnLeftMouseDown += OnToolbarMouseDown;
            _window.Append(toolbarPanel);

            _refreshButton = new TmodUIImageButton(
                btn,
                btn,
                TmodFrameStyle.Icon(TmodUIAssets.Bestiary.ButtonSearchPath),
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.RefreshEntries"),
                ReloadEntries);
            toolbarPanel.Append(_refreshButton);

            _sortButton = new TmodUICycleButton(
                GetSortValue(),
                GetSortHover(),
                ToggleSortOptions,
                toolbarSortWidth,
                btn,
                TmodUIAssets.Bestiary.ButtonSortingPath);
            toolbarPanel.Append(_sortButton);

            _filterButton = new TmodUICycleButton(
                GetFilterValue(),
                GetFilterHover(),
                ToggleFilterOptions,
                toolbarFilterWidth,
                btn,
                TmodUIAssets.Bestiary.ButtonFilteringPath);
            toolbarPanel.Append(_filterButton);

            toolbarPanel.AppendGrow();

            _searchBoxPanel = new UIPanel
            {
                BackgroundColor = new Color(35, 40, 83),
                BorderColor = new Color(35, 40, 83),
            };
            _searchBoxPanel.SetPadding(0f);
            TmodLayout.StackFixed(_searchBoxPanel, TmodTheme.BrowserSearchFieldWidth, TmodTheme.BrowserToolbarHeight);

            _searchPlaceholder = Language.GetText("Mods.TheArchitectsBlueprintMod.UI.SearchEntries");
            _searchBar = new UISearchBar(_searchPlaceholder, 0.8f)
            {
                Width = new StyleDimension(0f, 1f),
                Height = new StyleDimension(0f, 1f),
                IgnoresMouseInteraction = true,
            };
            _searchBar.OnContentsChanged += text =>
            {
                _browser.SetSearch(TmodSearchField.NormalizeContents(text, _searchPlaceholder));
                RebuildEntryElements();
            };
            _searchBar.OnStartTakingInput += () =>
            {
                if (_projectNameEditing)
                {
                    _projectNameEditing = false;
                    _projectNameRow?.CommitEditing();
                    _committedProjectName = _projectNameRow?.GetCommittedName() ?? _committedProjectName;
                }
            };
            _searchBoxPanel.OnLeftClick += (_, _) => _searchBar.ToggleTakingText();
            _searchBoxPanel.Append(_searchBar);
            toolbarPanel.Append(_searchBoxPanel);

            var closeButton = new TmodUIImageButton(
                btn,
                btn,
                TmodFrameStyle.Icon(TmodUIAssets.Shared.SearchCancelPath, TmodUIAssets.Bestiary.ButtonBorderPath),
                "X",
                RequestClose);
            toolbarPanel.Append(closeButton);

            float contentTop = TmodTheme.BrowserToolbarHeight;
            float contentHeight = TmodTheme.BrowserContentHeight;

            _browserPanel = new UIElement { OverflowHidden = true };
            TmodLayout.AnchorTopLeft(_browserPanel, 0f, contentTop, TmodTheme.BrowserEntryPanelWidth, contentHeight);
            _window.Append(_browserPanel);

            _entryScrollHost = new UIElement { OverflowHidden = true };
            TmodLayout.AnchorTopLeft(
                _entryScrollHost,
                0f,
                0f,
                TmodTheme.BrowserGridInnerWidth,
                contentHeight);
            _browserPanel.Append(_entryScrollHost);

            _entryGrid = new UIGrid
            {
                OverflowHidden = true,
                ListPadding = TmodTheme.Spacing
            };
            _entryGrid.Width.Set(0f, 1f);
            _entryGrid.Height.Set(0f, 1f);
            _entryScrollHost.Append(_entryGrid);

            _scrollbar = new UIScrollbar();
            _scrollbar.Width.Set(TmodTheme.BrowserScrollbarWidth, 0f);
            _scrollbar.Top.Set(TmodTheme.BrowserScrollbarInset, 0f);
            _scrollbar.Height.Set(-TmodTheme.BrowserScrollbarInset * 2f, 1f);
            _scrollbar.HAlign = 1f;
            _browserPanel.Append(_scrollbar);
            _entryGrid.SetScrollbar(_scrollbar);

            _emptyEntriesLabel = new TmodUILabel(
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.NoPayloadEntries"),
                textScale: 0.85f,
                secondary: true);
            TmodLayout.AnchorTopLeft(_emptyEntriesLabel, 0f, 24f, TmodTheme.BrowserEntryPanelInnerWidth, 20f);
            _emptyEntriesLabel.IgnoresMouseInteraction = true;
            _browserPanel.Append(_emptyEntriesLabel);

            float controlLeft = TmodTheme.BrowserEntryPanelWidth;
            float payloadLeft = controlLeft + TmodTheme.BrowserControlPanelWidth;

            var controlPanel = new UIElement { OverflowHidden = true };
            TmodLayout.AnchorTopLeft(controlPanel, controlLeft, contentTop, TmodTheme.BrowserControlPanelWidth, contentHeight);
            _window.Append(controlPanel);

            var controlStack = TmodLayout.CreateVerticalStack(spacing: TmodTheme.Spacing, crossAxisCenter: true);
            TmodLayout.FillParent(controlStack);
            controlPanel.Append(controlStack);

            controlStack.Append(new TmodUIImageButton(
                TmodTheme.BrowserControlButtonSize,
                TmodTheme.BrowserControlButtonSize,
                TmodFrameStyle.Icon(TmodUIAssets.Bestiary.ButtonForwardPath, TmodUIAssets.Bestiary.ButtonBorderPath),
                "Import",
                OnImportCommit));

            controlStack.Append(new TmodUIImageButton(
                TmodTheme.BrowserControlButtonSize,
                TmodTheme.BrowserControlButtonSize,
                TmodFrameStyle.Icon(TmodUIAssets.Bestiary.ButtonBackPath, TmodUIAssets.Bestiary.ButtonBorderPath),
                "Export",
                OnExport));

            controlStack.Append(new TmodUIImageButton(
                TmodTheme.BrowserControlButtonSize,
                TmodTheme.BrowserControlButtonSize,
                TmodFrameStyle.Icon(TmodUIAssets.Shared.ButtonDeletePath, TmodUIAssets.Bestiary.ButtonBorderPath),
                "Cancel",
                OnCancelPreview));

            _exportJsonCheckbox = new TmodUICheckbox(
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.ExportReadableJsonHoverOff"),
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.ExportReadableJsonHoverOn"),
                false);
            _exportJsonCheckbox.SelectedChanged += (_, _) => _browser.ExportAsReadableJson = _exportJsonCheckbox.Selected;
            controlStack.Append(_exportJsonCheckbox);

            var payloadPanel = new TmodPanel { OverflowHidden = true };
            TmodLayout.AnchorTopLeft(payloadPanel, payloadLeft, contentTop, TmodTheme.BrowserPayloadPanelWidth, contentHeight);
            _window.Append(payloadPanel);

            _previewStack = payloadPanel.AppendVerticalStack(TmodTheme.Spacing);
            _committedProjectName = PayloadBrowser.BuildDefaultProjectName();

            _window.IsTextInputActive = () =>
                _searchBar.IsWritingText || (_projectNameRow?.IsCapturingTextInput ?? false);

            RebuildEntryElements();
            RefreshPayloadSection(force: true);
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
                if (element == _searchBoxPanel
                    || element is TmodFramedControl
                    or UISearchBar
                    or UITextBox)
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
                RequestClose();
                return;
            }

            _browser.SyncActiveProjectionFromTopmost();
            RefreshPayloadSection(force: false);
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
                anchorLeftPixels: TmodTheme.BrowserOptionsPopupAnchorLeft);
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
                anchorLeftPixels: TmodTheme.BrowserOptionsPopupAnchorLeft);
            _filterOptionsPopup.OnRequestClose += CloseOptionsPopups;
            _browserPanel.Append(_filterOptionsPopup);
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

        private void ReloadEntries()
        {
            _browser.ReloadEntries();
            RebuildEntryElements();
        }

        private void RebuildEntryElements()
        {
            _entryGrid.Clear();
            for (int i = 0; i < _browser.EntryCount; i++)
            {
                PayloadImportListEntry entry = _browser.Entries[i];
                _entryGrid.Add(new PayloadFileEntry(owner: this, entry: entry));
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
            if (!_projectNameEditing)
                _committedProjectName = panel.ProjectName;

            _payloadThumbnail?.Dispose();
            _payloadThumbnail = null;
            if (panel.EntryThumbnailPng is { Length: > 0 } previewThumb)
            {
                _payloadThumbnail = PayloadTagIO.TryDecodeThumbnailPng(previewThumb);
                if (_payloadThumbnail != null)
                    TrackDecodedThumbnail(_payloadThumbnail);
            }
            else if (panel.MakeThumbnailFromPayload && _activeProjection is { HasPayload: true } projection)
            {
                _payloadThumbnail = PayloadPreview.MakeThumbnail(
                    projection.Payload,
                    (int)TmodTheme.BrowserPayloadThumbnailSize);
                if (_payloadThumbnail != null)
                    TrackDecodedThumbnail(_payloadThumbnail);
            }

            _projectNameRow = PayloadPreviewLayouts.Rebuild(
                _previewStack,
                panel,
                CreatePreviewContext(),
                _projectNameRow);
        }

        private PayloadPreviewBuildContext CreatePreviewContext() =>
            new()
            {
                ProjectNameEditing = _projectNameEditing,
                ThumbnailTexture = _payloadThumbnail,
                ToggleProjectNameEdit = ToggleProjectNameEdit
            };

        private void ToggleProjectNameEdit()
        {
            if (_projectNameEditing)
            {
                _projectNameRow?.CommitEditing();
                _committedProjectName = _projectNameRow?.GetCommittedName() ?? _committedProjectName;
                _projectNameEditing = false;
            }
            else
            {
                _projectNameEditing = true;
                _projectNameRow?.BeginEditing();
            }
        }

        private string GetExportProjectName()
        {
            if (_projectNameEditing)
                return _projectNameRow?.GetEditingName() ?? string.Empty;

            return _committedProjectName;
        }

        private void OnCancelPreview()
        {
            _browser.CancelPreview();
            RefreshPayloadSection(force: true);
        }

        private void OnEntrySelected(PayloadImportListEntry entry)
        {
            if (!_browser.TrySelectEntry(entry, out string? error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                    Main.NewText($"Import preview failed: {error}", Color.OrangeRed);
                return;
            }

            RefreshPayloadSection(force: true);
        }

        private void OnImportCommit()
        {
            if (!_browser.CommitImport(out string? error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                    Main.NewText(error, Color.OrangeRed);
                return;
            }

            Main.NewText("Import applied to projection.", Color.LightGreen);
            RefreshPayloadSection(force: true);
        }

        private void OnExport()
        {
            string projectName = GetExportProjectName();
            if (!_browser.Export(projectName, out string? error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                    Main.NewText($"Export failed: {error}", Color.OrangeRed);
                return;
            }

            _projectNameEditing = false;
            _projectNameRow?.EndTextEntry();
            _committedProjectName = PayloadBrowser.BuildDefaultProjectName();
            if (_searchBar.IsWritingText)
                _searchBar.ToggleTakingText();
            RebuildEntryElements();
            RefreshPayloadSection(force: true);
        }

        internal void TrackDecodedThumbnail(Texture2D texture) => _decodedThumbnails.Add(texture);

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

                Width.Set(TmodTheme.BrowserCompactCellSize, 0f);
                Height.Set(TmodTheme.BrowserCompactCellSize, 0f);

                _cell = new PayloadEntryCell(
                    () => PayloadBrowser.BuildEntryHoverText(_entry),
                    () => _owner.OnEntrySelected(_entry));
                Append(_cell);
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
                _owner.TrackDecodedThumbnail(texture);
            }
        }
    }
}
