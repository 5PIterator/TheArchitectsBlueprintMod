#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Discovers tile/wall/wire tool interactions from Item.tileWand ammo links.
    /// Writes Wands.itr in item-first format. Paint tools are owned by <see cref="PaintDiscovery"/>.
    /// </summary>
    internal sealed class WandDiscovery : DiscoveryBase
    {
        private static readonly HashSet<string> MulticolorWireTools = new(StringComparer.OrdinalIgnoreCase)
        {
            "MulticolorWrench",
            "TheGrandDesign",
            "WireKite",
            "CombatWrench",
        };

        private static readonly Dictionary<string, string> SingleWireToolColors = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Wrench"] = "Red",
            ["GreenWrench"] = "Green",
            ["BlueWrench"] = "Blue",
            ["YellowWrench"] = "Yellow",
        };

        private static readonly (string Token, string Color)[] WireColorNameTokens =
        [
            ("Yellow", "Yellow"),
            ("Green", "Green"),
            ("Blue", "Blue"),
            ("Red", "Red"),
        ];

        /// <summary>Scans loaded items and writes Wands.itr to SourceMaps.</summary>
        public static void Run(Mod mod, Action<string> msg)
        {
            ArgumentNullException.ThrowIfNull(mod);
            ArgumentNullException.ThrowIfNull(msg);

            var debugPath = GetDebugPath("WandDiscovery_Debug.txt");
            var outputPath = GetOutputPath("Wands");
            using var debugWriter = new StreamWriter(debugPath, append: false);

            void Log(string s)
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {s}";
                debugWriter.WriteLine(line);
                debugWriter.Flush();
                msg(s);
            }

            Log($"Wand discovery started. Output: {outputPath}");
            Log($"Debug log: {debugPath}");

            // --- Resolve shared ammo ids once, then scan every item for tileWand links.
            int wireItemId = ItemID.Wire;
            int? actuatorItemId = ContentNameResolver.ResolveItem("Actuator");

            var output = new Dictionary<string, WandItemOutput>(StringComparer.OrdinalIgnoreCase);
            int tileWands = 0;
            int wallWands = 0;
            int wireTools = 0;
            int actuatorTools = 0;
            int skippedFlexible = 0;
            int skippedUnknown = 0;

            for (int itemId = 0; itemId < ItemLoader.ItemCount; itemId++)
            {
                var item = new Item();
                item.SetDefaults(itemId);
                if (item.IsAir || item.type != itemId)
                    continue;

                if (item.GetFlexibleTileWand() is not null)
                {
                    skippedFlexible++;
                    Log($"  SKIP flexible wand: {ContentNameResolver.GetItemName(itemId)}");
                    continue;
                }

                if (item.tileWand <= 0)
                    continue;

                string toolName = ContentNameResolver.GetItemName(itemId);
                string ammoName = ContentNameResolver.GetItemName(item.tileWand);

                if (TryAddTileWandInteraction(output, item, toolName, ammoName))
                {
                    tileWands++;
                    continue;
                }

                if (TryAddWallWandInteraction(output, item, toolName, ammoName))
                {
                    wallWands++;
                    continue;
                }

                if (item.tileWand == wireItemId)
                {
                    IReadOnlyList<string> wireKinds = ResolveWireKinds(toolName);
                    if (wireKinds.Count == 0)
                    {
                        skippedUnknown++;
                        Log($"  SKIP wire tool (unknown color): {toolName}");
                        continue;
                    }

                    for (int i = 0; i < wireKinds.Count; i++)
                    {
                        AddInteraction(output, toolName, new WandInteractionOutput
                        {
                            Layer = "Wire",
                            Main = wireKinds[i],
                            Yield = 1,
                            Ammo = ammoName,
                        });
                    }

                    wireTools++;
                    continue;
                }

                if (actuatorItemId.HasValue && item.tileWand == actuatorItemId.Value)
                {
                    AddInteraction(output, toolName, new WandInteractionOutput
                    {
                        Layer = "Wire",
                        Main = "Actuator",
                        Yield = 1,
                        Ammo = ammoName,
                    });
                    actuatorTools++;
                    continue;
                }

                skippedUnknown++;
                Log($"  SKIP tileWand tool: {toolName} (ammo={ammoName}, createTile={item.createTile}, createWall={item.createWall})");
            }

            Log($"Discovered {tileWands} tile wands, {wallWands} wall wands, {wireTools} wire tools, {actuatorTools} actuator tools.");
            if (skippedFlexible > 0)
                Log($"Skipped {skippedFlexible} flexible tile wands.");
            if (skippedUnknown > 0)
                Log($"Skipped {skippedUnknown} unrecognized tileWand tools (see debug log).");

            Save(mod, output, Log);
            Log($"Wand discovery complete. Output: {outputPath}");
        }

        /// <summary>Adds a tile placement wand when createTile and tileWand are set on a non-consumable tool.</summary>
        private static bool TryAddTileWandInteraction(
            Dictionary<string, WandItemOutput> output,
            Item item,
            string toolName,
            string ammoName)
        {
            // --- Consumable placeables belong in Placement.itr, not wand discovery.
            if (item.createTile < TileID.Dirt || item.consumable)
                return false;

            string tileName = ContentNameResolver.GetTileName(item.createTile);
            var interaction = new WandInteractionOutput
            {
                Layer = "Tile",
                Main = tileName,
                Yield = 1,
                Ammo = ammoName,
            };

            if (item.placeStyle >= 0)
                interaction.Style = item.placeStyle;

            AddInteraction(output, toolName, interaction);
            return true;
        }

        /// <summary>Adds a wall placement wand when createWall and tileWand are set on a non-consumable tool.</summary>
        private static bool TryAddWallWandInteraction(
            Dictionary<string, WandItemOutput> output,
            Item item,
            string toolName,
            string ammoName)
        {
            if (item.createWall < 0 || item.consumable)
                return false;

            AddInteraction(output, toolName, new WandInteractionOutput
            {
                Layer = "Wall",
                Main = ContentNameResolver.GetWallName(item.createWall),
                Yield = 1,
                Ammo = ammoName,
            });
            return true;
        }

        /// <summary>Resolves wire colors for a wrench-like tool from known ids or name tokens.</summary>
        private static IReadOnlyList<string> ResolveWireKinds(string toolName)
        {
            string key = StripContentPrefix(toolName);

            if (MulticolorWireTools.Contains(key))
                return ["Red", "Green", "Blue", "Yellow"];

            if (SingleWireToolColors.TryGetValue(key, out string? single))
                return [single];

            for (int i = 0; i < WireColorNameTokens.Length; i++)
            {
                (string token, string color) = WireColorNameTokens[i];
                if (key.Contains(token, StringComparison.OrdinalIgnoreCase))
                    return [color];
            }

            return [];
        }

        private static string StripContentPrefix(string name)
        {
            int colon = name.IndexOf(':');
            return colon >= 0 ? name[(colon + 1)..] : name;
        }

        private static void AddInteraction(
            Dictionary<string, WandItemOutput> output,
            string toolName,
            WandInteractionOutput interaction)
        {
            if (!output.TryGetValue(toolName, out WandItemOutput? group))
            {
                group = new WandItemOutput();
                output[toolName] = group;
            }

            group.Interactions.Add(interaction);
        }

        private static void Save(Mod mod, Dictionary<string, WandItemOutput> output, Action<string> log)
        {
            // --- Emit specialized wire tools before multipurpose ones so load order matches coverage sort.

            var ordered = new Dictionary<string, WandItemOutput>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, WandItemOutput> kvp in output)
            {
                if (!IsMulticolorWireTool(kvp.Key))
                    ordered[kvp.Key] = kvp.Value;
            }

            foreach (KeyValuePair<string, WandItemOutput> kvp in output)
            {
                if (IsMulticolorWireTool(kvp.Key))
                    ordered[kvp.Key] = kvp.Value;
            }

            var doc = new InteractionMapDocument<Dictionary<string, WandItemOutput>>
            {
                Metadata = InteractionMapMetadata.Create(mod, "item-first", "wand"),
                Data = ordered,
            };
            SaveJson(GetOutputPath("Wands"), doc, log, "WandInteractionMap");
        }

        /// <summary>Whether the tool name is a known multipurpose wire wrench.</summary>
        private static bool IsMulticolorWireTool(string toolName)
        {
            // --- Match the curated MulticolorWireTools set after stripping content prefixes.

            return MulticolorWireTools.Contains(StripContentPrefix(toolName));
        }

        private sealed class WandItemOutput
        {
            public List<WandInteractionOutput> Interactions { get; set; } = [];
        }

        private sealed class WandInteractionOutput
        {
            public string Layer { get; set; } = "";
            public string Main { get; set; } = "";
            public int? Style { get; set; }
            public int Yield { get; set; }
            public string Ammo { get; set; } = "";
        }
    }
}
