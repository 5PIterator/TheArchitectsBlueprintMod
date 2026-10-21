using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.Localization;
using Terraria.UI;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.UI.Style;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Builds modular payload preview rows from panel state (Bestiary info-page layout).</summary>
    internal static class PayloadPreviewLayouts
    {
        private const float AuthorRowHeight = Theme.AuthorRowHeight;

        /// <summary>Rebuilds the subject info list for the active payload panel state.</summary>
        public static PayloadProjectNameRow? Rebuild(
            UIList host,
            PayloadBrowserPanelState panel,
            PayloadPreviewBuildContext ctx,
            PayloadProjectNameRow? projectNameRow = null)
        {
            // --- Clear list and append nameplate, portrait, metadata, and compatibility cards.
            host.Clear();

            if (!panel.HasProjection)
            {
                AppendEmptyState(host);
                return projectNameRow;
            }

            projectNameRow ??= new PayloadProjectNameRow(ctx);
            projectNameRow.ApplyState(ctx.DisplayProjectName, ctx);
            host.Add(projectNameRow);
            host.Add(CreateSeparator());

            AppendThumbnailColumn(host, panel, ctx);
            host.Add(CreateSeparator());

            bool appendedMetadata = false;
            foreach (string key in OrderMetadataKeys(panel.Metadata))
            {
                if (string.Equals(key, PayloadMetadataKeys.MissingMods, StringComparison.OrdinalIgnoreCase))
                    continue;

                AppendDefaultRow(host, key, panel.Metadata[key]);
                appendedMetadata = true;
            }

            if (panel.Metadata.TryGetValue(PayloadMetadataKeys.MissingMods, out string? missing)
                && !string.IsNullOrWhiteSpace(missing))
            {
                if (appendedMetadata)
                    host.Add(CreateSeparator());

                AppendDefaultRow(host, PayloadMetadataKeys.MissingMods, missing, wrapValue: true);
            }

            return projectNameRow;
        }

        private static void AppendEmptyState(UIList host)
        {
            var holder = new UIElement();
            holder.Width.Set(0f, 1f);
            holder.Height.Set(Theme.BrowserPayloadSectionHeight * 0.5f, 0f);

            var label = new UIText(
                Language.GetText("Mods.TheArchitectsBlueprintMod.UI.NoProjectionSelected"),
                0.85f)
            {
                TextColor = Theme.SecondaryText,
                HAlign = 0.5f,
                VAlign = 0.5f,
                TextOriginX = 0.5f,
                TextOriginY = 0.5f,
                IgnoresMouseInteraction = true
            };
            holder.Append(label);
            host.Add(holder);
        }

        private static void AppendThumbnailColumn(
            UIList host,
            PayloadBrowserPanelState panel,
            PayloadPreviewBuildContext ctx)
        {
            float thumb = Theme.BrowserPayloadThumbnailSize;
            float blockHeight = thumb + Theme.Spacing + AuthorRowHeight;

            var column = new UIElement { OverflowHidden = true };
            column.Width.Set(0f, 1f);
            column.Height.Set(blockHeight, 0f);

            var preview = new TmodUIPayloadPreview(thumb);
            preview.HAlign = 0.5f;
            preview.Top.Set(0f, 0f);
            preview.SetThumbnail(ctx.ThumbnailTexture);
            column.Append(preview);

            string author = panel.AuthorName;
            string authorLine = string.IsNullOrWhiteSpace(author)
                ? string.Empty
                : Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.AuthorBy", author);

            var authorLabel = new UIText(authorLine, 0.72f)
            {
                TextColor = Theme.DisabledText,
                HAlign = 0.5f,
                Top = new StyleDimension(thumb + Theme.Spacing, 0f),
                TextOriginX = 0.5f,
                TextOriginY = 0f,
                IgnoresMouseInteraction = true
            };
            column.Append(authorLabel);

            host.Add(column);
        }

        private static void AppendDefaultRow(UIList host, string key, string value, bool wrapValue = false)
        {
            // --- Resolve a localized label and append a Stat_Panel metadata card.
            string label = ResolveMetadataTooltip(key);
            bool shouldWrap = wrapValue
                || string.Equals(key, PayloadMetadataKeys.ModsLoaded, StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, PayloadMetadataKeys.MissingMods, StringComparison.OrdinalIgnoreCase);
            var row = new TmodUIMetadataDefaultRow(label, value, shouldWrap);
            host.Add(row);
        }

        private static UIHorizontalSeparator CreateSeparator() =>
            new()
            {
                Width = StyleDimension.FromPixelsAndPercent(0f, 1f),
                Color = Theme.InfoPageBorder * 0.9f
            };

        private static IEnumerable<string> OrderMetadataKeys(IReadOnlyDictionary<string, string> metadata)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in PayloadMetadataKeys.DefaultDisplayOrder)
            {
                if (metadata.ContainsKey(key) && seen.Add(key))
                    yield return key;
            }

            foreach (string key in metadata.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                if (!PayloadMetadataKeys.IsReserved(key) && seen.Add(key))
                    yield return key;
            }
        }

        private static string ResolveMetadataTooltip(string key)
        {
            string path = $"Mods.TheArchitectsBlueprintMod.UI.Metadata.{key}";
            if (Language.Exists(path))
                return Language.GetTextValue(path);

            return HumanizeKey(key);
        }

        private static string HumanizeKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return key;

            var chars = new char[key.Length + 8];
            int write = 0;
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(key[i - 1]))
                    chars[write++] = ' ';
                chars[write++] = i == 0 ? char.ToUpperInvariant(c) : c;
            }

            return new string(chars, 0, write);
        }
    }

    internal sealed class PayloadPreviewBuildContext
    {
        public required string DisplayProjectName { get; init; }
        public required bool ProjectNameEditing { get; init; }
        public required Texture2D? ThumbnailTexture { get; init; }
        public required Action ToggleProjectNameEdit { get; init; }
    }
}
