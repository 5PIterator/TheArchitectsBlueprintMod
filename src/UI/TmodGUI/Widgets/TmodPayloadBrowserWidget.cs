using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ModLoader.UI.Elements;
using Terraria.UI;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.UI;
using TheArchitectsBlueprintMod.UI.TmodGUI.Elements;
using TheArchitectsBlueprintMod.Utils;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Widgets
{
    /// <summary>Unified non-modal payload browser window for preview/import/export.</summary>
    public sealed class TmodPayloadBrowserWidget : TmodWidgetBase
    {
        private readonly List<Texture2D> _decodedThumbnails = new();
        private readonly List<PayloadImportListEntry> _entries = new();

        private TmodWindowPanel _window = null!;
        private UIGrid _grid = null!;
        private UIScrollbar _scrollbar = null!;
        private TmodButton _modeToggleButton = null!;
        private TmodLabel _projectionInfoLabel = null!;
        private TmodTextField _projectNameField = null!;
        private ThumbnailView _projectionThumbnailView = null!;

        private Texture2D? _projectionThumbnail;
        private PayloadImportListEntry? _hoveredEntry;
        private Projection? _activeProjection;
        private bool _lastHadPayload;
        private Point _lastPayloadSize;
        private bool _compactMode = true;

        internal override bool WantsMouseCapture
        {
            get
            {
                if (_window == null)
                    return false;

                Rectangle windowRect = _window.GetOuterDimensions().ToRectangle();
                return windowRect.Contains(Main.mouseX, Main.mouseY)
                    || _window.IsDragging
                    || _projectNameField?.IsFocused == true;
            }
        }

        public static void Open(Projection projection)
        {
            if (Main.dedServ)
                return;

            if (TmodGUI.GetActiveWidget() is TmodPayloadBrowserWidget)
                TmodGUI.Close();

            if (!PayloadSerialization.OpenBrowser(projection))
                return;

            var widget = new TmodPayloadBrowserWidget
            {
                OnClosed = () => PayloadSerialization.CloseBrowserSession(commitActivePreview: false)
            };
            TmodGUI.Show(widget);
        }

        public override void OnDeactivate()
        {
            _projectNameField?.UnfocusField();
            for (int i = 0; i < _decodedThumbnails.Count; i++)
                _decodedThumbnails[i].Dispose();
            _decodedThumbnails.Clear();

            _projectionThumbnail?.Dispose();
            _projectionThumbnail = null;
            base.OnDeactivate();
        }

        protected override void Build()
        {
            _window = new TmodWindowPanel();
            TmodLayout.CenterInParent(_window, TmodTheme.BrowserWidth, TmodTheme.BrowserHeight);
            Append(_window);

            float innerWidth = TmodTheme.BrowserWidth - TmodTheme.Padding * 2f;
            float innerHeight = TmodTheme.BrowserHeight - TmodTheme.Padding * 2f;

            var titleDrag = new UIElement();
            titleDrag.Width.Set(innerWidth - 120f, 0f);
            titleDrag.Height.Set(TmodTheme.TitleHeight + TmodTheme.Padding, 0f);
            titleDrag.OnLeftMouseDown += (evt, _) => _window.BeginDrag(evt);
            _window.Append(titleDrag);

            var titleLabel = new TmodLabel("Payload Browser", textScale: 1f);
            TmodLayout.AnchorTopLeft(titleLabel, 0f, 0f, innerWidth * 0.5f, TmodTheme.TitleHeight);
            titleLabel.OnLeftMouseDown += (evt, _) => _window.BeginDrag(evt);
            _window.Append(titleLabel);

            _modeToggleButton = new TmodButton("List", ToggleMode, 74f, 28f);
            TmodLayout.AnchorTopLeft(_modeToggleButton, innerWidth - 110f, 0f, 74f, 28f);
            _window.Append(_modeToggleButton);

            var closeButton = new TmodButton("X", RequestClose, 28f, 28f);
            TmodLayout.AnchorTopLeft(closeButton, innerWidth - 30f, 0f, 28f, 28f);
            _window.Append(closeButton);

            float contentTop = TmodTheme.TitleHeight + TmodTheme.Spacing;
            float contentHeight = innerHeight - contentTop;

            var filesPanel = new TmodPanel();
            TmodLayout.AnchorTopLeft(filesPanel, 0f, contentTop, TmodTheme.BrowserFilePanelWidth, contentHeight);
            _window.Append(filesPanel);

            var filesLabel = new TmodLabel("Payload Files", textScale: 0.9f);
            TmodLayout.AnchorTopLeft(filesLabel, 0f, 0f, TmodTheme.BrowserFilePanelWidth - TmodTheme.Padding * 2f, 22f);
            filesPanel.Append(filesLabel);

            var gridHost = new UIElement();
            TmodLayout.AnchorTopLeft(
                gridHost,
                0f,
                22f + TmodTheme.Spacing,
                TmodTheme.BrowserFilePanelWidth - TmodTheme.Padding * 2f,
                contentHeight - TmodTheme.Padding * 2f - 22f - TmodTheme.Spacing);
            filesPanel.Append(gridHost);

            _grid = new UIGrid();
            _grid.Width.Set(-24f, 1f);
            _grid.Height.Set(0f, 1f);
            _grid.ListPadding = TmodTheme.Spacing;
            gridHost.Append(_grid);

            _scrollbar = new UIScrollbar();
            _scrollbar.Width.Set(20f, 0f);
            _scrollbar.Height.Set(0f, 1f);
            _scrollbar.HAlign = 1f;
            _grid.SetScrollbar(_scrollbar);
            gridHost.Append(_scrollbar);

            float blueprintLeft = TmodTheme.BrowserFilePanelWidth + TmodTheme.Spacing;
            float blueprintWidth = innerWidth - blueprintLeft;

            var blueprintPanel = new TmodPanel();
            TmodLayout.AnchorTopLeft(blueprintPanel, blueprintLeft, contentTop, blueprintWidth, contentHeight);
            _window.Append(blueprintPanel);

            var blueprintLabel = new TmodLabel("Blueprint Panel", textScale: 0.9f);
            TmodLayout.AnchorTopLeft(blueprintLabel, 0f, 0f, blueprintWidth - TmodTheme.Padding * 2f, 22f);
            blueprintPanel.Append(blueprintLabel);

            _projectionInfoLabel = new TmodLabel("No projection selected.", textScale: 0.8f, secondary: true);
            TmodLayout.AnchorTopLeft(_projectionInfoLabel, 0f, 24f, blueprintWidth - TmodTheme.Padding * 2f, 22f);
            blueprintPanel.Append(_projectionInfoLabel);

            _projectionThumbnailView = new ThumbnailView();
            TmodLayout.AnchorTopLeft(
                _projectionThumbnailView,
                0f,
                52f,
                TmodTheme.BrowserBlueprintThumbnailSize,
                TmodTheme.BrowserBlueprintThumbnailSize);
            blueprintPanel.Append(_projectionThumbnailView);

            var nameLabel = new TmodLabel("Project name:", textScale: 0.85f);
            TmodLayout.AnchorTopLeft(
                nameLabel,
                0f,
                52f + TmodTheme.BrowserBlueprintThumbnailSize + TmodTheme.Spacing,
                blueprintWidth - TmodTheme.Padding * 2f,
                20f);
            blueprintPanel.Append(nameLabel);

            _projectNameField = new TmodTextField(PayloadSerialization.BuildDefaultProjectName(), 80, blueprintWidth - TmodTheme.Padding * 2f);
            TmodLayout.AnchorTopLeft(
                _projectNameField,
                0f,
                52f + TmodTheme.BrowserBlueprintThumbnailSize + TmodTheme.Spacing + 22f,
                blueprintWidth - TmodTheme.Padding * 2f,
                TmodTheme.TextFieldHeight);
            blueprintPanel.Append(_projectNameField);

            var importButton = new TmodButton("Import", OnImportCommit, 94f, TmodTheme.ButtonHeight);
            TmodLayout.AnchorBottomRight(importButton, 2f * (94f + TmodTheme.Spacing), 0f, 94f, TmodTheme.ButtonHeight);
            blueprintPanel.Append(importButton);

            var exportButton = new TmodButton("Export", OnExport, 94f, TmodTheme.ButtonHeight);
            TmodLayout.AnchorBottomRight(exportButton, 94f + TmodTheme.Spacing, 0f, 94f, TmodTheme.ButtonHeight);
            blueprintPanel.Append(exportButton);

            var cancelButton = new TmodButton("Cancel", RequestClose, 94f, TmodTheme.ButtonHeight);
            TmodLayout.AnchorBottomRight(cancelButton, 0f, 0f, 94f, TmodTheme.ButtonHeight);
            blueprintPanel.Append(cancelButton);

            ReloadEntries();
            RefreshProjectionSection(force: true);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Main.keyState.IsKeyDown(Keys.Escape) && !Main.oldKeyState.IsKeyDown(Keys.Escape))
            {
                RequestClose();
                return;
            }

            if (Main.LocalPlayer?.active == true)
                PayloadSerialization.UpdateActiveProjectionFromCursor(Main.LocalPlayer);

            RefreshProjectionSection(force: false);
            RefreshGridScroll();
            PayloadSerialization.UpdateBrowserConnectorOverlay(_window.GetOuterDimensions().ToRectangle());
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            base.Draw(spriteBatch);
            if (_hoveredEntry.HasValue)
                ModUtils.MouseText(BuildHoverText(_hoveredEntry.Value));
        }

        private void ToggleMode()
        {
            _compactMode = !_compactMode;
            _modeToggleButton.SetText(_compactMode ? "List" : "Grid", 0.9f, false);
            RebuildEntryCards();
        }

        private void ReloadEntries()
        {
            _entries.Clear();
            _entries.AddRange(PayloadSerialization.DiscoverPayloadFiles());
            RebuildEntryCards();
        }

        private void RebuildEntryCards()
        {
            _grid.RemoveAllChildren();
            float compactCardSize = ComputeCompactCardSize();

            for (int i = 0; i < _entries.Count; i++)
            {
                PayloadImportListEntry entry = _entries[i];
                _grid.Add(new PayloadFileCard(
                    owner: this,
                    entry: entry,
                    compactMode: _compactMode,
                    compactCardSize: compactCardSize));
            }
        }

        private float ComputeCompactCardSize()
        {
            float innerWidth = TmodTheme.BrowserFilePanelWidth - TmodTheme.Padding * 2f;
            float availableForGrid = innerWidth - 24f;
            float totalSpacing = TmodTheme.Spacing * 4f;
            return Math.Max(64f, (float)Math.Floor((availableForGrid - totalSpacing) / 5f));
        }

        private void RefreshProjectionSection(bool force)
        {
            if (!PayloadSerialization.TryGetActiveBrowserProjection(out Projection? projection) || projection == null)
            {
                _activeProjection = null;
                _projectionInfoLabel.SetText("No projection selected.", 0.8f, false);
                return;
            }

            bool projectionChanged = !ReferenceEquals(_activeProjection, projection);
            Point payloadSize = projection.HasPayload ? projection.Payload.Size : Point.Zero;
            bool payloadVisualChanged = projection.HasPayload != _lastHadPayload || payloadSize != _lastPayloadSize;
            if (!force && !projectionChanged && !payloadVisualChanged)
                return;

            _activeProjection = projection;
            _lastHadPayload = projection.HasPayload;
            _lastPayloadSize = payloadSize;
            _projectionInfoLabel.SetText(
                $"Projection area: {projection.Area.Width}x{projection.Area.Height} | Payload: {(projection.HasPayload ? $"{projection.Payload.Width}x{projection.Payload.Height}" : "none")}",
                0.8f,
                false);

            if (!_projectNameField.IsFocused)
                _projectNameField.SetText(PayloadSerialization.BuildDefaultProjectName(), 0.9f, false);

            _projectionThumbnail?.Dispose();
            _projectionThumbnail = projection.HasPayload
                ? PayloadPreview.MakeThumbnail(projection.Payload, (int)TmodTheme.BrowserBlueprintThumbnailSize)
                : null;
            _projectionThumbnailView.SetTexture(_projectionThumbnail);
        }

        private void RefreshGridScroll()
        {
            float viewHeight = _grid.GetInnerDimensions().Height;
            float totalHeight = _grid.GetTotalHeight();
            _scrollbar.SetView(viewHeight, totalHeight);
        }

        private void OnEntryHovered(PayloadImportListEntry entry) => _hoveredEntry = entry;

        private void OnEntryHoverCleared(PayloadImportListEntry entry)
        {
            if (_hoveredEntry.HasValue && _hoveredEntry.Value.FullPath == entry.FullPath)
                _hoveredEntry = null;
        }

        private void OnEntrySelected(PayloadImportListEntry entry)
        {
            if (!entry.IsValid)
                return;

            if (!PayloadSerialization.PreviewImportOnActiveProjection(entry.FullPath, out string? error))
            {
                Main.NewText(string.IsNullOrWhiteSpace(error) ? "Import preview failed." : $"Import preview failed: {error}", Color.OrangeRed);
                return;
            }

            RefreshProjectionSection(force: true);
        }

        private void OnImportCommit()
        {
            if (!PayloadSerialization.CommitActiveImportPreview(out string? error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                    Main.NewText(error, Color.OrangeRed);
                return;
            }

            Main.NewText("Import applied to projection.", Color.LightGreen);
            RefreshProjectionSection(force: true);
        }

        private void OnExport()
        {
            string projectName = _projectNameField.GetTrimmedText();
            if (!PayloadSerialization.ExportActiveProjection(projectName, out string? error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                    Main.NewText($"Export failed: {error}", Color.OrangeRed);
                return;
            }

            _projectNameField.UnfocusField();
            _projectNameField.SetText(PayloadSerialization.BuildDefaultProjectName(), 0.9f, false);
        }

        private static string BuildHoverText(PayloadImportListEntry entry)
        {
            var sb = new StringBuilder();
            sb.AppendLine(entry.DisplayName);
            sb.AppendLine($"Author: {entry.PlayerName}");

            if (entry.Width > 0 && entry.Height > 0)
                sb.AppendLine($"Size: {entry.Width}x{entry.Height}");
            if (!string.IsNullOrWhiteSpace(entry.CreatedUtc))
                sb.AppendLine($"Created: {entry.CreatedUtc}");
            if (!string.IsNullOrWhiteSpace(entry.ModifiedUtc))
                sb.AppendLine(entry.ModifiedUtc);
            if (entry.ModsLoaded is { Count: > 0 })
            {
                IReadOnlyList<string> missing = PayloadCompatibility.ComputeMissingMods(entry.ModsLoaded);
                if (missing.Count > 0)
                    sb.AppendLine($"Missing mods: {string.Join(", ", missing)}");
            }

            sb.AppendLine(Path.GetFileName(entry.FullPath));
            if (!entry.IsValid && !string.IsNullOrWhiteSpace(entry.ErrorSummary))
                sb.Append(entry.ErrorSummary);

            return sb.ToString();
        }

        private sealed class PayloadFileCard : UIPanel
        {
            private readonly TmodPayloadBrowserWidget _owner;
            private readonly PayloadImportListEntry _entry;
            private readonly ThumbnailView _thumbnail;
            private bool _thumbnailLoaded;

            public PayloadFileCard(TmodPayloadBrowserWidget owner, PayloadImportListEntry entry, bool compactMode, float compactCardSize)
            {
                _owner = owner;
                _entry = entry;

                if (compactMode)
                {
                    Width.Set(compactCardSize, 0f);
                    Height.Set(compactCardSize, 0f);
                }
                else
                {
                    Width.Set(0f, 1f);
                    Height.Set(TmodTheme.BrowserListCardHeight, 0f);
                }

                BackgroundColor = entry.IsValid
                    ? new Color(50, 60, 110, 180)
                    : new Color(40, 42, 55, 160);
                BorderColor = entry.IsValid
                    ? TmodTheme.PanelBorder
                    : new Color(90, 90, 100, 255);

                _thumbnail = new ThumbnailView();
                if (compactMode)
                {
                    TmodLayout.AnchorTopLeft(_thumbnail, 4f, 4f, compactCardSize - 8f, compactCardSize - 8f);
                    Append(_thumbnail);
                }
                else
                {
                    TmodLayout.AnchorTopLeft(_thumbnail, 6f, 6f, 64f, 64f);
                    Append(_thumbnail);

                    var title = new TmodLabel(entry.DisplayName, textScale: 0.82f);
                    TmodLayout.AnchorTopLeft(title, 76f, 6f, 360f, 18f);
                    Append(title);

                    string size = entry.Width > 0 && entry.Height > 0 ? $"{entry.Width}x{entry.Height}" : "Unknown size";
                    var details = new TmodLabel($"by {entry.PlayerName} | {size}", textScale: 0.74f, secondary: true);
                    TmodLayout.AnchorTopLeft(details, 76f, 28f, 460f, 16f);
                    Append(details);

                    string date = string.IsNullOrWhiteSpace(entry.ModifiedUtc) ? Path.GetFileName(entry.FullPath) : entry.ModifiedUtc;
                    var modified = new TmodLabel(date, textScale: 0.72f, secondary: true);
                    TmodLayout.AnchorTopLeft(modified, 76f, 48f, 460f, 16f);
                    Append(modified);
                }

                OnMouseOver += (_, _) => _owner.OnEntryHovered(_entry);
                OnMouseOut += (_, _) => _owner.OnEntryHoverCleared(_entry);
                OnLeftClick += (_, _) => _owner.OnEntrySelected(_entry);
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

                _thumbnail.SetTexture(texture);
                _owner._decodedThumbnails.Add(texture);
            }
        }

        private sealed class ThumbnailView : UIElement
        {
            private Texture2D? _texture;

            public void SetTexture(Texture2D? texture) => _texture = texture;

            protected override void DrawSelf(SpriteBatch spriteBatch)
            {
                Rectangle rect = GetInnerDimensions().ToRectangle();
                if (_texture != null)
                {
                    spriteBatch.Draw(_texture, rect, Color.White);
                    return;
                }

                spriteBatch.Draw(TextureAssets.MagicPixel.Value, rect, new Color(30, 35, 50, 220));
            }
        }
    }
}
