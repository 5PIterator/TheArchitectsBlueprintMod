#nullable enable

using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Discovers Paint-layer interactions by pairing paint tools with paint/coating ammo.
    /// Writes Paints.itr in item-first format (item = tool, ammo = paint or coating).
    /// </summary>
    internal sealed class PaintDiscovery : DiscoveryBase
    {
        private static readonly int[] VanillaPaintTools =
        [
            ItemID.Paintbrush,
            ItemID.PaintRoller,
            ItemID.SpectrePaintbrush,
            ItemID.SpectrePaintRoller,
            ItemID.PaintSprayer,
        ];

        /// <summary>Scans loaded items and writes Paints.itr.</summary>
        public static void Run(Mod mod)
        {
            // --- Collect ammo and tools, then emit one Paint interaction per tool/ammo pair.

            ArgumentNullException.ThrowIfNull(mod);

            var outputPath = GetOutputPath("Paints");

            Log($"Paint discovery started. Output: {outputPath}");

            List<PaintAmmo> ammo = CollectAmmo();
            List<PaintTool> tools = CollectTools();
            Log($"Collected {ammo.Count} paint/coating ammo and {tools.Count} paint tools.");

            var output = new Dictionary<string, PaintItemOutput>(StringComparer.OrdinalIgnoreCase);
            int interactions = 0;
            for (int t = 0; t < tools.Count; t++)
            {
                PaintTool tool = tools[t];
                for (int a = 0; a < ammo.Count; a++)
                {
                    PaintAmmo entry = ammo[a];
                    AddInteraction(output, tool.Name, new PaintInteractionOutput
                    {
                        Layer = "Paint",
                        Main = entry.Main,
                        Yield = tool.Yield,
                        Ammo = entry.AmmoName,
                    });
                    interactions++;
                }
            }

            Log($"Emitted {interactions} paint interactions.");
            Save(mod, output);
            Log($"Paint discovery complete. Output: {outputPath}");
        }

        /// <summary>Collects every paint color and coating consumable as Paint-layer ammo.</summary>
        private static List<PaintAmmo> CollectAmmo()
        {
            // --- Prefer PaintOrCoating items; map coatings onto synthetic Paint mains.

            var result = new List<PaintAmmo>();
            var seenMains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int itemId = 0; itemId < ItemLoader.ItemCount; itemId++)
            {
                var item = new Item();
                item.SetDefaults(itemId);
                if (item.IsAir || item.type != itemId || !item.PaintOrCoating)
                    continue;

                string ammoName = ContentNameResolver.GetItemName(itemId);
                if (!TryResolvePaintMain(item, out string main))
                {
                    LogDebug($"  SKIP paint ammo (unknown coating): {ammoName} paint={item.paint} coating={item.paintCoating}");
                    continue;
                }

                if (!seenMains.Add(main))
                {
                    LogDebug($"  SKIP duplicate paint main: {main} from {ammoName}");
                    continue;
                }

                result.Add(new PaintAmmo(main, ammoName));
                LogDebug($"  AMMO {ammoName} -> main={main}");
            }

            return result;
        }

        /// <summary>Collects vanilla and SortingPriorityPainting paint tools, excluding scrapers.</summary>
        private static List<PaintTool> CollectTools()
        {
            // --- Seed known vanilla tools, then add modded painting-sort items that are not scrapers/ammo.

            var result = new List<PaintTool>();
            var seen = new HashSet<int>();

            for (int i = 0; i < VanillaPaintTools.Length; i++)
            {
                int toolId = VanillaPaintTools[i];
                if (!TryAddTool(result, seen, toolId))
                    continue;
            }

            for (int itemId = 0; itemId < ItemLoader.ItemCount; itemId++)
            {
                if (seen.Contains(itemId))
                    continue;
                if (!HasPaintingSortPriority(itemId))
                    continue;
                if (ItemID.Sets.IsPaintScraper[itemId])
                    continue;

                var item = new Item();
                item.SetDefaults(itemId);
                if (item.IsAir || item.type != itemId || item.PaintOrCoating || item.consumable)
                    continue;

                TryAddTool(result, seen, itemId);
            }

            return result;
        }

        /// <summary>Adds one paint tool with a yield heuristic derived from its id and name.</summary>
        private static bool TryAddTool(List<PaintTool> tools, HashSet<int> seen, int itemId)
        {
            // --- Resolve the tool name and assign brush/roller/sprayer/spectre yield.

            var item = new Item();
            item.SetDefaults(itemId);
            if (item.IsAir || item.type != itemId)
                return false;

            if (!seen.Add(itemId))
                return false;

            string name = ContentNameResolver.GetItemName(itemId);
            int yield = ResolveToolYield(itemId, name);
            tools.Add(new PaintTool(name, yield));
            LogDebug($"  TOOL {name} yield={yield}");
            return true;
        }

        /// <summary>Maps an ammo item onto a Paint-layer main name for colors or coatings.</summary>
        private static bool TryResolvePaintMain(Item item, out string main)
        {
            // --- Color paints use PaintID names; coatings use EchoCoating / IlluminantCoating aliases.

            if (item.paint > 0)
            {
                main = ContentNameResolver.GetPaintName(item.paint);
                return true;
            }

            if (item.paintCoating == PaintCoatingID.Echo)
            {
                main = "EchoCoating";
                return true;
            }

            if (item.paintCoating == PaintCoatingID.Glow)
            {
                main = "IlluminantCoating";
                return true;
            }

            main = string.Empty;
            return false;
        }

        /// <summary>Chooses paste yield for a paint tool from known ids and name tokens.</summary>
        private static int ResolveToolYield(int itemId, string toolName)
        {
            // --- Spectre infinite, sprayer 10, roller 4, brush-like default 1.

            string key = StripContentPrefix(toolName);
            bool isSpectre = itemId is ItemID.SpectrePaintbrush or ItemID.SpectrePaintRoller
                || key.Contains("Spectre", StringComparison.OrdinalIgnoreCase);
            if (isSpectre && (itemId is ItemID.SpectrePaintbrush or ItemID.SpectrePaintRoller
                || key.Contains("Paint", StringComparison.OrdinalIgnoreCase)
                || key.Contains("Brush", StringComparison.OrdinalIgnoreCase)
                || key.Contains("Roller", StringComparison.OrdinalIgnoreCase)))
                return -1;

            if (itemId == ItemID.PaintSprayer || key.Contains("Sprayer", StringComparison.OrdinalIgnoreCase))
                return 10;

            if (itemId == ItemID.PaintRoller
                || itemId == ItemID.SpectrePaintRoller
                || key.Contains("Roller", StringComparison.OrdinalIgnoreCase))
                return 4;

            return 1;
        }

        /// <summary>True when the item participates in the painting inventory sort group.</summary>
        private static bool HasPaintingSortPriority(int itemId)
        {
            // --- SortingPriorityPainting uses -1 for non-painting items.

            int[] priorities = ItemID.Sets.SortingPriorityPainting;
            if (itemId < 0 || itemId >= priorities.Length)
                return false;
            return priorities[itemId] >= 0;
        }

        private static string StripContentPrefix(string name)
        {
            int colon = name.IndexOf(':');
            return colon >= 0 ? name[(colon + 1)..] : name;
        }

        private static void AddInteraction(
            Dictionary<string, PaintItemOutput> output,
            string toolName,
            PaintInteractionOutput interaction)
        {
            if (!output.TryGetValue(toolName, out PaintItemOutput? group))
            {
                group = new PaintItemOutput();
                output[toolName] = group;
            }

            group.Interactions.Add(interaction);
        }

        private static void Save(Mod mod, Dictionary<string, PaintItemOutput> output)
        {
            var doc = new InteractionMapDocument<Dictionary<string, PaintItemOutput>>
            {
                Metadata = InteractionMapMetadata.Create(mod, "item-first", "paint"),
                Data = output,
            };
            SaveJson(GetOutputPath("Paints"), doc, "PaintInteractionMap");
        }

        private readonly record struct PaintAmmo(string Main, string AmmoName);

        private readonly record struct PaintTool(string Name, int Yield);

        private sealed class PaintItemOutput
        {
            public List<PaintInteractionOutput> Interactions { get; set; } = [];
        }

        private sealed class PaintInteractionOutput
        {
            public string Layer { get; set; } = "";
            public string Main { get; set; } = "";
            public int Yield { get; set; }
            public string Ammo { get; set; } = "";
        }
    }
}
