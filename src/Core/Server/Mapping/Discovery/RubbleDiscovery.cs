#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Discovers FlexibleTileWand placement options (Rubblemaker, Mitey-Titey, and related tools).
    /// Writes Rubble.itr.json in item-first format with tool alias and optional fake place targets.
    /// </summary>
    internal sealed class RubbleDiscovery : DiscoveryBase
    {
        private static readonly Regex SizeTokenRegex = new(
            @"(1x1|2x1|1x2|2x2|3x2)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly FieldInfo? OptionsField =
            typeof(FlexibleTileWand).GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>Scans flexible tile wands and writes Rubble.itr.json to SourceMaps.</summary>
        public static void Run(Mod mod, Action<string> msg)
        {
            // --- Enumerate Rubblemaker tables (merged), pots, then other flexible wands.

            ArgumentNullException.ThrowIfNull(mod);
            ArgumentNullException.ThrowIfNull(msg);

            var debugPath = GetDebugPath("RubbleDiscovery_Debug.txt");
            var outputPath = GetOutputPath("Rubble");
            using var debugWriter = new StreamWriter(debugPath, append: false);

            void Log(string s)
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {s}";
                debugWriter.WriteLine(line);
                debugWriter.Flush();
                msg(s);
            }

            Log($"Rubble discovery started. Output: {outputPath}");
            Log($"Debug log: {debugPath}");

            if (OptionsField is null)
            {
                Log("FAIL: FlexibleTileWand._options field not found.");
                return;
            }

            // Resolve Rubblemaker mode item names for the canonical tool + alias list.
            if (!TryResolveRubblemakerItems(out int smallId, out string smallName, out List<string> modeAliases, Log))
            {
                Log("FAIL: Could not resolve Rubblemaker item ids.");
                return;
            }

            var createTileItems = BuildCreateTileItemSet();
            var output = new Dictionary<string, RubbleItemOutput>(StringComparer.OrdinalIgnoreCase);
            int optionCount = 0;

            // Merge Small/Medium/Large rubble tables onto the canonical Rubblemaker key.
            optionCount += EmitWandOptions(
                output,
                FlexibleTileWand.RubblePlacementSmall,
                smallName,
                modeAliases,
                createTileItems,
                Log);
            optionCount += EmitWandOptions(
                output,
                FlexibleTileWand.RubblePlacementMedium,
                smallName,
                modeAliases,
                createTileItems,
                Log);
            optionCount += EmitWandOptions(
                output,
                FlexibleTileWand.RubblePlacementLarge,
                smallName,
                modeAliases,
                createTileItems,
                Log);

            // Unused pot recipes: throwaway wand so live Rubblemaker tables stay untouched.
            optionCount += EmitPotsOptions(output, smallName, modeAliases, createTileItems, Log);

            // Remaining flexible wands (Mitey-Titey, Sandcastle, Portable Kiln, modded).
            var seenWands = new HashSet<FlexibleTileWand>();
            seenWands.Add(FlexibleTileWand.RubblePlacementSmall);
            seenWands.Add(FlexibleTileWand.RubblePlacementMedium);
            seenWands.Add(FlexibleTileWand.RubblePlacementLarge);

            for (int itemId = 0; itemId < ItemLoader.ItemCount; itemId++)
            {
                var item = new Item();
                item.SetDefaults(itemId);
                if (item.IsAir || item.type != itemId)
                    continue;

                FlexibleTileWand? wand = item.GetFlexibleTileWand();
                if (wand is null || !seenWands.Add(wand))
                    continue;

                if (itemId == smallId)
                    continue;

                string toolName = ContentNameResolver.GetItemName(itemId);
                optionCount += EmitWandOptions(output, wand, toolName, aliasNames: null, createTileItems, Log);
                Log($"  Flexible wand: {toolName}");
            }

            Save(mod, output, Log);
            Log($"Discovered {optionCount} rubble placement options across {output.Count} tools.");
            Log($"Rubble discovery complete. Output: {outputPath}");
        }

        /// <summary>Adds ForModders pot variations under the Rubblemaker key.</summary>
        private static int EmitPotsOptions(
            Dictionary<string, RubbleItemOutput> output,
            string rubblemakerName,
            List<string> modeAliases,
            HashSet<int> createTileItems,
            Action<string> log)
        {
            // --- Build a throwaway wand, register unused pot recipes, then emit dual rows.

            var wand = new FlexibleTileWand();
            int echoPileStyle = 0;
            ushort tileType = TileID.PotsEcho;
            FlexibleTileWand.ForModders_AddPotsToWand(wand, ref echoPileStyle, ref tileType);
            int added = EmitWandOptions(output, wand, rubblemakerName, modeAliases, createTileItems, log);
            log($"  PotsEcho options via ForModders_AddPotsToWand: {added}");
            return added;
        }

        /// <summary>Enumerates one FlexibleTileWand options table into the output map.</summary>
        private static int EmitWandOptions(
            Dictionary<string, RubbleItemOutput> output,
            FlexibleTileWand wand,
            string toolName,
            List<string>? aliasNames,
            HashSet<int> createTileItems,
            Action<string> log)
        {
            // --- Reflect _options (ammo → PlacementOption list) and emit natural+fake and echo-as-main rows.

            if (OptionsField?.GetValue(wand) is not IDictionary options)
            {
                log($"  WARN: could not read _options for {toolName}");
                return 0;
            }

            int added = 0;
            foreach (DictionaryEntry entry in options)
            {
                if (entry.Key is not int ammoItemId || ammoItemId <= 0)
                    continue;

                string ammoName = ContentNameResolver.GetItemName(ammoItemId);
                if (!TryGetPlacementOptions(entry.Value, out IEnumerable optionsList))
                    continue;

                foreach (object rawOption in optionsList)
                {
                    if (!TryReadPlacementOption(rawOption, out int tileId, out int style))
                        continue;

                    string echoName = ContentNameResolver.GetTileName(tileId);
                    added += AddDualRows(output, toolName, aliasNames, echoName, style, ammoName, createTileItems);
                }
            }

            return added;
        }

        /// <summary>Emits the Echo-as-main row and, when possible, the natural+fake row.</summary>
        private static int AddDualRows(
            Dictionary<string, RubbleItemOutput> output,
            string toolName,
            List<string>? aliasNames,
            string echoName,
            int style,
            string ammoName,
            HashSet<int> createTileItems)
        {
            // --- Always emit Echo as main; also emit natural+fake when a counterpart resolves and has no createTile item.

            int added = 0;
            if (AddInteraction(output, toolName, new RubbleInteractionOutput
            {
                Layer = "Tile",
                Main = echoName,
                Style = style,
                Yield = 1,
                Ammo = ammoName,
                Alias = aliasNames is { Count: > 0 } ? [.. aliasNames] : null,
            }))
                added++;

            if (!TryDeriveNaturalTileName(echoName, out string naturalName))
                return added;

            int? naturalId = ContentNameResolver.ResolveTile(naturalName);
            if (!naturalId.HasValue || naturalId.Value < 0)
                return added;

            // Skip natural rows when a consumable createTile item already places this tile.
            if (createTileItems.Contains(naturalId.Value))
                return added;

            if (AddInteraction(output, toolName, new RubbleInteractionOutput
            {
                Layer = "Tile",
                Main = naturalName,
                Fake = echoName,
                Style = style,
                Yield = 1,
                Ammo = ammoName,
                Alias = aliasNames is { Count: > 0 } ? [.. aliasNames] : null,
            }))
                added++;

            return added;
        }

        /// <summary>Adds an interaction when the (main, style, ammo, fake) tuple is new for the tool.</summary>
        private static bool AddInteraction(
            Dictionary<string, RubbleItemOutput> output,
            string toolName,
            RubbleInteractionOutput interaction)
        {
            // --- Deduplicate so Small/Medium/Large table merges do not triple identical rows.

            if (!output.TryGetValue(toolName, out RubbleItemOutput? group))
            {
                group = new RubbleItemOutput();
                output[toolName] = group;
            }

            string key = $"{interaction.Main}|{interaction.Style}|{interaction.Ammo}|{interaction.Fake ?? ""}";
            if (!group.SeenKeys.Add(key))
                return false;

            group.Interactions.Add(interaction);
            return true;
        }

        /// <summary>Strips Echo/Replica and size tokens to derive a natural tile name.</summary>
        private static bool TryDeriveNaturalTileName(string echoName, out string naturalName)
        {
            // --- SmallPiles1x1Echo → SmallPiles; LifeFruitReplica → LifeFruit; PotsEcho → Pots.

            naturalName = "";
            if (string.IsNullOrEmpty(echoName))
                return false;

            string stem = echoName;
            if (stem.EndsWith("Replica", StringComparison.OrdinalIgnoreCase))
                stem = stem[..^"Replica".Length];
            else if (stem.EndsWith("Echo", StringComparison.OrdinalIgnoreCase))
                stem = stem[..^"Echo".Length];
            else
                return false;

            stem = SizeTokenRegex.Replace(stem, "");
            if (string.IsNullOrEmpty(stem))
                return false;

            naturalName = stem;
            return true;
        }

        /// <summary>Resolves Rubblemaker Small and lists Medium/Large names for alias.</summary>
        private static bool TryResolveRubblemakerItems(
            out int smallId,
            out string smallName,
            out List<string> aliases,
            Action<string> log)
        {
            // --- Scan items bound to the three RubblePlacement* tables.

            smallId = 0;
            smallName = "";
            aliases = [];
            var modeIds = new List<(int Id, string Name, FlexibleTileWand Wand)>();

            for (int itemId = 0; itemId < ItemLoader.ItemCount; itemId++)
            {
                var item = new Item();
                item.SetDefaults(itemId);
                if (item.IsAir || item.type != itemId)
                    continue;

                FlexibleTileWand? wand = item.GetFlexibleTileWand();
                if (wand is null)
                    continue;

                if (!ReferenceEquals(wand, FlexibleTileWand.RubblePlacementSmall)
                    && !ReferenceEquals(wand, FlexibleTileWand.RubblePlacementMedium)
                    && !ReferenceEquals(wand, FlexibleTileWand.RubblePlacementLarge))
                    continue;

                string name = ContentNameResolver.GetItemName(itemId);
                modeIds.Add((itemId, name, wand));
                log($"  Rubblemaker mode: {name} ({itemId})");
            }

            if (modeIds.Count == 0)
                return false;

            for (int i = 0; i < modeIds.Count; i++)
            {
                if (ReferenceEquals(modeIds[i].Wand, FlexibleTileWand.RubblePlacementSmall))
                {
                    smallId = modeIds[i].Id;
                    smallName = modeIds[i].Name;
                    break;
                }
            }

            if (smallId <= 0)
            {
                modeIds.Sort((a, b) => a.Id.CompareTo(b.Id));
                smallId = modeIds[0].Id;
                smallName = modeIds[0].Name;
            }

            for (int i = 0; i < modeIds.Count; i++)
            {
                if (modeIds[i].Id == smallId)
                    continue;
                aliases.Add(modeIds[i].Name);
            }

            return true;
        }

        /// <summary>Builds the set of tile ids that have a consumable createTile item.</summary>
        private static HashSet<int> BuildCreateTileItemSet()
        {
            // --- Used to skip natural+fake rows when a real placeable item already exists.

            var set = new HashSet<int>();
            for (int itemId = 0; itemId < ItemLoader.ItemCount; itemId++)
            {
                var item = new Item();
                item.SetDefaults(itemId);
                if (item.IsAir || item.type != itemId || !item.consumable)
                    continue;
                if (item.createTile >= TileID.Dirt)
                    set.Add(item.createTile);
            }

            return set;
        }

        /// <summary>Reads the Options list from an OptionBucket via reflection.</summary>
        private static bool TryGetPlacementOptions(object? bucket, out IEnumerable options)
        {
            // --- OptionBucket stores PlacementOption entries in a public or private list field/property.

            options = Array.Empty<object>();
            if (bucket is null)
                return false;

            Type bucketType = bucket.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (string name in new[] { "Options", "options", "_options" })
            {
                PropertyInfo? prop = bucketType.GetProperty(name, flags);
                if (prop?.GetValue(bucket) is IEnumerable fromProp)
                {
                    options = fromProp;
                    return true;
                }

                FieldInfo? field = bucketType.GetField(name, flags);
                if (field?.GetValue(bucket) is IEnumerable fromField)
                {
                    options = fromField;
                    return true;
                }
            }

            // Fall back: first IEnumerable instance field that is not a string.
            foreach (FieldInfo field in bucketType.GetFields(flags))
            {
                if (field.FieldType == typeof(string))
                    continue;
                if (field.GetValue(bucket) is IEnumerable enumerable and not string)
                {
                    options = enumerable;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Reads TileIdToPlace and TileStyleToPlace from a PlacementOption.</summary>
        private static bool TryReadPlacementOption(object option, out int tileId, out int style)
        {
            // --- Prefer public fields on FlexibleTileWand.PlacementOption.

            tileId = 0;
            style = 0;
            Type type = option.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            FieldInfo? tileField = type.GetField("TileIdToPlace", flags);
            FieldInfo? styleField = type.GetField("TileStyleToPlace", flags);
            if (tileField is null || styleField is null)
                return false;

            object? tileValue = tileField.GetValue(option);
            object? styleValue = styleField.GetValue(option);
            if (tileValue is not int t || styleValue is not int s)
                return false;

            tileId = t;
            style = s;
            return tileId > 0;
        }

        private static void Save(Mod mod, Dictionary<string, RubbleItemOutput> output, Action<string> log)
        {
            // --- Strip dedupe keys before serialize; write item-first rubble mapType.

            var serializable = new Dictionary<string, RubbleItemOutputDto>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, RubbleItemOutput> kvp in output)
            {
                serializable[kvp.Key] = new RubbleItemOutputDto
                {
                    Interactions = kvp.Value.Interactions,
                };
            }

            var doc = new InteractionMapDocument<Dictionary<string, RubbleItemOutputDto>>
            {
                Metadata = InteractionMapMetadata.Create(mod, "item-first", "rubble"),
                Data = serializable,
            };
            SaveJson(GetOutputPath("Rubble"), doc, log, "RubbleInteractionMap");
        }

        private sealed class RubbleItemOutput
        {
            public List<RubbleInteractionOutput> Interactions { get; set; } = [];
            public HashSet<string> SeenKeys { get; } = new(StringComparer.Ordinal);
        }

        private sealed class RubbleItemOutputDto
        {
            public List<RubbleInteractionOutput> Interactions { get; set; } = [];
        }

        private sealed class RubbleInteractionOutput
        {
            public string Layer { get; set; } = "";
            public string Main { get; set; } = "";
            public string? Fake { get; set; }
            public int? Style { get; set; }
            public int Yield { get; set; }
            public string Ammo { get; set; } = "";
            public List<string>? Alias { get; set; }
        }
    }
}
