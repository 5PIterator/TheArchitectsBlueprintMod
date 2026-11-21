using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core.Server;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Formats paste manifests for chat; clients request authoritative builds instead of loading maps.</summary>
    internal static class ManifestPrinter
    {
        public static readonly Color HeaderColor = new(135, 206, 250);
        public static readonly Color OkColor = new(144, 238, 144);
        public static readonly Color MissingColor = new(255, 160, 122);
        public static readonly Color WarningColor = new(255, 215, 0);

        private const int MainWidth = 35;
        private const int BodyRowsPerPage = 12;
        private const string CellSeparator = " | ";
        internal const string ChatTagName = "tabmanifest";

        private static readonly List<(string Text, Color Color)> CachedLines = [];
        private static int CachedPageCount;

        /// <summary>Number of pages in the last received manifest (0 when empty).</summary>
        internal static int CachedTotalPages => CachedPageCount;

        /// <summary>Requests an authoritative manifest print for the given projection.</summary>
        public static void Print(Projection projection)
        {
            // --- Never load CompiledMaps on the client; SP builds locally, MP sends ManifestRequest.

            if (Main.dedServ)
                return;

            Mod mod = ModContent.GetInstance<global::TheArchitectsBlueprintMod.TheArchitectsBlueprintMod>()!;
            PasteManifestNetworking.RequestPrint(mod, projection);
        }

        /// <summary>Writes one manifest line to local chat and queues it for the batched mod info log.</summary>
        public static void EmitLocal(string message, Color color)
        {
            // --- Immediate single-line path for errors; paged manifests use EmitPage instead.

            Main.NewText(message, color);
            TABLog.Info(
                message,
                TABLog.ManifestCategory,
                chatColor: color,
                ownerPlayer: null,
                echoToChat: false);
        }

        /// <summary>Stores formatted lines, logs them all, and prints page 1 to chat.</summary>
        public static void ReceiveAndPrintFirstPage(IReadOnlyList<(string Text, Color Color)> lines)
        {
            // --- Cache for clickable next-page tags; file log always gets the full dump.

            CachedLines.Clear();
            for (int i = 0; i < lines.Count; i++)
            {
                (string text, Color color) = lines[i];
                CachedLines.Add((text, color));
                TABLog.Info(
                    text,
                    TABLog.ManifestCategory,
                    chatColor: color,
                    ownerPlayer: null,
                    echoToChat: false);
            }

            TABLog.Flush();
            CachedPageCount = CountPages(CachedLines.Count);
            PrintPage(1);
        }

        /// <summary>Prints one cached manifest page as a single multiline chat message.</summary>
        public static void PrintPage(int page)
        {
            // --- Join body rows with color tags; append a next-page link when more pages remain.

            if (CachedLines.Count == 0)
                return;

            int pageCount = Math.Max(1, CachedPageCount);
            page = Math.Clamp(page, 1, pageCount);

            int start;
            int end;
            GetPageRange(CachedLines.Count, page, out start, out end);

            var builder = new StringBuilder(512);
            for (int i = start; i < end; i++)
            {
                if (builder.Length > 0)
                    builder.Append('\n');
                (string text, Color color) = CachedLines[i];
                builder.Append(WrapWithColorTag(text, color));
            }

            if (page < pageCount)
            {
                builder.Append('\n');
                builder.Append('[').Append(ChatTagName).Append(':').Append(page + 1).Append(']');
            }
            else if (pageCount > 1)
            {
                builder.Append('\n');
                builder.Append(WrapWithColorTag(
                    Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.Manifest.PageEnd", page, pageCount),
                    HeaderColor));
            }

            Main.NewText(builder.ToString());
        }

        /// <summary>Invokes <paramref name="emit"/> for each formatted manifest chat line in print order.</summary>
        public static void EnumerateLines(Manifest manifest, string playerName, Action<string, Color> emit)
        {
            // --- Same row order as the former local PrintManifest path.

            emit(
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.Manifest.Header"),
                HeaderColor);

            if (manifest.IsEmpty)
            {
                emit(
                    Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.Manifest.Empty"),
                    OkColor);
                return;
            }

            emit(FormatHeaderRow(), HeaderColor);

            for (int i = 0; i < manifest.Entries.Count; i++)
            {
                ManifestEntry entry = manifest.Entries[i];
                Color color = entry.MissingCount > 0 || entry.ToolMissingCount > 0
                    ? MissingColor
                    : OkColor;
                emit(FormatEntry(entry, playerName), color);
            }

            if (manifest.Unresolved.Count > 0)
            {
                string header = Language.GetTextValue(
                    "Mods.TheArchitectsBlueprintMod.UI.Manifest.Unresolved",
                    manifest.Unresolved.Count);
                emit(header, WarningColor);
                for (int i = 0; i < manifest.Unresolved.Count; i++)
                    emit($"  {manifest.Unresolved[i]}", WarningColor);
            }

            if (manifest.IsSatisfied)
            {
                emit(
                    Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.Manifest.Satisfied"),
                    OkColor);
            }
        }

        /// <summary>Formats the unpadded column header row for the manifest table.</summary>
        private static string FormatHeaderRow()
        {
            // --- Short locale labels joined with the same separators as data rows.

            return JoinRow(
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.Manifest.ColItems"),
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.Manifest.ColQty"),
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.Manifest.ColMissing"),
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.Manifest.ColMain"),
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.Manifest.ColSources"));
        }

        /// <summary>Formats one material row as pipe-separated cells without space padding.</summary>
        private static string FormatEntry(ManifestEntry entry, string playerName)
        {
            // --- Items | Qty | Missing | Main | Sources; paired missing only when tool and ammo differ.

            int total = entry.TotalCount;
            int available = entry.AvailableCount;

            return JoinRow(
                FormatItemsCell(entry),
                Language.GetTextValue(
                    "Mods.TheArchitectsBlueprintMod.UI.Manifest.EntryQty",
                    available,
                    total),
                FormatMissingCell(entry),
                Ellipsize(entry.MainName ?? string.Empty, MainWidth),
                FormatSourcesCell(entry, playerName));
        }

        /// <summary>Builds the items cell as adjacent tool+ammo tags or a single item tag, without stacks.</summary>
        private static string FormatItemsCell(ManifestEntry entry)
        {
            // --- Qty owns the count; tags stay stackless so ammo is not shown twice.

            if (entry.HasDistinctTool)
                return $"{FormatItemTag(entry.ToolItemId)}{FormatItemTag(entry.ConsumableItemId)}";

            return FormatItemTag(entry.ItemId);
        }

        /// <summary>Formats missing as tool+ammo pairs only when those items differ.</summary>
        private static string FormatMissingCell(ManifestEntry entry)
        {
            // --- Single-item rows need one missing group; wand rows keep tool then ammo.

            if (entry.HasDistinctTool)
            {
                return Language.GetTextValue(
                    "Mods.TheArchitectsBlueprintMod.UI.Manifest.EntryMissingPaired",
                    entry.ToolMissingCount,
                    entry.MissingCount);
            }

            return Language.GetTextValue(
                "Mods.TheArchitectsBlueprintMod.UI.Manifest.EntryMissing",
                entry.MissingCount);
        }

        /// <summary>Joins inventory sources into a single comma-separated sources cell.</summary>
        private static string FormatSourcesCell(ManifestEntry entry, string playerName)
        {
            // --- Empty sources stay blank so the trailing pipe still marks the column.

            if (entry.Sources.Count == 0)
                return string.Empty;

            var builder = new StringBuilder(64);
            for (int i = 0; i < entry.Sources.Count; i++)
            {
                if (i > 0)
                    builder.Append(',');
                builder.Append(FormatSource(entry.Sources[i], playerName));
            }

            return builder.ToString();
        }

        /// <summary>Formats a contributing inventory as the player name or a chest item tag.</summary>
        private static string FormatSource(ManifestInventorySource source, string playerName)
        {
            // --- Player inventories show the owner name; chests show their container item tag.

            if (source.IsPlayer)
                return string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName;

            return FormatItemTag(source.IconItemId);
        }

        /// <summary>Joins cells with the table separator, keeping empty trailing cells.</summary>
        private static string JoinRow(params string[] cells)
        {
            // --- Pipe-separate every cell in order, including a blank sources cell.

            return string.Join(CellSeparator, cells);
        }

        /// <summary>Truncates text with an ellipsis when it exceeds the character limit.</summary>
        private static string Ellipsize(string text, int width)
        {
            // --- Cap Main names by character count; do not pad.

            text ??= string.Empty;
            if (width <= 0)
                return string.Empty;

            if (text.Length <= width)
                return text;

            if (width <= 3)
                return text[..width];

            return text[..(width - 3)] + "...";
        }

        /// <summary>Formats a stackless item chat tag for the given item id.</summary>
        private static string FormatItemTag(int itemId)
        {
            // --- Manifest qty already carries counts; tags only identify the item.

            if (itemId <= ItemID.None)
                return "[i:0]";

            return $"[i:{itemId}]";
        }

        /// <summary>Wraps plain text in a vanilla color chat tag.</summary>
        private static string WrapWithColorTag(string text, Color color)
        {
            // --- Nested [i:id] tags parse inside the colored span.

            return $"[c/{color.R:X2}{color.G:X2}{color.B:X2}:{text}]";
        }

        /// <summary>Computes how many chat pages a line list needs (header lines stay on page 1).</summary>
        private static int CountPages(int lineCount)
        {
            // --- Page 1 can hold a few header lines plus BodyRowsPerPage body rows.

            if (lineCount <= 0)
                return 0;

            int headerLines = Math.Min(2, lineCount);
            int bodyLines = Math.Max(0, lineCount - headerLines);
            if (bodyLines <= BodyRowsPerPage)
                return 1;

            return 1 + (int)Math.Ceiling((bodyLines - BodyRowsPerPage) / (double)BodyRowsPerPage);
        }

        /// <summary>Resolves inclusive start and exclusive end indices for a 1-based page.</summary>
        private static void GetPageRange(int lineCount, int page, out int start, out int end)
        {
            // --- Page 1 includes header rows; later pages are pure BodyRowsPerPage slices.

            int headerLines = Math.Min(2, lineCount);
            if (page <= 1)
            {
                start = 0;
                end = Math.Min(lineCount, headerLines + BodyRowsPerPage);
                return;
            }

            int bodyStart = headerLines + BodyRowsPerPage + (page - 2) * BodyRowsPerPage;
            start = Math.Min(lineCount, bodyStart);
            end = Math.Min(lineCount, start + BodyRowsPerPage);
        }
    }
}
