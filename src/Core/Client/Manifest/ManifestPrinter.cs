using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Prints a projection paste manifest into local chat and the mod log.</summary>
    internal static class ManifestPrinter
    {
        private static readonly Color HeaderColor = new(135, 206, 250);
        private static readonly Color OkColor = new(144, 238, 144);
        private static readonly Color MissingColor = new(255, 160, 122);
        private static readonly Color WarningColor = new(255, 215, 0);

        private const int MainWidth = 35;
        private const string CellSeparator = " | ";

        /// <summary>Builds and prints the manifest for the given projection into local chat and logs.</summary>
        public static void Print(Projection projection)
        {
            // --- Guard projection state, compute demand, emit lines, then flush the info batch once.

            if (Main.dedServ)
                return;

            if (projection is not { IsDefined: true })
            {
                Emit(
                    Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.Manifest.NoProjection"),
                    WarningColor);
                TABLog.Flush();
                return;
            }

            if (!projection.HasPayload)
            {
                Emit(
                    Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.Manifest.NoPayload"),
                    WarningColor);
                TABLog.Flush();
                return;
            }

            Manifest manifest = ManifestBuilder.Build(projection);
            PrintManifest(manifest, ResolveOwnerName(projection));
            TABLog.Flush();
        }

        private static void PrintManifest(Manifest manifest, string playerName)
        {
            Emit(
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.Manifest.Header"),
                HeaderColor);

            if (manifest.IsEmpty)
            {
                Emit(
                    Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.Manifest.Empty"),
                    OkColor);
                return;
            }

            Emit(FormatHeaderRow(), HeaderColor);

            for (int i = 0; i < manifest.Entries.Count; i++)
            {
                ManifestEntry entry = manifest.Entries[i];
                Color color = entry.MissingCount > 0 || entry.ToolMissingCount > 0
                    ? MissingColor
                    : OkColor;
                Emit(FormatEntry(entry, playerName), color);
            }

            if (manifest.Unresolved.Count > 0)
            {
                // --- Chat lists one name per line; the info log keeps names on the header line so
                // --- multiline continuations (no timestamp) are not lost when copying client logs.

                string header = Language.GetTextValue(
                    "Mods.TheArchitectsBlueprintMod.UI.Manifest.Unresolved",
                    manifest.Unresolved.Count);
                Main.NewText(header, WarningColor);
                for (int i = 0; i < manifest.Unresolved.Count; i++)
                    Main.NewText($"  {manifest.Unresolved[i]}", WarningColor);

                TABLog.Info(
                    $"{header} {string.Join(", ", manifest.Unresolved)}",
                    TABLog.ManifestCategory,
                    chatColor: WarningColor,
                    ownerPlayer: null,
                    echoToChat: false);
            }

            if (manifest.IsSatisfied)
            {
                Emit(
                    Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.Manifest.Satisfied"),
                    OkColor);
            }
        }

        /// <summary>Writes one manifest line to chat and queues it for the batched mod info log.</summary>
        private static void Emit(string message, Color color)
        {
            // --- Chat stays colored and immediate; info matches other TABLog batches (one Logger write on Flush).

            Main.NewText(message, color);
            TABLog.Info(
                message,
                TABLog.ManifestCategory,
                chatColor: color,
                ownerPlayer: null,
                echoToChat: false);
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

        /// <summary>Resolves the projection owner's display name for player inventory sources.</summary>
        private static string ResolveOwnerName(Projection projection)
        {
            // --- Prefer the live owner player; fall back to the local client name.

            int owner = projection.Owner;
            if (owner >= 0 && owner < Main.player.Length)
            {
                Player player = Main.player[owner];
                if (player is { active: true } && !string.IsNullOrWhiteSpace(player.name))
                    return player.name;
            }

            return Main.LocalPlayer?.name ?? string.Empty;
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
    }
}
