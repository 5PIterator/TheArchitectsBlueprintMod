using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI.Chat;
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

            for (int i = 0; i < manifest.Entries.Count; i++)
            {
                ManifestEntry entry = manifest.Entries[i];
                Color color = entry.MissingCount > 0 ? MissingColor : OkColor;
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

        /// <summary>Formats one material line as item tag, available/total (-missing), and inventory sources.</summary>
        private static string FormatEntry(ManifestEntry entry, string playerName)
        {
            // --- [i/sTotal:id]: Available/Total (-Missing) <-- PlayerName,[i:chest],...

            int total = entry.TotalCount;
            int available = entry.AvailableCount;
            int missing = entry.MissingCount;

            var builder = new StringBuilder(96);
            builder.Append(FormatItemTag(entry.ItemId, total > 0 ? total : 1));
            builder.Append(": ");
            builder.Append(
                Language.GetTextValue(
                    "Mods.TheArchitectsBlueprintMod.UI.Manifest.EntryCounts",
                    available,
                    total,
                    missing));

            if (entry.Sources.Count > 0)
            {
                builder.Append(Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.Manifest.SourcesPrefix"));
                for (int i = 0; i < entry.Sources.Count; i++)
                {
                    if (i > 0)
                        builder.Append(',');
                    builder.Append(FormatSource(entry.Sources[i], playerName));
                }
            }

            return builder.ToString();
        }

        /// <summary>Formats a contributing inventory as the player name or a chest item tag.</summary>
        private static string FormatSource(ManifestInventorySource source, string playerName)
        {
            // --- Player inventories show the owner name; chests show their container item tag.

            if (source.IsPlayer)
                return string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName;

            return FormatItemTag(source.IconItemId, stack: 0);
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

        private static string FormatItemTag(int itemId, int stack)
        {
            if (itemId <= ItemID.None)
                return "[i:0]";

            // Prefer GenerateTag when a live Item is cheap to synthesize for stack-aware tags.
            if (stack > 1)
            {
                var item = new Item();
                item.SetDefaults(itemId);
                item.stack = stack;
                return ItemTagHandler.GenerateTag(item);
            }

            return $"[i:{itemId}]";
        }
    }
}
