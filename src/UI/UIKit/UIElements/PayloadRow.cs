using System;
using ArchitectsStencilMod.UIKit;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace ArchitectsStencilMod.UI
{
    /// <summary>
    /// One clickable payload file row.
    /// </summary>
    internal sealed class PayloadRow : UIView
    {
        private readonly PayloadImportDialogEntry _entry;
        private readonly Action<PayloadImportDialogEntry> _select;

        public PayloadRow(PayloadImportDialogEntry entry, Action<PayloadImportDialogEntry> select)
        {
            // --- Store row callbacks and add labels for display name/details.

            _entry = entry;
            _select = select;
            AddChild(new UILabel(entry.DisplayName) { Position = new Vector2(8, 5), Scale = 0.42f });
            AddChild(new UILabel(entry.Details) { Position = new Vector2(8, 26), Scale = 0.32f, ForegroundColor = Color.LightGray });
            onLeftClick += (_, _) => _select(_entry);
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            var bounds = new Rectangle((int)(DrawPosition.X - Origin.X), (int)(DrawPosition.Y - Origin.Y), (int)Width, (int)Height);
            Color fill = MouseInside ? new Color(70, 76, 105, 245) : new Color(32, 35, 48, 235);
            spriteBatch.Draw(global::ArchitectsStencilMod.ModUtils.ModUtils.DummyTexture, bounds, fill);
            base.Draw(spriteBatch);
        }
    }
}
