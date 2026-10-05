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
    /// <summary>Builds modular payload preview rows from panel state (special + default layouts).</summary>
    internal static class PayloadPreviewLayouts
    {
        private const float AuthorRowHeight = 18f;

        public static PayloadProjectNameRow? Rebuild(
            TmodStackPanel host,
            PayloadBrowserPanelState panel,
            PayloadPreviewBuildContext ctx,
            PayloadProjectNameRow? projectNameRow = null)
        {
            host.ClearChildren();

            if (!panel.HasProjection)
            {
                AppendEmptyState(host);
                return projectNameRow;
            }

            projectNameRow ??= new PayloadProjectNameRow(ctx);
            projectNameRow.ApplyState(panel.ProjectName, ctx);
            host.Append(projectNameRow);
            host.Append(new TmodUISeparator());
            AppendThumbnailColumn(host, panel, ctx);
            host.Append(new TmodUISeparator());

            foreach (string key in OrderMetadataKeys(panel.Metadata))
                AppendDefaultRow(host, key, panel.Metadata[key]);

            return projectNameRow;
        }

        private static void AppendEmptyState(TmodStackPanel host)
        {
            var holder = new UIElement();
            holder.Width.Set(0f, 1f);
            holder.Height.Set(Theme.BrowserPayloadSectionHeight, 0f);

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
            host.Append(holder);
        }

        private static void AppendThumbnailColumn(
            TmodStackPanel host,
            PayloadBrowserPanelState panel,
            PayloadPreviewBuildContext ctx)
        {
            float thumb = Theme.BrowserPayloadThumbnailSize;
            float blockHeight = thumb + Theme.Spacing + AuthorRowHeight;

            var column = new UIElement { OverflowHidden = true };
            TmodLayout.StackFillWidth(column, blockHeight);

            var preview = new TmodUIPayloadPreview(thumb);
            preview.HAlign = 0.5f;
            preview.Top.Set(0f, 0f);
            if (ctx.ThumbnailTexture != null)
                preview.SetThumbnail(ctx.ThumbnailTexture);
            else
                preview.SetThumbnail(null);
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

            host.Append(column);
        }

        private static void AppendDefaultRow(TmodStackPanel host, string key, string value)
        {
            string tooltip = ResolveMetadataTooltip(key);
            var row = new TmodUIMetadataDefaultRow(tooltip, value);
            TmodLayout.StackFillWidth(row, Theme.BrowserPayloadMetadataLineHeight);
            host.Append(row);
        }

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
        public required bool ProjectNameEditing { get; init; }
        public required Texture2D? ThumbnailTexture { get; init; }
        public required Action ToggleProjectNameEdit { get; init; }
    }
}
