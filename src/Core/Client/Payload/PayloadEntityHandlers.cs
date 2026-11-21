using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Tile_Entities;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Per-kind capture/apply behavior. <see cref="PayloadEntityHandlers.Resolve"/> fills null members from Generic.
    /// </summary>
    internal readonly struct PayloadEntityHandler
    {
        public Func<TileEntity, TagCompound?>? CaptureTe { get; init; }
        public Func<TagCompound, TagCompound?>? Sanitize { get; init; }
        public Func<int, int, TagCompound, bool>? Apply { get; init; }
    }

    /// <summary>
    /// Default TileEntity path plus a FullName table of custom handlers (chest, sign, display doll).
    /// </summary>
    internal static class PayloadEntityHandlers
    {
        private const string LogCategory = "Paste";
        private const string EntityCaptureLog = "EntityCapture";
        private const string EntityPlaceLog = "EntityPlace";
        private const int DisplayDollSlotCount = 8;

        private static readonly FieldInfo? DisplayDollItemsField = typeof(TEDisplayDoll).GetField(
            "_items",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? DisplayDollDyesField = typeof(TEDisplayDoll).GetField(
            "_dyes",
            BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>Default path: exact ByPosition, SaveData/LoadData.</summary>
        public static readonly PayloadEntityHandler Generic = new()
        {
            CaptureTe = CaptureGenericTileEntity,
            Sanitize = SanitizeGenericTileEntityData,
            Apply = TryApplyGenericTileEntity,
        };

        private static readonly Dictionary<string, PayloadEntityHandler> ByName = new(StringComparer.Ordinal)
        {
            [PayloadEntityNames.Chest] = new()
            {
                Sanitize = SanitizeChestData,
                Apply = TryApplyChestData,
            },
            [PayloadEntityNames.Sign] = new()
            {
                Sanitize = SanitizeSignData,
                Apply = TryApplySignData,
            },
            [PayloadEntityNames.DisplayDoll] = new()
            {
                CaptureTe = CaptureDisplayDoll,
                Sanitize = SanitizeDisplayDollData,
                Apply = TryApplyDisplayDoll,
            },
        };

        /// <summary>Returns the custom handler for <paramref name="fullName"/>, or the generic TileEntity path.</summary>
        public static PayloadEntityHandler Resolve(string fullName)
        {
            // --- Table hit wins (null members filled from Generic); unknown names use the TE default.

            if (!string.IsNullOrEmpty(fullName) && ByName.TryGetValue(fullName, out PayloadEntityHandler handler))
                return OverlayGeneric(handler);
            return Generic;
        }

        /// <summary>Copies Generic into any unset handler members.</summary>
        private static PayloadEntityHandler OverlayGeneric(PayloadEntityHandler custom)
        {
            // --- Table row wins per member; Generic fills Find/Capture/Sanitize/Apply when omitted.

            return new PayloadEntityHandler
            {
                CaptureTe = custom.CaptureTe ?? Generic.CaptureTe,
                Sanitize = custom.Sanitize ?? Generic.Sanitize,
                Apply = custom.Apply ?? Generic.Apply,
            };
        }

        /// <summary>One host table entry whose recorded origin lies inside a world rectangle.</summary>
        public readonly struct HostAtOrigin
        {
            public int WorldX { get; init; }
            public int WorldY { get; init; }
            public string FullName { get; init; }
        }

        /// <summary>Yields every chest, sign, and TileEntity whose exact origin lies inside <paramref name="worldBounds"/>.</summary>
        public static IEnumerable<HostAtOrigin> EnumerateHostsInBounds(Rectangle worldBounds)
        {
            // --- Exact table origins only; never FindChest/ReadSign (ghosts have no live multitile frame).

            if (worldBounds.IsEmpty)
                yield break;

            for (int i = 0; i < Main.chest.Length; i++)
            {
                Chest? chest = Main.chest[i];
                if (chest is null)
                    continue;
                if (!worldBounds.Contains(chest.x, chest.y))
                    continue;

                yield return new HostAtOrigin
                {
                    WorldX = chest.x,
                    WorldY = chest.y,
                    FullName = PayloadEntityNames.Chest,
                };
            }

            for (int i = 0; i < Main.sign.Length; i++)
            {
                Sign? sign = Main.sign[i];
                if (sign is null)
                    continue;
                if (!worldBounds.Contains(sign.x, sign.y))
                    continue;

                yield return new HostAtOrigin
                {
                    WorldX = sign.x,
                    WorldY = sign.y,
                    FullName = PayloadEntityNames.Sign,
                };
            }

            foreach (KeyValuePair<Point16, TileEntity> pair in TileEntity.ByPosition)
            {
                Point16 pos = pair.Key;
                if (!worldBounds.Contains(pos.X, pos.Y))
                    continue;

                TileEntity? te = pair.Value;
                if (te is null)
                    continue;

                string? name = PayloadEntityCapture.TryGetTileEntityFullName(te);
                if (string.IsNullOrEmpty(name))
                    continue;

                yield return new HostAtOrigin
                {
                    WorldX = pos.X,
                    WorldY = pos.Y,
                    FullName = name,
                };
            }
        }

        /// <summary>Resolves the host FullName registered at exact world coordinates via table scans.</summary>
        public static bool TryGetHostAtExact(int worldX, int worldY, out string fullName)
        {
            // --- Exact chest.x/y, sign.x/y, and ByPosition key only.

            fullName = string.Empty;
            if (!WorldGen.InWorld(worldX, worldY))
                return false;

            if (TryFindChestIndexAtExact(worldX, worldY, out _))
            {
                fullName = PayloadEntityNames.Chest;
                return true;
            }

            if (TryFindSignIndexAtExact(worldX, worldY, out _))
            {
                fullName = PayloadEntityNames.Sign;
                return true;
            }

            if (!TileEntity.ByPosition.TryGetValue(new Point16(worldX, worldY), out TileEntity? te) || te is null)
                return false;

            string? name = PayloadEntityCapture.TryGetTileEntityFullName(te);
            if (string.IsNullOrEmpty(name))
                return false;

            fullName = name;
            return true;
        }

        /// <summary>Resolves the live FullName registered at exact world coordinates.</summary>
        public static bool TryGetLiveFullName(int worldX, int worldY, out string fullName) =>
            TryGetHostAtExact(worldX, worldY, out fullName);

        /// <summary>True when the expected entity FullName is registered at exact world coordinates.</summary>
        public static bool TryFind(string fullName, int worldX, int worldY) =>
            TryGetLiveFullName(worldX, worldY, out string liveName)
            && string.Equals(liveName, fullName, StringComparison.Ordinal);

        /// <summary>Captures chest items and name into a payload tag.</summary>
        public static TagCompound? CaptureChest(Chest chest)
        {
            // --- Sparse identity slots plus optional custom name, capped for security.

            var tag = new TagCompound();

            string name = chest.name ?? string.Empty;
            if (name.Length > PayloadEntityLimits.MaxChestNameLength)
                name = name[..PayloadEntityLimits.MaxChestNameLength];
            if (!string.IsNullOrEmpty(name))
                tag["name"] = name;

            List<TagCompound>? items = SaveIdentityInventory(
                chest.item,
                PayloadEntityLimits.MaxChestItemSlots);
            if (items is not null)
                tag["items"] = items;

            return tag.Count > 0 ? tag : null;
        }

        /// <summary>Captures non-empty sign text into a payload tag.</summary>
        public static TagCompound? CaptureSign(Sign sign)
        {
            // --- Store trimmed text when non-empty.

            string text = sign.text ?? string.Empty;
            if (text.Length > PayloadEntityLimits.MaxSignTextLength)
                text = text[..PayloadEntityLimits.MaxSignTextLength];
            if (string.IsNullOrEmpty(text))
                return null;

            return new TagCompound { ["text"] = text };
        }

        /// <summary>Captures TileEntity data using the kind handler, or generic SaveData.</summary>
        public static TagCompound? CaptureTileEntity(TileEntity te, string fullName)
        {
            // --- Custom CaptureTe when registered; otherwise SaveData into a fresh tag.

            PayloadEntityHandler handler = Resolve(fullName);
            if (handler.CaptureTe is not null)
                return handler.CaptureTe(te);

            return Generic.CaptureTe!(te);
        }

        /// <summary>Sanitizes payload entity data using the kind handler, or a byte-size cap.</summary>
        public static TagCompound? Sanitize(string fullName, TagCompound? data)
        {
            // --- Drop empty tags; dispatch to handler Sanitize when present.

            if (data is null || data.Count == 0)
                return null;

            PayloadEntityHandler handler = Resolve(fullName);
            Func<TagCompound, TagCompound?> sanitize = handler.Sanitize ?? Generic.Sanitize!;
            return sanitize(data);
        }

        /// <summary>Applies payload data using the kind handler, or generic TileEntity.LoadData.</summary>
        public static bool TryApply(string fullName, int worldX, int worldY, TagCompound data)
        {
            // --- Handler Apply if registered; otherwise LoadData + TileEntitySharing.

            PayloadEntityHandler handler = Resolve(fullName);
            Func<int, int, TagCompound, bool> apply = handler.Apply ?? Generic.Apply!;
            return apply(worldX, worldY, data);
        }

        /// <summary>Removes a chest, sign, or TileEntity host at exact world coordinates.</summary>
        public static bool TryRemoveLiveHost(int worldX, int worldY)
        {
            // --- Drop stale records left on the destination when sweep blits tiles without clearing entities.

            if (!TryGetLiveFullName(worldX, worldY, out string liveName))
                return true;

            try
            {
                if (liveName == PayloadEntityNames.Chest)
                    return TryRemoveChest(worldX, worldY);
                if (liveName == PayloadEntityNames.Sign)
                    return TryRemoveSign(worldX, worldY);
                return TryRemoveTileEntity(worldX, worldY, out _);
            }
            catch (Exception ex)
            {
                TABLog.Error(
                    $"EntityRemove ({worldX},{worldY}) n={liveName} result=fail",
                    ex,
                    LogCategory);
                return false;
            }
        }

        /// <summary>Removes every chest, sign, and TileEntity whose origin lies inside <paramref name="worldBounds"/>.</summary>
        public static void RemoveAllHostsInBounds(Rectangle worldBounds)
        {
            // --- Snapshot origins first so Kill/unregister can mutate the live tables safely.

            if (worldBounds.IsEmpty)
                return;

            int removedChests = 0;
            int removedSigns = 0;
            int removedTes = 0;
            int teGenericFallback = 0;

            var chestOrigins = new List<Point>();
            for (int i = 0; i < Main.chest.Length; i++)
            {
                Chest? chest = Main.chest[i];
                if (chest is null)
                    continue;
                if (!worldBounds.Contains(chest.x, chest.y))
                    continue;
                chestOrigins.Add(new Point(chest.x, chest.y));
            }

            for (int i = 0; i < chestOrigins.Count; i++)
            {
                Point origin = chestOrigins[i];
                if (TryRemoveChest(origin.X, origin.Y))
                    removedChests++;
            }

            var signOrigins = new List<Point>();
            for (int i = 0; i < Main.sign.Length; i++)
            {
                Sign? sign = Main.sign[i];
                if (sign is null)
                    continue;
                if (!worldBounds.Contains(sign.x, sign.y))
                    continue;
                signOrigins.Add(new Point(sign.x, sign.y));
            }

            for (int i = 0; i < signOrigins.Count; i++)
            {
                Point origin = signOrigins[i];
                if (TryRemoveSign(origin.X, origin.Y))
                    removedSigns++;
            }

            var teOrigins = new List<Point16>();
            foreach (KeyValuePair<Point16, TileEntity> pair in TileEntity.ByPosition)
            {
                Point16 pos = pair.Key;
                if (!worldBounds.Contains(pos.X, pos.Y))
                    continue;
                teOrigins.Add(pos);
            }

            for (int i = 0; i < teOrigins.Count; i++)
            {
                Point16 pos = teOrigins[i];
                if (!TryRemoveTileEntity(pos.X, pos.Y, out bool usedGenericFallback))
                    continue;

                removedTes++;
                if (usedGenericFallback)
                    teGenericFallback++;
            }

            TABLog.Info(
                $"EntityRemove preSweep bounds=({worldBounds.X},{worldBounds.Y},{worldBounds.Width}x{worldBounds.Height}) "
                + $"chests={removedChests} signs={removedSigns} tes={removedTes} teGenericFallback={teGenericFallback}",
                LogCategory);
        }

        /// <summary>Reads a sparse inventory slot index stored as int or short.</summary>
        internal static int ReadSlotIndex(TagCompound tag, int fallback)
        {
            // --- TagCompound.TryGet is strict about numeric types; slot may be int (chest) or short (dolls).

            if (!tag.ContainsKey("slot"))
                return fallback;

            object value = tag["slot"];
            return value switch
            {
                int i => i,
                short s => s,
                byte b => b,
                long l => (int)l,
                _ => fallback,
            };
        }

        /// <summary>Builds a sparse identity slot tag from a live inventory item.</summary>
        internal static bool TrySaveIdentitySlot(Item item, int slot, out TagCompound entry)
        {
            // --- Emit name/stack/prefix only; never GlobalItem data.

            entry = null!;
            if (item is null || item.IsAir || item.stack <= 0)
                return false;

            // Clamp stack and resolve a portable content name.
            int stack = Math.Clamp(item.stack, 1, PayloadEntityLimits.MaxStackPerSlot);
            entry = new TagCompound
            {
                ["slot"] = slot,
                ["item"] = ContentNameResolver.GetItemName(item.type),
                ["stack"] = stack,
            };
            if (item.prefix != 0)
                entry["prefix"] = (int)item.prefix;
            return true;
        }

        /// <summary>Saves non-air inventory slots as sparse identity tags up to <paramref name="maxSlots"/>.</summary>
        internal static List<TagCompound>? SaveIdentityInventory(Item[]? inv, int maxSlots)
        {
            // --- Walk occupied slots and emit identity tags; omit empty inventories.

            if (inv is null || inv.Length == 0 || maxSlots <= 0)
                return null;

            var list = new List<TagCompound>();
            int limit = Math.Min(inv.Length, maxSlots);
            for (int slot = 0; slot < limit; slot++)
            {
                if (!TrySaveIdentitySlot(inv[slot], slot, out TagCompound entry))
                    continue;
                list.Add(entry);
            }

            return list.Count > 0 ? list : null;
        }

        /// <summary>Sanitizes a sparse identity inventory, converting legacy ItemIO slots when needed.</summary>
        internal static List<TagCompound>? SanitizeIdentityInventory(
            IList<TagCompound> slots,
            int maxSlots,
            string listName)
        {
            // --- Keep resolvable identity slots; rewrite mod/id ItemIO blobs once.

            var list = new List<TagCompound>();
            var usedSlots = new HashSet<int>();
            for (int i = 0; i < slots.Count && list.Count < maxSlots; i++)
            {
                TagCompound raw = slots[i];
                if (!TryNormalizeIdentitySlot(raw, i, maxSlots, out TagCompound normalized, out int slot))
                {
                    TABLog.Warn(
                        $"EntityCapture dropped {listName}[{i}] unresolved identity slot",
                        EntityCaptureLog);
                    continue;
                }

                if (!usedSlots.Add(slot))
                    continue;

                list.Add(normalized);
            }

            return list.Count > 0 ? list : null;
        }

        /// <summary>Loads an identity slot tag into a memory Item without mutating the world.</summary>
        internal static bool TryLoadIdentityItem(TagCompound entry, out Item item)
        {
            // --- Resolve name (or legacy ItemIO fields), then SetDefaults with stack/prefix.

            item = null!;
            if (!TryResolveIdentityFields(entry, out int itemType, out int stack, out int prefix) || itemType <= 0)
                return false;

            item = new Item();
            item.SetDefaults(itemType);
            if (item.IsAir)
                return false;

            item.stack = Math.Clamp(stack, 1, PayloadEntityLimits.MaxStackPerSlot);
            if (prefix > 0)
                item.Prefix(prefix);
            return true;
        }

        /// <summary>Loads an identity-only ItemIO blob into a memory Item without mutating the world.</summary>
        internal static bool TryLoadItemIOTag(TagCompound tag, out Item item)
        {
            // --- Single ItemIO.Load attempt; callers expect globalData already stripped at sanitize.

            item = null!;
            if (tag is null || tag.Count == 0)
                return false;

            try
            {
                item = ItemIO.Load(tag);
            }
            catch
            {
                item = null!;
                return false;
            }

            return item is { IsAir: false };
        }

        /// <summary>Normalizes one inventory entry to identity form (name/stack/prefix/slot).</summary>
        private static bool TryNormalizeIdentitySlot(
            TagCompound raw,
            int indexFallback,
            int maxSlots,
            out TagCompound normalized,
            out int slot)
        {
            // --- Prefer existing item name; else resolve legacy ItemIO identity fields.

            normalized = null!;
            slot = ReadSlotIndex(raw, indexFallback);
            if (slot < 0 || slot >= maxSlots)
                return false;

            if (!TryResolveIdentityFields(raw, out int itemType, out int stack, out int prefix) || itemType <= 0)
                return false;

            string itemName = ContentNameResolver.GetItemName(itemType);
            if (string.IsNullOrWhiteSpace(itemName) || ContentNameResolver.ResolveItem(itemName) is null)
                return false;

            normalized = new TagCompound
            {
                ["slot"] = slot,
                ["item"] = itemName,
                ["stack"] = Math.Clamp(stack, 1, PayloadEntityLimits.MaxStackPerSlot),
            };
            if (prefix > 0)
                normalized["prefix"] = prefix;
            return true;
        }

        /// <summary>Resolves type/stack/prefix from an identity slot or legacy ItemIO tag.</summary>
        private static bool TryResolveIdentityFields(
            TagCompound tag,
            out int itemType,
            out int stack,
            out int prefix)
        {
            // --- Identity name first; fall back to ItemIO mod/id/name fields.

            itemType = 0;
            stack = ReadTagInt(tag, "stack", 1);
            prefix = ReadTagInt(tag, "prefix", 0);

            // Chest/doll identity slots store a portable content name under "item".
            if (tag.ContainsKey("item") && tag["item"] is string itemName && !string.IsNullOrWhiteSpace(itemName))
            {
                if (ContentNameResolver.ResolveItem(itemName) is int id && id > 0)
                {
                    itemType = id;
                    return true;
                }

                return false;
            }

            return TryResolveItemTypeFromTag(tag, out itemType) && itemType > 0;
        }

        /// <summary>Resolves an item type from ItemIO mod+id, mod+name, or legacy item name keys.</summary>
        private static bool TryResolveItemTypeFromTag(TagCompound tag, out int itemType)
        {
            // --- Prefer numeric id with mod namespace, then FullName / search name.

            itemType = 0;
            if (tag.ContainsKey("mod") && tag.ContainsKey("id"))
            {
                string? mod = tag.GetString("mod");
                int id = ReadTagInt(tag, "id", 0);
                if (string.Equals(mod, "Terraria", StringComparison.OrdinalIgnoreCase) && id > 0)
                {
                    itemType = id;
                    return itemType < ItemLoader.ItemCount;
                }

                if (!string.IsNullOrEmpty(mod) && tag.ContainsKey("name"))
                {
                    string? name = tag.GetString("name");
                    if (!string.IsNullOrEmpty(name) && ContentNameResolver.ResolveItem($"{mod}/{name}") is int modId)
                    {
                        itemType = modId;
                        return true;
                    }

                    if (!string.IsNullOrEmpty(name) && ContentNameResolver.ResolveItem($"{mod}:{name}") is int modIdColon)
                    {
                        itemType = modIdColon;
                        return true;
                    }
                }
            }

            if (tag.ContainsKey("mod") && tag.ContainsKey("name"))
            {
                string? mod = tag.GetString("mod");
                string? name = tag.GetString("name");
                if (!string.IsNullOrEmpty(mod) && !string.IsNullOrEmpty(name))
                {
                    if (ContentNameResolver.ResolveItem($"{mod}/{name}") is int bySlash)
                    {
                        itemType = bySlash;
                        return true;
                    }

                    if (ContentNameResolver.ResolveItem($"{mod}:{name}") is int byColon)
                    {
                        itemType = byColon;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Rewrites an ItemIO item blob to identity-only Save output (no globalData).</summary>
        private static bool TryRewriteIdentityItemIO(
            TagCompound raw,
            out TagCompound rewritten,
            out string? dropReason)
        {
            // --- Strip globalData, resolve type, SetDefaults, ItemIO.Save with clean numeric types.

            rewritten = null!;
            dropReason = null;
            if (raw is null || raw.Count == 0)
            {
                dropReason = "emptyTag";
                return false;
            }

            // Load without GlobalItem fields so JSON-widened globals cannot throw.
            Item? loaded = null;
            TagCompound stripped = CloneWithoutGlobalData(raw);
            try
            {
                Item candidate = ItemIO.Load(stripped);
                if (candidate is { IsAir: false })
                    loaded = candidate;
            }
            catch
            {
                loaded = null;
            }

            // Fall back to type/stack/prefix when the stripped tag still cannot load.
            if (loaded is null)
            {
                if (!TryResolveItemTypeFromTag(raw, out int itemType) || itemType <= 0)
                {
                    dropReason = "unresolvedItem";
                    return false;
                }

                loaded = new Item();
                loaded.SetDefaults(itemType);
                if (loaded.IsAir)
                {
                    dropReason = "unresolvedItem";
                    return false;
                }

                loaded.stack = Math.Clamp(ReadTagInt(raw, "stack", 1), 1, PayloadEntityLimits.MaxStackPerSlot);
                int prefix = ReadTagInt(raw, "prefix", 0);
                if (prefix > 0)
                    loaded.Prefix(prefix);
            }
            else if (loaded.stack > PayloadEntityLimits.MaxStackPerSlot)
            {
                loaded.stack = PayloadEntityLimits.MaxStackPerSlot;
            }

            try
            {
                rewritten = CloneWithoutGlobalData(ItemIO.Save(loaded));
                return rewritten.Count > 0;
            }
            catch (Exception ex)
            {
                dropReason = ex.GetType().Name;
                return false;
            }
        }

        /// <summary>Clones a tag without the globalData list so fragile GlobalItem fields are dropped.</summary>
        private static TagCompound CloneWithoutGlobalData(TagCompound source)
        {
            // --- Shallow-copy every key except globalData.

            var clone = new TagCompound();
            foreach (KeyValuePair<string, object> pair in source)
            {
                if (string.Equals(pair.Key, "globalData", StringComparison.Ordinal))
                    continue;
                clone[pair.Key] = pair.Value;
            }

            return clone;
        }

        /// <summary>Reads a non-negative int from a tag key that may be stored as several numeric types.</summary>
        private static int ReadTagInt(TagCompound tag, string key, int fallback)
        {
            // --- JSON import widens integers to int; ItemIO may use byte/short.

            if (!tag.ContainsKey(key))
                return fallback;

            return tag[key] switch
            {
                int i => i,
                short s => s,
                byte b => b,
                long l => (int)l,
                _ => fallback,
            };
        }

        /// <summary>Clears live chest, sign, or TileEntity contents at exact world coordinates.</summary>
        public static bool TryWipe(string fullName, int worldX, int worldY)
        {
            // --- Dispatch by sidecar name; TileEntities use ByPosition at the payload origin only.

            if (fullName == PayloadEntityNames.Chest)
                return TryWipeChest(worldX, worldY);
            if (fullName == PayloadEntityNames.Sign)
                return TryWipeSign(worldX, worldY);
            return TryWipeTileEntity(worldX, worldY);
        }

        /// <summary>Removes any host at the payload origin and creates the sidecar record at exact coordinates.</summary>
        public static bool TryCreateHostForSweep(string fullName, int worldX, int worldY)
        {
            // --- Sweep trusts payload origins; drop whatever lives there, then recreate blindly.

            if (!WorldGen.InWorld(worldX, worldY))
                return false;

            TryRemoveLiveHost(worldX, worldY);
            return TryCreateHostAt(fullName, worldX, worldY);
        }

        /// <summary>Creates a missing chest, sign, or TileEntity host at exact world coordinates.</summary>
        public static bool TryEnsureHost(string fullName, int worldX, int worldY)
        {
            // --- No-op when the expected host is already registered at the payload origin.

            if (TryFind(fullName, worldX, worldY))
                return true;

            return TryCreateHostAt(fullName, worldX, worldY);
        }

        /// <summary>Creates a chest, sign, or TileEntity sidecar at exact world coordinates without tile checks.</summary>
        private static bool TryCreateHostAt(string fullName, int worldX, int worldY)
        {
            // --- Blind exact-origin create: ghosts are allowed when the live tile does not match.

            if (!WorldGen.InWorld(worldX, worldY))
                return false;

            if (fullName == PayloadEntityNames.Chest)
                return TryCreateChestAtExact(worldX, worldY);

            if (fullName == PayloadEntityNames.Sign)
                return TryCreateSignAtExact(worldX, worldY);

            if (TryFind(fullName, worldX, worldY))
                return true;

            return TryPlaceTileEntityHost(fullName, worldX, worldY);
        }

        /// <summary>Ensures a Main.chest entry exists at exact x/y, ignoring live tile type.</summary>
        private static bool TryCreateChestAtExact(int worldX, int worldY)
        {
            // --- CreateChest writes the table slot; no container-tile gate.

            if (TryFindChestIndexAtExact(worldX, worldY, out _))
                return true;

            int created = Chest.CreateChest(worldX, worldY);
            if (created >= 0)
                return true;

            TABLog.Warn($"EntityEnsure Chest CreateChest rejected ({worldX},{worldY})", LogCategory);
            return false;
        }

        /// <summary>Ensures a Main.sign entry exists at exact x/y, ignoring live tile type.</summary>
        private static bool TryCreateSignAtExact(int worldX, int worldY)
        {
            // --- Allocate a free sign slot at the payload origin; no ReadSign/TopLeft.

            if (TryFindSignIndexAtExact(worldX, worldY, out _))
                return true;

            for (int i = 0; i < Main.sign.Length; i++)
            {
                if (Main.sign[i] is not null)
                    continue;

                Main.sign[i] = new Sign
                {
                    x = worldX,
                    y = worldY,
                    text = string.Empty,
                };

                if (Main.netMode == NetmodeID.Server)
                    NetMessage.SendData(MessageID.ReadSign, -1, -1, null, i, 0f, 0f, 0f, 0, 0, 0);

                return true;
            }

            TABLog.Warn($"EntityEnsure Sign no free slot ({worldX},{worldY})", LogCategory);
            return false;
        }

        private static bool TryRemoveChest(int worldX, int worldY)
        {
            // --- Unregister the chest record whose exact origin matches the payload cell.

            if (!TryFindChestIndexAtExact(worldX, worldY, out int chestIndex))
                return true;

            Main.chest[chestIndex] = null;
            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.ChestUpdates, -1, -1, null, chestIndex, worldX, worldY);

            return true;
        }

        /// <summary>Finds the Main.chest index with exact x/y, ignoring multitile frame walks.</summary>
        private static bool TryFindChestIndexAtExact(int worldX, int worldY, out int chestIndex)
        {
            // --- Linear scan of Main.chest; FindChest misses ghost records without live frames.

            chestIndex = -1;
            for (int i = 0; i < Main.chest.Length; i++)
            {
                Chest? chest = Main.chest[i];
                if (chest is null)
                    continue;
                if (chest.x != worldX || chest.y != worldY)
                    continue;

                chestIndex = i;
                return true;
            }

            return false;
        }

        /// <summary>Finds the Main.sign index with exact x/y, ignoring multitile frame walks.</summary>
        private static bool TryFindSignIndexAtExact(int worldX, int worldY, out int signIndex)
        {
            // --- Linear scan of Main.sign; ReadSign misses ghost records without live frames.

            signIndex = -1;
            for (int i = 0; i < Main.sign.Length; i++)
            {
                Sign? sign = Main.sign[i];
                if (sign is null)
                    continue;
                if (sign.x != worldX || sign.y != worldY)
                    continue;

                signIndex = i;
                return true;
            }

            return false;
        }

        private static bool TryRemoveSign(int worldX, int worldY)
        {
            // --- Drop the sign text record whose exact origin matches the payload cell.

            if (!TryFindSignIndexAtExact(worldX, worldY, out int signIndex))
                return true;

            Main.sign[signIndex] = null;
            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.ReadSign, -1, -1, null, signIndex, 0f, 0f, 0f, 0, 0, 0);

            return true;
        }

        private static bool TryRemoveTileEntity(int worldX, int worldY, out bool usedGenericFallback)
        {
            // --- Prefer type Kill helpers; always unregister ByPosition/ByID so unknown vanilla TEs cannot linger.

            usedGenericFallback = false;
            Point16 key = new(worldX, worldY);
            if (!TileEntity.ByPosition.TryGetValue(key, out TileEntity? te) || te is null)
                return true;

            int removedId = te.ID;
            bool typedKill = TryKillTypedTileEntity(te, worldX, worldY);

            // Typed Kill may already have cleared the maps; force-unregister any leftover entry.
            bool stillMapped = TileEntity.ByPosition.ContainsKey(key) || TileEntity.ByID.ContainsKey(removedId);
            if (!typedKill || stillMapped)
            {
                usedGenericFallback = true;
                TileEntity.ByPosition.Remove(key);
                TileEntity.ByID.Remove(removedId);
            }

            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.TileEntitySharing, -1, -1, null, removedId, worldX, worldY);

            if (usedGenericFallback)
            {
                TABLog.Debug(
                    $"EntityRemove TE genericUnregister ({worldX},{worldY}) id={removedId} type={te.GetType().Name}",
                    LogCategory);
            }

            return true;
        }

        /// <summary>Invokes the known vanilla/mod Kill helper for a TileEntity when one exists.</summary>
        private static bool TryKillTypedTileEntity(TileEntity te, int worldX, int worldY)
        {
            // --- Typed Kill drops inventory/items correctly; missing types fall through to generic unregister.

            switch (te)
            {
                case TEItemFrame:
                    TEItemFrame.Kill(worldX, worldY);
                    return true;
                case TEDisplayDoll:
                    TEDisplayDoll.Kill(worldX, worldY);
                    return true;
                case TEWeaponsRack:
                    TEWeaponsRack.Kill(worldX, worldY);
                    return true;
                case TEHatRack:
                    TEHatRack.Kill(worldX, worldY);
                    return true;
                case TEFoodPlatter:
                    TEFoodPlatter.Kill(worldX, worldY);
                    return true;
                case ModTileEntity modTe:
                    modTe.Kill(worldX, worldY);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Creates a TileEntity record at exact coordinates, allowing ghosts when Place rejects the tile.</summary>
        private static bool TryPlaceTileEntityHost(string fullName, int worldX, int worldY)
        {
            // --- Prefer typed Place; force-register when ValidTile rejects a ghost origin.

            if (!WorldGen.InWorld(worldX, worldY))
                return false;

            Point16 key = new(worldX, worldY);
            if (TileEntity.ByPosition.TryGetValue(key, out TileEntity? existing) && existing is not null)
            {
                string? existingName = PayloadEntityCapture.TryGetTileEntityFullName(existing);
                if (string.Equals(existingName, fullName, StringComparison.Ordinal))
                    return true;
            }

            try
            {
                int placedId = fullName switch
                {
                    PayloadEntityNames.ItemFrame => TEItemFrame.Place(worldX, worldY),
                    PayloadEntityNames.DisplayDoll => TEDisplayDoll.Place(worldX, worldY),
                    PayloadEntityNames.WeaponsRack => TEWeaponsRack.Place(worldX, worldY),
                    PayloadEntityNames.HatRack => TEHatRack.Place(worldX, worldY),
                    PayloadEntityNames.FoodPlatter => TEFoodPlatter.Place(worldX, worldY),
                    _ => TryPlaceModOrGenericTileEntity(fullName, worldX, worldY),
                };

                if (placedId >= 0)
                    return true;

                if (TryForceRegisterTileEntity(fullName, worldX, worldY))
                    return true;

                TABLog.Warn(
                    $"EntityEnsure TE Place rejected ({worldX},{worldY}) n={fullName}",
                    LogCategory);
                return false;
            }
            catch (Exception ex)
            {
                TABLog.Error(
                    $"EntityEnsure TE Place failed ({worldX},{worldY}) n={fullName}",
                    ex,
                    LogCategory);
                return false;
            }
        }

        /// <summary>Places a mod TileEntity via ModContent when the vanilla Place switch does not apply.</summary>
        private static int TryPlaceModOrGenericTileEntity(string fullName, int worldX, int worldY)
        {
            // --- ModTileEntity.Place registers the host at the payload origin.

            return ModContent.TryFind(fullName, out ModTileEntity modTe)
                ? modTe.Place(worldX, worldY)
                : -1;
        }

        /// <summary>Registers a TileEntity at exact coordinates when typed Place rejects the live tile.</summary>
        private static bool TryForceRegisterTileEntity(string fullName, int worldX, int worldY)
        {
            // --- Ghost hosts: construct an instance and insert into ByPosition/ByID without ValidTile.

            Point16 key = new(worldX, worldY);
            if (!TryCreateTileEntityInstance(fullName, out TileEntity? te) || te is null)
                return false;

            if (TileEntity.ByPosition.ContainsKey(key))
                TryRemoveTileEntity(worldX, worldY, out _);

            te.Position = key;
            te.ID = TileEntity.AssignNewID();
            TileEntity.ByID[te.ID] = te;
            TileEntity.ByPosition[key] = te;

            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.TileEntitySharing, -1, -1, null, te.ID, worldX, worldY);

            TABLog.Debug(
                $"EntityEnsure TE forceRegister ({worldX},{worldY}) n={fullName} id={te.ID}",
                LogCategory);
            return true;
        }

        /// <summary>Constructs a detached TileEntity instance for the given FullName.</summary>
        private static bool TryCreateTileEntityInstance(string fullName, out TileEntity? te)
        {
            // --- Mod templates via ConstructFromBase; vanilla Terraria:* via Activator.

            te = null;
            if (string.IsNullOrEmpty(fullName))
                return false;

            if (ModContent.TryFind(fullName, out ModTileEntity modTe))
            {
                te = ModTileEntity.ConstructFromBase(modTe);
                return te is not null;
            }

            if (!fullName.StartsWith("Terraria:", StringComparison.Ordinal))
                return false;

            string className = fullName["Terraria:".Length..];
            Type? type = typeof(TileEntity).Assembly.GetType($"Terraria.GameContent.Tile_Entities.{className}");
            if (type is null || !typeof(TileEntity).IsAssignableFrom(type))
                return false;

            te = Activator.CreateInstance(type) as TileEntity;
            return te is not null;
        }

        /// <summary>Serializes a TileEntity via SaveData, omitting an empty tag.</summary>
        private static TagCompound? CaptureGenericTileEntity(TileEntity te)
        {
            // --- SaveData into a fresh tag; omit when empty.

            var tag = new TagCompound();
            te.SaveData(tag);
            return tag.Count > 0 ? tag : null;
        }

        /// <summary>Loads TagCompound data onto the TileEntity at exact coordinates and shares it.</summary>
        private static bool TryApplyGenericTileEntity(int worldX, int worldY, TagCompound data)
        {
            // --- ByPosition lookup; catch LoadData failures so one bad TE does not throw into paste.

            Point16 key = new(worldX, worldY);
            if (!TileEntity.ByPosition.TryGetValue(key, out TileEntity? te) || te is null)
                return false;

            try
            {
                te.LoadData(data);
            }
            catch (Exception ex)
            {
                TABLog.Warn(
                    $"EntityPlace TE LoadData failed world=({worldX},{worldY}) type={te.GetType().Name}: "
                    + $"{ex.GetType().Name}: {ex.Message}",
                    EntityPlaceLog);
                return false;
            }

            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.TileEntitySharing, -1, -1, null, te.ID, key.X, key.Y);

            return true;
        }

        private static TagCompound? CaptureDisplayDoll(TileEntity te)
        {
            // --- Read private inventories, then emit chest-style identity items/dyes lists.

            if (te is not TEDisplayDoll doll)
                return CaptureGenericTileEntity(te);

            Item[]? items = DisplayDollItemsField?.GetValue(doll) as Item[];
            Item[]? dyes = DisplayDollDyesField?.GetValue(doll) as Item[];
            if (items is null && dyes is null)
            {
                TABLog.Warn(
                    "EntityCapture DisplayDoll reflection miss: _items/_dyes fields unavailable",
                    EntityCaptureLog);
                return null;
            }

            var tag = new TagCompound();
            List<TagCompound>? itemTags = SaveIdentityInventory(items, DisplayDollSlotCount);
            if (itemTags is not null)
                tag["items"] = itemTags;

            List<TagCompound>? dyeTags = SaveIdentityInventory(dyes, DisplayDollSlotCount);
            if (dyeTags is not null)
                tag["dyes"] = dyeTags;

            return tag.Count > 0 ? tag : null;
        }

        /// <summary>Sanitizes DisplayDoll items/dyes lists to identity slots, converting legacy ItemIO.</summary>
        private static TagCompound? SanitizeDisplayDollData(TagCompound data)
        {
            // --- Normalize each inventory slot to name/stack/prefix, then enforce the entity byte cap.

            var cleaned = new TagCompound();
            if (data.ContainsKey("items") && data["items"] is IList<TagCompound> items)
            {
                List<TagCompound>? normalized = SanitizeIdentityInventory(
                    items,
                    DisplayDollSlotCount,
                    "items");
                if (normalized is not null)
                    cleaned["items"] = normalized;
            }

            if (data.ContainsKey("dyes") && data["dyes"] is IList<TagCompound> dyes)
            {
                List<TagCompound>? normalized = SanitizeIdentityInventory(
                    dyes,
                    DisplayDollSlotCount,
                    "dyes");
                if (normalized is not null)
                    cleaned["dyes"] = normalized;
            }

            return cleaned.Count > 0 ? ClampTagSize(cleaned) : null;
        }

        /// <summary>Applies identity DisplayDoll inventory tags into reflected _items/_dyes after wipe.</summary>
        private static bool TryApplyDisplayDoll(int worldX, int worldY, TagCompound data)
        {
            // --- Per-slot identity load into live arrays; skip failures so other slots still place.

            Point16 key = new(worldX, worldY);
            if (!TileEntity.ByPosition.TryGetValue(key, out TileEntity? te) || te is not TEDisplayDoll)
                return false;

            Item[]? items = DisplayDollItemsField?.GetValue(te) as Item[];
            Item[]? dyes = DisplayDollDyesField?.GetValue(te) as Item[];
            if (items is null && dyes is null)
            {
                TABLog.Warn(
                    $"EntityPlace DisplayDoll reflection miss world=({worldX},{worldY})",
                    EntityPlaceLog);
                return false;
            }

            // Wipe first so unlisted slots do not survive the override.
            WipeInventoryField(te, DisplayDollItemsField);
            WipeInventoryField(te, DisplayDollDyesField);

            bool wrote = false;
            if (items is not null && data.ContainsKey("items") && data["items"] is IList<TagCompound> itemSlots)
                wrote |= ApplyIdentityInventory(items, itemSlots, worldX, worldY, "items");

            if (dyes is not null && data.ContainsKey("dyes") && data["dyes"] is IList<TagCompound> dyeSlots)
                wrote |= ApplyIdentityInventory(dyes, dyeSlots, worldX, worldY, "dyes");

            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.TileEntitySharing, -1, -1, null, te.ID, key.X, key.Y);

            // Empty identity inventories still count as a successful wipe-only apply.
            return wrote || (!data.ContainsKey("items") && !data.ContainsKey("dyes"));
        }

        /// <summary>Writes identity Item slots into a live inventory array, logging per-slot load misses.</summary>
        private static bool ApplyIdentityInventory(
            Item[] dest,
            IList<TagCompound> slots,
            int worldX,
            int worldY,
            string listName)
        {
            // --- Best-effort identity load with logging; skip out-of-range or unresolved slots.

            bool wrote = false;
            for (int i = 0; i < slots.Count; i++)
            {
                TagCompound tag = slots[i];
                int slot = ReadSlotIndex(tag, i);
                if (slot < 0 || slot >= dest.Length)
                    continue;

                if (!TryLoadIdentityItem(tag, out Item loaded))
                {
                    TABLog.Warn(
                        $"EntityPlace DisplayDoll load miss world=({worldX},{worldY}) {listName} slot={slot}",
                        EntityPlaceLog);
                    continue;
                }

                dest[slot] = loaded;
                wrote = true;
            }

            return wrote;
        }

        private static TagCompound? SanitizeChestData(TagCompound data)
        {
            // --- Keep capped name and validated sparse identity slots; drop empty chest tags.

            var cleaned = new TagCompound();

            if (data.ContainsKey("name"))
            {
                string name = data.GetString("name") ?? string.Empty;
                if (name.Length > PayloadEntityLimits.MaxChestNameLength)
                    name = name[..PayloadEntityLimits.MaxChestNameLength];
                if (!string.IsNullOrEmpty(name))
                    cleaned["name"] = name;
            }

            if (data.ContainsKey("items") && data["items"] is IList<TagCompound> items)
            {
                List<TagCompound>? itemList = SanitizeIdentityInventory(
                    items,
                    PayloadEntityLimits.MaxChestItemSlots,
                    "chest.items");
                if (itemList is not null)
                    cleaned["items"] = itemList;
            }

            return cleaned.Count > 0 ? cleaned : null;
        }

        private static TagCompound? SanitizeSignData(TagCompound data)
        {
            if (!data.ContainsKey("text"))
                return null;
            string text = data.GetString("text") ?? string.Empty;
            if (text.Length > PayloadEntityLimits.MaxSignTextLength)
                text = text[..PayloadEntityLimits.MaxSignTextLength];
            return string.IsNullOrEmpty(text) ? null : new TagCompound { ["text"] = text };
        }

        /// <summary>Rewrites a nested ItemIO item blob to identity-only form, then enforces the entity byte cap.</summary>
        private static TagCompound? SanitizeGenericTileEntityData(TagCompound data)
        {
            // --- TEItemFrame-style d.item must stay ItemIO-shaped for LoadData, without globalData.

            TagCompound working = data;
            if (data.ContainsKey("item") && data["item"] is TagCompound itemTag)
            {
                if (!TryRewriteIdentityItemIO(itemTag, out TagCompound rewritten, out string? dropReason))
                {
                    TABLog.Warn(
                        $"EntityCapture TE dropped uncertified item reason={dropReason ?? "unknown"}",
                        EntityCaptureLog);
                    working = new TagCompound();
                    foreach (KeyValuePair<string, object> pair in data)
                    {
                        if (string.Equals(pair.Key, "item", StringComparison.Ordinal))
                            continue;
                        working[pair.Key] = pair.Value;
                    }
                }
                else
                {
                    working = new TagCompound();
                    foreach (KeyValuePair<string, object> pair in data)
                        working[pair.Key] = pair.Value;
                    working["item"] = rewritten;
                }
            }

            return working.Count > 0 ? ClampTagSize(working) : null;
        }

        private static TagCompound? ClampTagSize(TagCompound data)
        {
            try
            {
                using var ms = new System.IO.MemoryStream();
                TagIO.ToStream(data, ms, compress: true);
                if (ms.Length > PayloadEntityLimits.MaxDataTagBytes)
                    return null;
                return data;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryApplyChestData(int worldX, int worldY, TagCompound data)
        {
            // --- Write name first, then fill sparse slots and sync each changed facet.

            if (!TryFindChestIndexAtExact(worldX, worldY, out int chestIndex))
                return false;

            Chest chest = Main.chest[chestIndex];
            if (chest.item is null)
                return false;

            bool wroteAnything = false;

            if (data.ContainsKey("name"))
            {
                string name = data.GetString("name") ?? string.Empty;
                if (name.Length > PayloadEntityLimits.MaxChestNameLength)
                    name = name[..PayloadEntityLimits.MaxChestNameLength];
                chest.name = name;
                wroteAnything = true;

                if (Main.netMode == NetmodeID.Server)
                {
                    NetMessage.SendData(
                        MessageID.ChestName,
                        -1,
                        -1,
                        NetworkText.FromLiteral(name),
                        chestIndex,
                        worldX,
                        worldY);
                }
            }

            if (!data.ContainsKey("items") || data["items"] is not IList<TagCompound> items)
                return true;

            int appliedSlots = 0;
            for (int i = 0; i < items.Count; i++)
            {
                TagCompound entry = items[i];
                int slot = ReadSlotIndex(entry, appliedSlots);
                if (slot < 0 || slot >= chest.item.Length || slot >= PayloadEntityLimits.MaxChestItemSlots)
                    continue;

                if (!TryLoadIdentityItem(entry, out Item loaded))
                {
                    TABLog.Warn(
                        $"EntityApply chest slot={slot} unresolved item={entry.GetString("item")}",
                        LogCategory);
                    continue;
                }

                chest.item[slot] = loaded;

                if (Main.netMode == NetmodeID.Server)
                    NetMessage.SendData(MessageID.SyncChestItem, -1, -1, null, chestIndex, slot);

                appliedSlots++;
            }

            return wroteAnything || appliedSlots > 0 || items.Count == 0;
        }

        private static bool TryApplySignData(int worldX, int worldY, TagCompound data)
        {
            // --- Write sign text and broadcast when networked.

            if (!data.ContainsKey("text"))
                return true;

            string text = data.GetString("text") ?? string.Empty;
            if (text.Length > PayloadEntityLimits.MaxSignTextLength)
                text = text[..PayloadEntityLimits.MaxSignTextLength];

            if (!TryFindSignIndexAtExact(worldX, worldY, out int signIndex))
                return false;

            Sign? sign = Main.sign[signIndex];
            if (sign is null)
                return false;

            sign.text = text;
            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.ReadSign, -1, -1, null, signIndex, 0f, 0f, 0f, 0, 0, 0);

            return true;
        }

        /// <summary>Empties every chest slot and clears the custom name.</summary>
        private static bool TryWipeChest(int worldX, int worldY)
        {
            // --- Turn every occupied slot to air and sync, then blank the chest name.

            if (!TryFindChestIndexAtExact(worldX, worldY, out int chestIndex))
                return false;

            Chest chest = Main.chest[chestIndex];
            if (chest.item is null)
                return false;

            for (int slot = 0; slot < chest.item.Length; slot++)
            {
                Item item = chest.item[slot];
                if (item is null || item.IsAir)
                    continue;

                item.TurnToAir();
                if (Main.netMode == NetmodeID.Server)
                    NetMessage.SendData(MessageID.SyncChestItem, -1, -1, null, chestIndex, slot);
            }

            if (!string.IsNullOrEmpty(chest.name))
            {
                chest.name = string.Empty;
                if (Main.netMode == NetmodeID.Server)
                {
                    NetMessage.SendData(
                        MessageID.ChestName,
                        -1,
                        -1,
                        NetworkText.FromLiteral(string.Empty),
                        chestIndex,
                        worldX,
                        worldY);
                }
            }

            return true;
        }

        /// <summary>Clears sign text at the live host.</summary>
        private static bool TryWipeSign(int worldX, int worldY)
        {
            // --- Blank text and broadcast when networked.

            if (!TryFindSignIndexAtExact(worldX, worldY, out int signIndex))
                return false;

            Sign? sign = Main.sign[signIndex];
            if (sign is null)
                return false;

            sign.text = string.Empty;
            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.ReadSign, -1, -1, null, signIndex, 0f, 0f, 0f, 0, 0, 0);

            return true;
        }

        /// <summary>Clears displayed items on the TileEntity registered at exact coordinates.</summary>
        private static bool TryWipeTileEntity(int worldX, int worldY)
        {
            // --- ByPosition at the payload origin only.

            Point16 key = new(worldX, worldY);
            if (!TileEntity.ByPosition.TryGetValue(key, out TileEntity? te) || te is null)
                return false;

            WipeKnownTileEntityItems(te, out bool wipedKnownItems);
            if (!wipedKnownItems)
                te.LoadData(new TagCompound());

            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.TileEntitySharing, -1, -1, null, te.ID, key.X, key.Y);

            return true;
        }

        /// <summary>Turns known vanilla TE item fields to air so leftover contents cannot merge into a later apply.</summary>
        private static void WipeKnownTileEntityItems(TileEntity te, out bool wipedKnownItems)
        {
            // --- Public item fields, then private _items/_dyes arrays used by dolls and hat racks.

            wipedKnownItems = true;
            switch (te)
            {
                case TEItemFrame itemFrame:
                    itemFrame.item ??= new Item();
                    itemFrame.item.TurnToAir();
                    return;
                case TEWeaponsRack weaponsRack:
                    weaponsRack.item ??= new Item();
                    weaponsRack.item.TurnToAir();
                    return;
                case TEFoodPlatter foodPlatter:
                    foodPlatter.item ??= new Item();
                    foodPlatter.item.TurnToAir();
                    return;
                case TEDisplayDoll:
                    WipeInventoryField(te, DisplayDollItemsField);
                    WipeInventoryField(te, DisplayDollDyesField);
                    return;
                case TEHatRack:
                    WipeInventoryField(
                        te,
                        typeof(TEHatRack).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic));
                    WipeInventoryField(
                        te,
                        typeof(TEHatRack).GetField("_dyes", BindingFlags.Instance | BindingFlags.NonPublic));
                    return;
            }

            wipedKnownItems = false;
        }

        /// <summary>Turns every item in a reflected Item[] field to air.</summary>
        private static void WipeInventoryField(TileEntity te, FieldInfo? field)
        {
            // --- Skip missing fields; TurnToAir each occupied slot in place.

            if (field?.GetValue(te) is not Item[] items)
                return;

            for (int i = 0; i < items.Length; i++)
            {
                Item item = items[i];
                if (item is null || item.IsAir)
                    continue;
                item.TurnToAir();
            }
        }
    }
}
