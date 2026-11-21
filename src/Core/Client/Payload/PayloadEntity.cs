using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Tile_Entities;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Synthetic FullNames for chests/signs, plus vanilla TileEntity class names.</summary>
    public static class PayloadEntityNames
    {
        public const string Chest = "Terraria:Chest";
        public const string Sign = "Terraria:Sign";
        public const string ItemFrame = "Terraria:TEItemFrame";
        public const string DisplayDoll = "Terraria:TEDisplayDoll";
        public const string WeaponsRack = "Terraria:TEWeaponsRack";
        public const string HatRack = "Terraria:TEHatRack";
        public const string FoodPlatter = "Terraria:TEFoodPlatter";
    }

    /// <summary>Security caps for payload entity capture and import.</summary>
    public static class PayloadEntityLimits
    {
        public const int MaxEntities = 4096;
        public const int MaxDataTagBytes = 64 * 1024;
        public const int MaxChestItemSlots = 40;
        public const int MaxChestNameLength = 63;
        public const int MaxSignTextLength = 1024;
        public const int MaxStackPerSlot = 9999;
    }

    /// <summary>
    /// Position-keyed entity sidecar entry: relative origin, FullName <c>n</c>, opaque TagCompound <c>d</c>.
    /// </summary>
    public readonly struct PayloadEntity
    {
        public Point Relative { get; }
        public string FullName { get; }
        public TagCompound? Data { get; }

        public PayloadEntity(Point relative, string fullName, TagCompound? data = null)
        {
            Relative = relative;
            FullName = fullName ?? string.Empty;
            Data = data;
        }

        public bool HasData => Data is { Count: > 0 };
    }

    /// <summary>Capture and validate helpers for <see cref="PayloadEntity"/> sidecars.</summary>
    internal static class PayloadEntityCapture
    {
        private const string LogCategory = "EntityCapture";

        /// <summary>Snapshots chests, signs, and tile entities inside <paramref name="worldBounds"/> as relative entities.</summary>
        public static PayloadEntity[] CaptureFromWorld(Rectangle worldBounds)
        {
            // --- Exact table origins in bounds; ghost hosts are kept so paste can recreate them.

            if (worldBounds.IsEmpty)
                return [];

            var list = new List<PayloadEntity>();
            var seen = new HashSet<Point>();
            int teCaptured = 0;
            int teEmptyData = 0;
            int teSkippedUnnamed = 0;
            int teSkippedSeen = 0;

            // Capture every chest whose recorded origin sits in the selection.
            for (int i = 0; i < Main.chest.Length && list.Count < PayloadEntityLimits.MaxEntities; i++)
            {
                Chest? chest = Main.chest[i];
                if (chest is null)
                    continue;
                if (!TryRelativeInBounds(worldBounds, chest.x, chest.y, out Point rel))
                    continue;

                if (!seen.Add(rel))
                {
                    TABLog.Warn(
                        $"EntityCapture Chest skipped duplicateOrigin world=({chest.x},{chest.y}) rel=({rel.X},{rel.Y})",
                        LogCategory);
                    continue;
                }

                try
                {
                    TagCompound? data = PayloadEntityHandlers.CaptureChest(chest);
                    list.Add(new PayloadEntity(rel, PayloadEntityNames.Chest, data));
                }
                catch (Exception ex)
                {
                    TABLog.Error(
                        $"EntityCapture Chest failed world=({chest.x},{chest.y}) rel=({rel.X},{rel.Y})",
                        ex,
                        LogCategory);
                }
            }

            // Capture every sign whose recorded origin sits in the selection.
            for (int i = 0; i < Main.sign.Length && list.Count < PayloadEntityLimits.MaxEntities; i++)
            {
                Sign? sign = Main.sign[i];
                if (sign is null)
                    continue;
                if (!TryRelativeInBounds(worldBounds, sign.x, sign.y, out Point rel))
                    continue;

                if (!seen.Add(rel))
                {
                    TABLog.Warn(
                        $"EntityCapture Sign skipped duplicateOrigin world=({sign.x},{sign.y}) rel=({rel.X},{rel.Y})",
                        LogCategory);
                    continue;
                }

                try
                {
                    TagCompound? data = PayloadEntityHandlers.CaptureSign(sign);
                    list.Add(new PayloadEntity(rel, PayloadEntityNames.Sign, data));
                }
                catch (Exception ex)
                {
                    TABLog.Error(
                        $"EntityCapture Sign failed world=({sign.x},{sign.y}) rel=({rel.X},{rel.Y})",
                        ex,
                        LogCategory);
                }
            }

            // Capture TileEntities keyed by position inside the bounds.
            foreach (KeyValuePair<Point16, TileEntity> pair in TileEntity.ByPosition)
            {
                if (list.Count >= PayloadEntityLimits.MaxEntities)
                    break;

                Point16 pos = pair.Key;
                if (!TryRelativeInBounds(worldBounds, pos.X, pos.Y, out Point rel))
                    continue;

                TileEntity? te = pair.Value;
                if (te is null)
                    continue;

                if (!seen.Add(rel))
                {
                    teSkippedSeen++;
                    TABLog.Warn(
                        $"EntityCapture TE skipped duplicateOrigin world=({pos.X},{pos.Y}) rel=({rel.X},{rel.Y}) "
                        + $"type={te.GetType().Name}",
                        LogCategory);
                    continue;
                }

                string? fullName = TryGetTileEntityFullName(te);
                if (string.IsNullOrEmpty(fullName))
                {
                    teSkippedUnnamed++;
                    TABLog.Warn(
                        $"EntityCapture TE skipped unnamed world=({pos.X},{pos.Y}) rel=({rel.X},{rel.Y}) "
                        + $"runtimeType={te.GetType().FullName} teType={te.type}",
                        LogCategory);
                    continue;
                }

                TagCompound? data = CaptureTileEntityData(te, fullName, pos.X, pos.Y, rel.X, rel.Y);
                if (data is null)
                    teEmptyData++;
                else
                    teCaptured++;

                list.Add(new PayloadEntity(rel, fullName, data));
            }

            TABLog.Info(
                $"EntityCapture done bounds=({worldBounds.X},{worldBounds.Y},{worldBounds.Width}x{worldBounds.Height}) "
                + $"entities={list.Count} teWithData={teCaptured} teEmptyData={teEmptyData} "
                + $"teSkippedUnnamed={teSkippedUnnamed} teSkippedSeen={teSkippedSeen}",
                LogCategory);

            return list.ToArray();
        }

        /// <summary>Filters and caps an imported entity list for document bounds.</summary>
        public static PayloadEntity[] Sanitize(IReadOnlyList<PayloadEntity> entities, int width, int height)
        {
            // --- Drop OOB / empty-name / oversize entries and enforce MaxEntities.

            if (entities is null || entities.Count == 0 || width <= 0 || height <= 0)
                return [];

            var result = new List<PayloadEntity>(Math.Min(entities.Count, PayloadEntityLimits.MaxEntities));
            var seen = new HashSet<Point>();
            for (int i = 0; i < entities.Count && result.Count < PayloadEntityLimits.MaxEntities; i++)
            {
                PayloadEntity e = entities[i];
                if (string.IsNullOrWhiteSpace(e.FullName))
                    continue;
                if ((uint)e.Relative.X >= (uint)width || (uint)e.Relative.Y >= (uint)height)
                    continue;
                if (!seen.Add(e.Relative))
                    continue;

                TagCompound? data = PayloadEntityHandlers.Sanitize(e.FullName, e.Data);
                result.Add(new PayloadEntity(e.Relative, e.FullName.Trim(), data));
            }

            return result.ToArray();
        }

        /// <summary>Returns a stable FullName for a live tile entity, or null if it cannot be named.</summary>
        public static string? TryGetTileEntityFullName(TileEntity te)
        {
            // --- Use ModTileEntity.FullName; vanilla TEs use Terraria:{class name}.

            if (te is null)
                return null;

            if (te is ModTileEntity modTe)
                return modTe.FullName;

            if (te.type < TileEntitiesManager.VanillaTypeCount)
                return $"Terraria:{te.GetType().Name}";

            return null;
        }

        /// <summary>Returns the live entity FullName at world coordinates, if any.</summary>
        public static bool TryGetLiveFullName(int worldX, int worldY, out string fullName)
        {
            // --- Delegate to the handler table (chest/sign, then generic TileEntity).

            return PayloadEntityHandlers.TryGetLiveFullName(worldX, worldY, out fullName);
        }

        private static bool TryRelativeInBounds(Rectangle worldBounds, int worldX, int worldY, out Point relative)
        {
            relative = new Point(worldX - worldBounds.X, worldY - worldBounds.Y);
            return (uint)relative.X < (uint)worldBounds.Width && (uint)relative.Y < (uint)worldBounds.Height;
        }

        private static TagCompound? CaptureTileEntityData(
            TileEntity te,
            string fullName,
            int worldX,
            int worldY,
            int relX,
            int relY)
        {
            // --- Dispatch CaptureTe from the handler table; log empty SaveData vs claimed content.

            try
            {
                TagCompound? tag = PayloadEntityHandlers.CaptureTileEntity(te, fullName);
                if (tag is null)
                {
                    if (TileEntityClaimsContent(te))
                    {
                        TABLog.Warn(
                            $"EntityCapture TE SaveData empty but claimsContent n={fullName} "
                            + $"world=({worldX},{worldY}) rel=({relX},{relY}) runtimeType={te.GetType().Name}",
                            LogCategory);
                    }
                    else
                    {
                        TABLog.Debug(
                            $"EntityCapture TE SaveData empty n={fullName} world=({worldX},{worldY}) rel=({relX},{relY})",
                            LogCategory);
                    }

                    return null;
                }

                TABLog.Debug(
                    $"EntityCapture TE SaveData ok n={fullName} world=({worldX},{worldY}) rel=({relX},{relY}) keys={tag.Count}",
                    LogCategory);
                return tag;
            }
            catch (Exception ex)
            {
                TABLog.Error(
                    $"EntityCapture TE SaveData threw n={fullName} world=({worldX},{worldY}) rel=({relX},{relY})",
                    ex,
                    LogCategory);
                return null;
            }
        }

        /// <summary>Returns true when the live TE appears to hold inventory that SaveData should have written.</summary>
        private static bool TileEntityClaimsContent(TileEntity te)
        {
            // --- Display dolls expose ContainsItems; item frames check their stored item.

            if (te is TEDisplayDoll doll)
                return doll.ContainsItems();

            if (te is TEItemFrame frame)
                return frame.item is { IsAir: false };

            return false;
        }
    }
}
