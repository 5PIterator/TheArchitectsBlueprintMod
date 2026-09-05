#nullable enable

using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Discovers semi-custom conversion interactions for tile/wall families.
    /// Family handlers encode the conversion rule; member lists are scanned at run time.
    /// Writes Conversion.itr.json in item-first format. Ammo-based conversion entries sort below
    /// direct placeables via <see cref="InteractionPriorityComparer"/>.
    /// </summary>
    internal sealed class ConversionDiscovery : DiscoveryBase
    {
        private const string SpectrePaintRollerItem = "SpectrePaintRoller";

        /// <summary>Runs all conversion family handlers and writes Conversion.itr.json.</summary>
        public static void Run(Mod mod)
        {
            // --- Build shared indexes once, then let each family append interactions into one item-first map.

            ArgumentNullException.ThrowIfNull(mod);

            var outputPath = GetOutputPath("Conversion");

            Log($"Conversion discovery started. Output: {outputPath}");

            IReadOnlyDictionary<int, int> createWallItems = BuildCreateWallItemIndex();
            var output = new Dictionary<string, ConversionItemOutput>(StringComparer.OrdinalIgnoreCase);
            int total = 0;

            total += DiscoverWallConversionFamily(output, createWallItems);

            Log($"Discovered {total} conversion interactions across families.");
            Save(mod, output);
            Log($"Conversion discovery complete. Output: {outputPath}");
        }

        /// <summary>
        /// Maps craftable walls and natural unsafes onto Spectre Paint Roller + wall-item ammo.
        /// </summary>
        private static int DiscoverWallConversionFamily(
            Dictionary<string, ConversionItemOutput> output,
            IReadOnlyDictionary<int, int> createWallItems)
        {
            // --- Craftable walls use their createWall item as ammo; unsafes without createWall use wallBlend.

            Log("Family: WallConversion");
            if (!ContentNameResolver.ResolveItem(SpectrePaintRollerItem).HasValue)
            {
                LogWarn("  SKIP WallConversion: SpectrePaintRoller item unresolved.");
                return 0;
            }

            var coveredWalls = new HashSet<int>();
            int craftable = 0;
            int unsafeOnly = 0;

            // Craftable walls: roller + the placeable wall item (sorts below direct place via ammo priority).
            foreach (KeyValuePair<int, int> kvp in createWallItems)
            {
                int wallId = kvp.Key;
                int ammoItemId = kvp.Value;
                if (wallId <= 0 || ammoItemId <= 0)
                    continue;

                AddWallConversion(output, wallId, ammoItemId, "craftable");
                coveredWalls.Add(wallId);
                craftable++;
            }

            // Natural unsafes with no createWall: roller + related placeable ammo via wallBlend.
            for (int wallId = 1; wallId < WallLoader.WallCount; wallId++)
            {
                if (coveredWalls.Contains(wallId))
                    continue;

                if (wallId >= Main.wallHouse.Length || Main.wallHouse[wallId])
                    continue;

                string wallName = ContentNameResolver.GetWallName(wallId);
                if (wallName.Contains("Unloaded", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!TryResolveRelatedWallItem(wallId, createWallItems, out int ammoItemId))
                {
                    LogDebug($"  SKIP {wallName} ({wallId}): no related placeable wall item.");
                    continue;
                }

                AddWallConversion(output, wallId, ammoItemId, "unsafe");
                coveredWalls.Add(wallId);
                unsafeOnly++;
            }

            Log($"  WallConversion summary: {craftable} craftable, {unsafeOnly} unsafe-only, {craftable + unsafeOnly} total.");
            return craftable + unsafeOnly;
        }

        /// <summary>Emits one Spectre Paint Roller wall conversion for the given main wall and ammo item.</summary>
        private static void AddWallConversion(
            Dictionary<string, ConversionItemOutput> output,
            int wallId,
            int ammoItemId,
            string source)
        {
            // --- Record roller tool + wall-item ammo for this wall main.
            string wallName = ContentNameResolver.GetWallName(wallId);
            string ammoName = ContentNameResolver.GetItemName(ammoItemId);
            AddInteraction(output, SpectrePaintRollerItem, new ConversionInteractionOutput
            {
                Layer = "Wall",
                Main = wallName,
                Yield = 1,
                Ammo = ammoName,
            });
            LogDebug($"  {wallName} ({wallId}) -> {SpectrePaintRollerItem} ammo={ammoName} ({source})");
        }

        /// <summary>Builds createWall wall-id -> item-id for every placeable wall item.</summary>
        private static Dictionary<int, int> BuildCreateWallItemIndex()
        {
            // --- First item that places each wall wins; later duplicates are ignored.
            var index = new Dictionary<int, int>();
            for (int itemId = 0; itemId < ItemLoader.ItemCount; itemId++)
            {
                var item = new Item();
                item.SetDefaults(itemId);
                if (item.IsAir || item.type != itemId || item.createWall < 0)
                    continue;

                if (!index.ContainsKey(item.createWall))
                    index[item.createWall] = itemId;
            }

            return index;
        }

        /// <summary>Resolves the placeable wall item linked to an unsafe wall via createWall, wallBlend, or name.</summary>
        private static bool TryResolveRelatedWallItem(
            int unsafeWallId,
            IReadOnlyDictionary<int, int> createWallItems,
            out int ammoItemId)
        {
            // --- Prefer exact createWall, then wallBlend, then Echo/safe names (Cave6Unsafe -> Cave6Echo).

            ammoItemId = 0;

            if (createWallItems.TryGetValue(unsafeWallId, out int exactItemId) && exactItemId > 0)
            {
                ammoItemId = exactItemId;
                return true;
            }

            if (unsafeWallId >= 0 && unsafeWallId < Main.wallBlend.Length)
            {
                int blend = Main.wallBlend[unsafeWallId];
                if (blend != unsafeWallId
                    && createWallItems.TryGetValue(blend, out int blendItemId)
                    && blendItemId > 0)
                {
                    ammoItemId = blendItemId;
                    return true;
                }
            }

            foreach (KeyValuePair<int, int> kvp in createWallItems)
            {
                int safeWallId = kvp.Key;
                if (safeWallId < 0 || safeWallId >= Main.wallBlend.Length)
                    continue;

                int safeBlend = Main.wallBlend[safeWallId];
                if (safeBlend == unsafeWallId || Main.wallBlend[unsafeWallId] == safeWallId)
                {
                    ammoItemId = kvp.Value;
                    return true;
                }
            }

            return TryResolveRelatedWallItemByName(unsafeWallId, createWallItems, out ammoItemId);
        }

        /// <summary>Matches an unsafe wall to a placeable Echo or safe wall by stripping Unsafe and adding Echo.</summary>
        private static bool TryResolveRelatedWallItemByName(
            int unsafeWallId,
            IReadOnlyDictionary<int, int> createWallItems,
            out int ammoItemId)
        {
            // --- Cave6Unsafe -> Cave6Echo; FlowerUnsafe -> Flower; CaveWall -> CaveWall1Echo.

            ammoItemId = 0;
            string name = ContentNameResolver.GetWallName(unsafeWallId);
            if (string.IsNullOrEmpty(name) || name.StartsWith("Wall_", StringComparison.Ordinal))
                return false;

            string stem = name.EndsWith("Unsafe", StringComparison.OrdinalIgnoreCase)
                ? name[..^"Unsafe".Length]
                : name;
            if (string.IsNullOrEmpty(stem))
                return false;

            var candidates = new List<string> { stem + "Echo", stem, name + "Echo" };
            if (stem.Length > 0 && !char.IsDigit(stem[^1]))
                candidates.Add(stem + "1Echo");

            for (int i = 0; i < candidates.Count; i++)
            {
                int? wallId = ContentNameResolver.ResolveWall(candidates[i]);
                if (!wallId.HasValue || wallId.Value == unsafeWallId)
                    continue;

                if (createWallItems.TryGetValue(wallId.Value, out int itemId) && itemId > 0)
                {
                    ammoItemId = itemId;
                    return true;
                }
            }

            return false;
        }

        private static void AddInteraction(
            Dictionary<string, ConversionItemOutput> output,
            string toolName,
            ConversionInteractionOutput interaction)
        {
            if (!output.TryGetValue(toolName, out ConversionItemOutput? group))
            {
                group = new ConversionItemOutput();
                output[toolName] = group;
            }

            group.Interactions.Add(interaction);
        }

        private static void Save(Mod mod, Dictionary<string, ConversionItemOutput> output)
        {
            var doc = new InteractionMapDocument<Dictionary<string, ConversionItemOutput>>
            {
                Metadata = InteractionMapMetadata.Create(mod, "item-first", "conversion"),
                Data = output,
            };
            SaveJson(GetOutputPath("Conversion"), doc, "ConversionInteractionMap");
        }

        private sealed class ConversionItemOutput
        {
            public List<ConversionInteractionOutput> Interactions { get; set; } = [];
        }

        private sealed class ConversionInteractionOutput
        {
            public string Layer { get; set; } = "";
            public string Main { get; set; } = "";
            public int Yield { get; set; }
            public string Ammo { get; set; } = "";
        }
    }
}
