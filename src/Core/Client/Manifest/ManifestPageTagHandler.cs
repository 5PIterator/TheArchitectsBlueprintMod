using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Localization;
using Terraria.UI.Chat;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Parses <c>[tabmanifest:page]</c> into a clickable next-page chat snippet.</summary>
    internal sealed class ManifestPageTagHandler : ITagHandler
    {
        public TextSnippet Parse(string text, Color baseColor, string options)
        {
            // --- Options carry the 1-based page index; display text is localized.

            if (!int.TryParse(string.IsNullOrWhiteSpace(options) ? text : options, out int page) || page < 1)
                page = 1;

            int total = Math.Max(page, ManifestPrinter.CachedTotalPages);
            string label = Language.GetTextValue(
                "Mods.TheArchitectsBlueprintMod.UI.Manifest.NextPage",
                page,
                total);
            return new ManifestPageSnippet(label, page)
            {
                Color = ManifestPrinter.HeaderColor,
            };
        }
    }

    /// <summary>Chat snippet that prints the next cached manifest page when clicked.</summary>
    internal sealed class ManifestPageSnippet : TextSnippet
    {
        private readonly int _page;

        public ManifestPageSnippet(string text, int page) : base(text)
        {
            _page = page;
        }

        public override void OnClick()
        {
            // --- Ignore clicks when chat is closed; otherwise advance the cached manifest.

            if (!Main.drawingPlayerChat)
                return;

            ManifestPrinter.PrintPage(_page);
        }

        public override Color GetVisibleColor() =>
            Main.drawingPlayerChat ? Color.Lerp(Color, Color.White, 0.35f) : Color;
    }
}
