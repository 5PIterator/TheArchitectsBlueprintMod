using System;
using System.Collections.Generic;
using TheArchitectsBlueprintMod.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI;

namespace TheArchitectsBlueprintMod.UI
{
    /// <summary>
    /// Display data for one payload file shown in the import dialog.
    /// </summary>
    public readonly struct PayloadImportDialogEntry(string displayName, string details, string fullPath)
    {
        public string DisplayName { get; } = displayName;
        public string Details { get; } = details;
        public string FullPath { get; } = fullPath;
    }

    /// <summary>
    /// Hosts payload import/export modal dialogs and exposes their public UI API.
    /// </summary>
    public sealed class PayloadFileDialogs : ModSystem
    {
        private enum DialogMode
        {
            None,
            ExportName,
            ImportList,
        }

        private const int DialogWidth = 520;
        private const int ExportDialogHeight = 190;
        private const int ImportDialogHeight = 430;
        private const int Padding = 18;
        private const int ButtonWidth = 92;
        private const int ButtonHeight = 30;
        private const int TextBoxHeight = 34;
        private const int RowHeight = 50;
        private const int MaxVisibleRows = 6;

        private static readonly Rectangle PixelSource = new(0, 0, 1, 1);
        private DialogMode _mode;
        private string _exportName = string.Empty;
        private Action<string>? _exportSave;
        private Action? _cancel;
        private IReadOnlyList<PayloadImportDialogEntry> _importEntries = [];
        private Action<string>? _importSelect;
        private int _scrollOffset;
        private bool _previousLeftDown;
        private int _previousScrollWheel;

        private static PayloadFileDialogs Instance => ModContent.GetInstance<PayloadFileDialogs>();

        public static bool IsActive => Instance._mode != DialogMode.None;

        /// <summary>Shows a modal text-entry dialog for choosing an export project name.</summary>
        public static void ShowExportNameDialog(string defaultProjectName, Action<string> onSave, Action? onCancel)
        {
            // --- Store the callback and switch the host into export-name mode.

            ArgumentNullException.ThrowIfNull(onSave);
            Instance.OpenExport(defaultProjectName, onSave, onCancel);
        }

        /// <summary>Shows a modal list dialog for choosing a local payload file to import.</summary>
        public static void ShowImportListDialog(IReadOnlyList<PayloadImportDialogEntry> entries, Action<string> onSelect, Action? onCancel)
        {
            // --- Store the entries and switch the host into import-list mode.

            ArgumentNullException.ThrowIfNull(entries);
            ArgumentNullException.ThrowIfNull(onSelect);
            Instance.OpenImport(entries, onSelect, onCancel);
        }

        /// <summary>Closes any active payload dialog.</summary>
        public static void CloseActive()
        {
            // --- Cancel the current modal without invoking callbacks.

            Instance.Close(invokeCancel: false);
        }

        public override void UpdateUI(GameTime gameTime)
        {
            // --- Update active dialog input and consume game mouse interaction while modal.

            if (_mode == DialogMode.None)
            {
                _previousLeftDown = Main.mouseLeft;
                _previousScrollWheel = Mouse.GetState().ScrollWheelValue;
                return;
            }

            if (Main.gameMenu)
            {
                Close(invokeCancel: true);
                _previousLeftDown = Main.mouseLeft;
                return;
            }

            Main.LocalPlayer.mouseInterface = true;
            PlayerInput.WritingText = _mode == DialogMode.ExportName;

            bool leftClicked = Main.mouseLeft && !_previousLeftDown;
            _previousLeftDown = Main.mouseLeft;

            if (JustPressed(Keys.Escape))
            {
                Close(invokeCancel: true);
                return;
            }

            if (_mode == DialogMode.ExportName)
                UpdateExportDialog(leftClicked);
            else if (_mode == DialogMode.ImportList)
                UpdateImportDialog(leftClicked);
        }

        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            int mouseTextIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Mouse Text"));
            if (mouseTextIndex == -1)
                mouseTextIndex = layers.Count;

            layers.Insert(mouseTextIndex, new LegacyGameInterfaceLayer(
                "TheArchitectsBlueprintMod: PayloadFileDialogs",
                DrawInterface,
                InterfaceScaleType.UI));
        }

        public override void OnWorldUnload()
        {
            Close(invokeCancel: false);
        }

        /// <summary>Opens the export dialog with its callback state.</summary>
        private void OpenExport(string defaultProjectName, Action<string> onSave, Action? onCancel)
        {
            // --- Replace any current dialog with a fresh export-name prompt.

            _mode = DialogMode.ExportName;
            _exportName = defaultProjectName ?? string.Empty;
            _exportSave = onSave;
            _cancel = onCancel;
            _importEntries = [];
            _importSelect = null;
            _scrollOffset = 0;
            _previousLeftDown = Main.mouseLeft;
            _previousScrollWheel = Mouse.GetState().ScrollWheelValue;
        }

        /// <summary>Opens the import dialog with its list and callback state.</summary>
        private void OpenImport(IReadOnlyList<PayloadImportDialogEntry> entries, Action<string> onSelect, Action? onCancel)
        {
            // --- Replace any current dialog with a fresh import-file picker.

            _mode = DialogMode.ImportList;
            _importEntries = entries;
            _importSelect = onSelect;
            _cancel = onCancel;
            _exportSave = null;
            _exportName = string.Empty;
            _scrollOffset = 0;
            _previousLeftDown = Main.mouseLeft;
            _previousScrollWheel = Mouse.GetState().ScrollWheelValue;
        }

        /// <summary>Closes the active dialog and optionally invokes its cancel callback.</summary>
        private void Close(bool invokeCancel)
        {
            // --- Clear modal state after optionally notifying the caller.

            Action? cancel = _cancel;
            _mode = DialogMode.None;
            _exportName = string.Empty;
            _exportSave = null;
            _cancel = null;
            _importEntries = [];
            _importSelect = null;
            _scrollOffset = 0;
            _previousScrollWheel = Mouse.GetState().ScrollWheelValue;
            PlayerInput.WritingText = false;

            if (invokeCancel)
                cancel?.Invoke();
        }

        /// <summary>Updates text input and buttons for the export dialog.</summary>
        private void UpdateExportDialog(bool leftClicked)
        {
            // --- Handle typed project name, Enter-to-save, and button clicks.

            Main.instance.HandleIME();
            _exportName = Main.GetInputText(_exportName);

            if (_exportName.Length > 80)
                _exportName = _exportName[..80];

            if (JustPressed(Keys.Enter))
            {
                SaveExport();
                return;
            }

            Rectangle bounds = GetDialogBounds(ExportDialogHeight);
            Rectangle cancelButton = GetButton(bounds, 1);
            Rectangle saveButton = GetButton(bounds, 0);
            Point mouse = new(Main.mouseX, Main.mouseY);

            if (!leftClicked)
                return;

            if (saveButton.Contains(mouse))
                SaveExport();
            else if (cancelButton.Contains(mouse))
                Close(invokeCancel: true);
        }

        /// <summary>Updates scrolling and row selection for the import dialog.</summary>
        private void UpdateImportDialog(bool leftClicked)
        {
            // --- Scroll visible rows and select a clicked file path.

            int maxOffset = Math.Max(0, _importEntries.Count - MaxVisibleRows);
            int currentScrollWheel = Mouse.GetState().ScrollWheelValue;
            int scrollDelta = currentScrollWheel - _previousScrollWheel;
            _previousScrollWheel = currentScrollWheel;
            if (scrollDelta != 0)
            {
                _scrollOffset -= Math.Sign(scrollDelta);
                _scrollOffset = Math.Clamp(_scrollOffset, 0, maxOffset);
            }

            Rectangle bounds = GetDialogBounds(ImportDialogHeight);
            Rectangle cancelButton = GetButton(bounds, 0);
            Point mouse = new(Main.mouseX, Main.mouseY);

            if (!leftClicked)
                return;

            if (cancelButton.Contains(mouse))
            {
                Close(invokeCancel: true);
                return;
            }

            for (int row = 0; row < MaxVisibleRows; row++)
            {
                int index = _scrollOffset + row;
                if (index >= _importEntries.Count)
                    break;

                if (GetImportRowBounds(bounds, row).Contains(mouse))
                {
                    SelectImport(_importEntries[index].FullPath);
                    return;
                }
            }
        }

        /// <summary>Invokes the export callback and closes the dialog.</summary>
        private void SaveExport()
        {
            // --- Capture callback state before clearing modal data.

            Action<string>? save = _exportSave;
            string projectName = _exportName;
            Close(invokeCancel: false);
            save?.Invoke(projectName);
        }

        /// <summary>Invokes the import callback and closes the dialog.</summary>
        private void SelectImport(string fullPath)
        {
            // --- Capture callback state before clearing modal data.

            Action<string>? select = _importSelect;
            Close(invokeCancel: false);
            select?.Invoke(fullPath);
        }

        /// <summary>Draws the active payload modal.</summary>
        private bool DrawInterface()
        {
            // --- Draw the active dialog in the UI layer.

            if (_mode == DialogMode.ExportName)
                DrawExportDialog();
            else if (_mode == DialogMode.ImportList)
                DrawImportDialog();

            return true;
        }

        /// <summary>Draws the export-name modal.</summary>
        private void DrawExportDialog()
        {
            // --- Draw window chrome, text input, and action buttons.

            Rectangle bounds = GetDialogBounds(ExportDialogHeight);
            DrawDialogPanel(bounds, "Export Payload");

            DynamicSpriteFont font = FontAssets.MouseText.Value;
            Vector2 labelPos = new(bounds.X + Padding, bounds.Y + 54);
            Main.spriteBatch.DrawString(font, "Project name", labelPos, Color.White);

            Rectangle textBox = new(bounds.X + Padding, bounds.Y + 80, bounds.Width - Padding * 2, TextBoxHeight);
            DrawFilledRect(textBox, new Color(25, 26, 36, 245), new Color(130, 132, 160, 255));
            string text = _exportName;
            if (Main.GameUpdateCount % 60 < 30)
                text += "|";
            Main.spriteBatch.DrawString(font, text, new Vector2(textBox.X + 8, textBox.Y + 7), Color.White);

            DrawButton(GetButton(bounds, 1), "Cancel");
            DrawButton(GetButton(bounds, 0), "Export");
        }

        /// <summary>Draws the import-list modal.</summary>
        private void DrawImportDialog()
        {
            // --- Draw window chrome, visible payload rows, scroll hint, and cancel button.

            Rectangle bounds = GetDialogBounds(ImportDialogHeight);
            DrawDialogPanel(bounds, "Import Payload");

            DynamicSpriteFont font = FontAssets.MouseText.Value;
            int visibleCount = Math.Min(MaxVisibleRows, _importEntries.Count);
            for (int row = 0; row < visibleCount; row++)
            {
                int index = _scrollOffset + row;
                Rectangle rowBounds = GetImportRowBounds(bounds, row);
                bool hovered = rowBounds.Contains(new Point(Main.mouseX, Main.mouseY));
                DrawFilledRect(rowBounds, hovered ? new Color(70, 76, 105, 245) : new Color(32, 35, 48, 235), new Color(95, 98, 125, 255));

                PayloadImportDialogEntry entry = _importEntries[index];
                Main.spriteBatch.DrawString(font, entry.DisplayName, new Vector2(rowBounds.X + 10, rowBounds.Y + 7), Color.White);
                Main.spriteBatch.DrawString(font, entry.Details, new Vector2(rowBounds.X + 10, rowBounds.Y + 28), Color.LightGray, 0f, Vector2.Zero, 0.82f, SpriteEffects.None, 0f);
            }

            if (_importEntries.Count > MaxVisibleRows)
            {
                string scrollText = $"{_scrollOffset + 1}-{_scrollOffset + visibleCount} of {_importEntries.Count} (mouse wheel)";
                Main.spriteBatch.DrawString(font, scrollText, new Vector2(bounds.X + Padding, bounds.Bottom - 44), Color.LightGray, 0f, Vector2.Zero, 0.8f, SpriteEffects.None, 0f);
            }

            DrawButton(GetButton(bounds, 0), "Cancel");
        }

        /// <summary>Draws a modal panel with a title.</summary>
        private static void DrawDialogPanel(Rectangle bounds, string title)
        {
            // --- Draw backdrop, body, border, and title text.

            DrawFilledRect(new Rectangle(0, 0, Main.screenWidth, Main.screenHeight), Color.Black * 0.45f, Color.Transparent);
            DrawFilledRect(bounds, new Color(18, 20, 30, 245), new Color(110, 112, 145, 255));
            Main.spriteBatch.DrawString(FontAssets.MouseText.Value, title, new Vector2(bounds.X + Padding, bounds.Y + 18), Color.White);
        }

        /// <summary>Draws a clickable button.</summary>
        private static void DrawButton(Rectangle bounds, string label)
        {
            // --- Fill the button, highlight hover, and center its label.

            bool hovered = bounds.Contains(new Point(Main.mouseX, Main.mouseY));
            DrawFilledRect(bounds, hovered ? new Color(92, 99, 135, 255) : new Color(55, 60, 84, 255), new Color(145, 148, 178, 255));

            DynamicSpriteFont font = FontAssets.MouseText.Value;
            Vector2 size = font.MeasureString(label);
            Vector2 pos = new(bounds.Center.X - size.X * 0.5f, bounds.Center.Y - size.Y * 0.5f);
            Main.spriteBatch.DrawString(font, label, pos, Color.White);
        }

        /// <summary>Draws a filled rectangle with a one-pixel border.</summary>
        private static void DrawFilledRect(Rectangle bounds, Color fill, Color border)
        {
            // --- Draw the fill first, then four border edges when requested.

            Texture2D pixel = ModUtils.DummyTexture;
            Main.spriteBatch.Draw(pixel, bounds, PixelSource, fill);
            if (border == Color.Transparent)
                return;

            Main.spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), PixelSource, border);
            Main.spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), PixelSource, border);
            Main.spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), PixelSource, border);
            Main.spriteBatch.Draw(pixel, new Rectangle(bounds.Right - 1, bounds.Y, 1, bounds.Height), PixelSource, border);
        }

        /// <summary>Returns true on the first frame a key is pressed.</summary>
        private static bool JustPressed(Keys key)
        {
            // --- Compare current and previous keyboard states.

            return Main.keyState.IsKeyDown(key) && !Main.oldInputText.IsKeyDown(key);
        }

        /// <summary>Returns the centered dialog rectangle for a specific height.</summary>
        private static Rectangle GetDialogBounds(int height)
        {
            // --- Center a fixed-width dialog in screen space.

            return new Rectangle((Main.screenWidth - DialogWidth) / 2, (Main.screenHeight - height) / 2, DialogWidth, height);
        }

        /// <summary>Returns one of the bottom-right button rectangles.</summary>
        private static Rectangle GetButton(Rectangle dialogBounds, int indexFromRight)
        {
            // --- Lay buttons right-to-left with standard spacing.

            int x = dialogBounds.Right - Padding - ButtonWidth - indexFromRight * (ButtonWidth + 10);
            int y = dialogBounds.Bottom - Padding - ButtonHeight;
            return new Rectangle(x, y, ButtonWidth, ButtonHeight);
        }

        /// <summary>Returns the rectangle for a visible import row.</summary>
        private static Rectangle GetImportRowBounds(Rectangle dialogBounds, int visibleRow)
        {
            // --- Stack rows below the title with compact spacing.

            return new Rectangle(
                dialogBounds.X + Padding,
                dialogBounds.Y + 58 + visibleRow * (RowHeight + 6),
                dialogBounds.Width - Padding * 2,
                RowHeight);
        }
    }
}
