using System;
using System.Collections.Generic;
using ArchitectsStencilMod.UIKit;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.UI
{
    /// <summary>
    /// UIKit import payload dialog with a small scrollable row list.
    /// </summary>
    internal sealed class ImportPayloadWindow : UIWindow
    {
        private const int MaxVisibleRows = 6;
        private const int RowHeight = 48;
        private readonly IReadOnlyList<PayloadImportDialogEntry> _entries;
        private readonly Action<string> _select;
        private readonly Action _close;
        private readonly List<PayloadRow> _rows = [];
        private int _offset;

        public ImportPayloadWindow(IReadOnlyList<PayloadImportDialogEntry> entries, Action<string> select, Action close)
        {
            // --- Compose a UIKit window and populate visible payload rows.

            _entries = entries;
            _select = select;
            _close = close;
            Width = 560;
            Height = 430;
            Anchor = AnchorPosition.Center;
            Position = new Vector2(Main.screenWidth / 2f, Main.screenHeight / 2f);

            AddChild(new UILabel("Import Payload") { Position = new Vector2(18, 16), Scale = 0.72f });
            AddChild(new UILabel("Mouse wheel scrolls the list") { Position = new Vector2(18, Height - 45), Scale = 0.38f, ForegroundColor = Color.LightGray });

            var cancel = new UIButton("Cancel") { AutoSize = false, Width = 92, Position = new Vector2(Width - 110, Height - 48) };
            cancel.onLeftClick += (_, _) => ModContent.GetInstance<PayloadFileDialogs>().CloseWithCancel();
            AddChild(cancel);

            RebuildRows();
        }

        public override void Update()
        {
            base.Update();
            if (ScrollAmount != 0 && MouseInside)
            {
                int oldOffset = _offset;
                _offset = Math.Clamp(_offset - Math.Sign(ScrollAmount), 0, Math.Max(0, _entries.Count - MaxVisibleRows));
                if (_offset != oldOffset)
                    RebuildRows();
            }
        }

        /// <summary>Rebuilds the visible payload rows from the current scroll offset.</summary>
        private void RebuildRows()
        {
            // --- Remove old row controls and create rows for the visible slice.

            foreach (PayloadRow row in _rows)
                RemoveChild(row);
            _rows.Clear();

            int count = Math.Min(MaxVisibleRows, Math.Max(0, _entries.Count - _offset));
            for (int i = 0; i < count; i++)
            {
                PayloadImportDialogEntry entry = _entries[_offset + i];
                var row = new PayloadRow(entry, SelectAndClose)
                {
                    Position = new Vector2(18, 58 + i * (RowHeight + 6)),
                    Width = Width - 36,
                    Height = RowHeight,
                };
                _rows.Add(row);
                AddChild(row);
            }
        }

        /// <summary>Selects a payload and closes the dialog.</summary>
        private void SelectAndClose(PayloadImportDialogEntry entry)
        {
            // --- Close before invoking import logic.

            _close();
            _select(entry.FullPath);
        }
    }
}
