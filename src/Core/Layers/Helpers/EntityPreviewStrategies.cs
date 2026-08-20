using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.UI.Preview;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    /// <summary>Shared preview draw strategies for item-holder and player-doll TileEntities.</summary>
    internal static class EntityPreviewStrategies
    {
        private const int ItemFrameMaxSize = 20;
        private const int WeaponsRackMaxSize = 40;
        private const int DisplayDollArmorSlots = 8;
        private const int HatRackSlots = 2;

        private static Player? _dollPlayer;
        private static Item[]? _armorScratch;
        private static Item[]? _dyeScratch;

        /// <summary>True when this pass invoked PlayerRenderer and the sprite batch must be restored.</summary>
        public static bool UsedPlayerRenderer { get; private set; }

        /// <summary>Clears per-pass player-renderer state before drawing payload entities.</summary>
        public static void BeginPass()
        {
            // --- Reset the DrawPlayer flag so the overlay pass can restore SpriteBatch afterward.

            UsedPlayerRenderer = false;
        }

        /// <summary>Draws a small diamond origin marker centered in the entity relative cell.</summary>
        public static bool TryDrawOriginMarker(in PreviewEntityDrawArgs args)
        {
            // --- Rotate a compact square 45° at the cell center so the origin reads as a point.

            PreviewDrawContext ctx = args.Ctx;
            Texture2D pixel = TextureAssets.MagicPixel.Value;
            var src = new Rectangle(0, 0, 1, 1);
            float cell = PreviewDrawContext.TilePixelSize;
            float cellSize = cell * ctx.Scale;
            Vector2 center = ctx.ToScreen(new Vector2(
                (args.RelX + 0.5f) * cell,
                (args.RelY + 0.5f) * cell));

            // ~7 world pixels across (~half a tile diagonal lobe), scaled with the preview.
            float diamond = Math.Max(3f, cellSize * 0.45f);
            float outline = diamond + Math.Max(2f, ctx.Scale * 2f);

            Color fill = ctx.Tint;
            fill.A = (byte)Math.Clamp(140, 0, 255);
            Color border = ctx.Tint;
            border.A = (byte)Math.Clamp(Math.Max((int)ctx.Tint.A, 200), 0, 255);

            // Outline diamond behind the fill.
            ctx.SpriteBatch.Draw(
                pixel,
                center,
                src,
                border,
                MathHelper.PiOver4,
                new Vector2(0.5f, 0.5f),
                new Vector2(outline, outline),
                SpriteEffects.None,
                0f);
            ctx.SpriteBatch.Draw(
                pixel,
                center,
                src,
                fill,
                MathHelper.PiOver4,
                new Vector2(0.5f, 0.5f),
                new Vector2(diamond, diamond),
                SpriteEffects.None,
                0f);

            return true;
        }

        /// <summary>Draws a single displayed item using vanilla tile-type anchors, or a 16,16 fallback.</summary>
        public static bool TryDrawItemOverlay(in PreviewEntityDrawArgs args)
        {
            // --- Load d.item via ItemIO, then place it with the matching vanilla special-draw recipe.

            if (!TryLoadSingleItem(args.Entity.Data, out Item item))
                return false;

            ushort tileType = args.OriginCell.Block.Type;
            Vector2 originPixels = new(
                args.RelX * PreviewDrawContext.TilePixelSize,
                args.RelY * PreviewDrawContext.TilePixelSize);
            PreviewDrawContext ctx = args.Ctx;

            if (tileType == TileID.WeaponsRack)
            {
                DrawWeaponsRackItem(in ctx, item, args.OriginCell.Block.FrameX, originPixels);
                return true;
            }

            if (tileType == TileID.FoodPlatter)
            {
                DrawFoodPlatterItem(in ctx, item, args.OriginCell.Block.FrameX, originPixels);
                return true;
            }

            // Item frames and unknown d.item TEs use the 2x2 frame-center icon.
            Vector2 center = ctx.ToScreen(
                originPixels + new Vector2(PreviewDrawContext.TilePixelSize, PreviewDrawContext.TilePixelSize));
            ItemSlot.DrawItemIcon(
                item,
                31,
                ctx.SpriteBatch,
                center,
                item.scale,
                ItemFrameMaxSize * ctx.Scale,
                ctx.Tint);
            return true;
        }

        /// <summary>Draws a mannequin or hat-rack dummy player from FrameX and captured inventories.</summary>
        public static bool TryDrawPlayerDoll(in PreviewEntityDrawArgs args)
        {
            // --- Load items/dyes when present, then draw a cached dummy player like TEDisplayDoll.Draw.

            ushort tileType = args.OriginCell.Block.Type;
            if (tileType == TileID.HatRack || args.Entity.FullName == PayloadEntityNames.HatRack)
                return TryDrawHatRack(in args);

            Player doll = GetOrCreateDollPlayer();
            ResetDollAppearance(doll);

            // Gender and facing come from the pedestal FrameX, not from entity data.
            short frameX = args.OriginCell.Block.FrameX;
            doll.direction = frameX % 72 == 36 ? 1 : -1;
            doll.Male = frameX / 72 != 1;
            doll.isDisplayDollOrInanimate = true;
            doll.isHatRackDoll = false;
            doll.invis = false;

            EnsureScratchInventories(DisplayDollArmorSlots);
            ClearInventory(_armorScratch!);
            ClearInventory(_dyeScratch!);
            TryLoadInventories(args.Entity.Data, _armorScratch!, _dyeScratch!);

            for (int i = 0; i < DisplayDollArmorSlots; i++)
            {
                doll.armor[i] = _armorScratch![i];
                doll.dye[i] = _dyeScratch![i];
            }

            PrepareDollFrame(doll);

            Vector2 localPos = new Vector2(args.RelX + 1, args.RelY + 3) * PreviewDrawContext.TilePixelSize
                + new Vector2(-doll.width / 2f, -doll.height - 6f);
            PreviewDrawContext ctx = args.Ctx;
            DrawDollAtLocal(in ctx, doll, localPos);
            return true;
        }

        /// <summary>Invokes a public Draw(int,int) on a scratch TileEntity after LoadData, without registering it in the world.</summary>
        public static bool TryDrawGenericTileEntity(in PreviewEntityDrawArgs args)
        {
            // --- Construct a detached TE, load payload tags, and call Draw at scratch coords if that method exists.
            // Phase 2: ModTile.SpecialDraw still needs a live ByPosition TE plus DrawEffects queueing; skip that here.

            if (!TryCreateScratchTileEntity(args.Entity.FullName, out TileEntity? te) || te is null)
                return false;

            TagCompound? data = args.Entity.Data;
            if (data is { Count: > 0 })
            {
                try
                {
                    te.LoadData(data);
                }
                catch
                {
                    return false;
                }
            }

            MethodInfo? draw = te.GetType().GetMethod(
                "Draw",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: [typeof(int), typeof(int)],
                modifiers: null);
            if (draw is null)
                return false;

            PreviewTileDraw.GetSafePreviewCoords(out int scratchX, out int scratchY);
            if (!WorldGen.InWorld(scratchX, scratchY))
                return false;

            Tile live = Main.tile[scratchX, scratchY];
            Tile backup = new();
            backup.CopyFrom(live);
            args.OriginCell.CopyToWorldTile(scratchX, scratchY);
            te.Position = new Point16(scratchX, scratchY);

            try
            {
                draw.Invoke(te, [scratchX, scratchY]);
                UsedPlayerRenderer = true;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                live.CopyFrom(backup);
            }
        }

        /// <summary>Draws a weapons-rack item at vanilla (+24,+24) with FrameX facing.</summary>
        private static void DrawWeaponsRackItem(in PreviewDrawContext ctx, Item item, short frameX, Vector2 originPixels)
        {
            // --- Scale to 40px, flip when the rack's FrameX is on the right-facing half.

            Main.GetItemDrawFrame(item.type, out Texture2D texture, out Rectangle frame);
            float scale = 1f;
            float max = WeaponsRackMaxSize;
            if (frame.Width > max || frame.Height > max)
                scale = frame.Width <= frame.Height ? max / frame.Height : max / frame.Width;
            scale *= item.scale;

            Color color = ctx.Tint;
            float lightScale = 1f;
            ItemSlot.GetItemLight(ref color, ref lightScale, item);
            scale *= lightScale;

            SpriteEffects effects = frameX < 54 ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
            Vector2 pos = ctx.ToScreen(originPixels + new Vector2(24f, 24f));
            Vector2 origin = new(frame.Width / 2f, frame.Height / 2f);
            ctx.SpriteBatch.Draw(texture, pos, frame, color, 0f, origin, scale * ctx.Scale, effects, 0f);
            if (item.color != default)
                ctx.SpriteBatch.Draw(texture, pos, frame, item.GetColor(ctx.Tint), 0f, origin, scale * ctx.Scale, effects, 0f);
        }

        /// <summary>Draws a food-platter item at vanilla (+8,+18) using the plated food frame when needed.</summary>
        private static void DrawFoodPlatterItem(in PreviewDrawContext ctx, Item item, short frameX, Vector2 originPixels)
        {
            // --- Use the plated food sheet row for food items, and flip from FrameX.

            Main.instance.LoadItem(item.type);
            Texture2D texture = TextureAssets.Item[item.type].Value;
            Rectangle frame = ItemID.Sets.IsFood[item.type] ? texture.Frame(1, 3, 0, 2) : texture.Frame();

            Color color = ctx.Tint;
            float scale = 1f;
            ItemSlot.GetItemLight(ref color, ref scale, item);

            SpriteEffects effects = frameX == 0 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Vector2 pos = ctx.ToScreen(originPixels + new Vector2(8f, 18f));
            Vector2 origin = new(frame.Width / 2f, frame.Height);
            ctx.SpriteBatch.Draw(texture, pos, frame, color, 0f, origin, scale * ctx.Scale, effects, 0f);
            if (item.color != default)
                ctx.SpriteBatch.Draw(texture, pos, frame, item.GetColor(ctx.Tint), 0f, origin, scale * ctx.Scale, effects, 0f);
        }

        /// <summary>Draws the two hat-rack dummy heads using captured hat and dye slots.</summary>
        private static bool TryDrawHatRack(in PreviewEntityDrawArgs args)
        {
            // --- Two invisible doll passes, matching TEHatRack.Draw offsets and facing.

            Player doll = GetOrCreateDollPlayer();
            ResetDollAppearance(doll);
            doll.isDisplayDollOrInanimate = true;
            doll.isHatRackDoll = true;
            doll.Male = true;

            short frameX = args.OriginCell.Block.FrameX;
            doll.direction = frameX % 216 == 54 ? 1 : -1;

            EnsureScratchInventories(HatRackSlots);
            ClearInventory(_armorScratch!);
            ClearInventory(_dyeScratch!);
            TryLoadInventories(args.Entity.Data, _armorScratch!, _dyeScratch!);

            Vector2 rackOrigin = new Vector2(args.RelX + 1.5f, args.RelY + 4f) * PreviewDrawContext.TilePixelSize;
            PreviewDrawContext ctx = args.Ctx;

            // First hat: invert facing, offset 14px sideways and -2px up.
            doll.armor[0] = _armorScratch![0];
            doll.dye[0] = _dyeScratch![0];
            doll.invis = true;
            PrepareDollFrame(doll);
            doll.direction *= -1;
            Vector2 firstOffset = new Vector2(-doll.width / 2f, -doll.height - 6f)
                + new Vector2(doll.direction * 14f, -2f);
            DrawDollAtLocal(in ctx, doll, rackOrigin + firstOffset);

            // Second hat: invert facing again, offset 12px sideways and +16px down.
            doll.armor[0] = _armorScratch[1];
            doll.dye[0] = _dyeScratch[1];
            doll.invis = true;
            doll.skipAnimatingValuesInPlayerFrame = true;
            PrepareDollFrame(doll);
            doll.skipAnimatingValuesInPlayerFrame = false;
            doll.direction *= -1;
            Vector2 secondOffset = new Vector2(-doll.width / 2f, -doll.height - 6f)
                + new Vector2(doll.direction * 12f, 16f);
            DrawDollAtLocal(in ctx, doll, rackOrigin + secondOffset);
            return true;
        }

        /// <summary>Runs vanilla dummy-player update so armor frames are ready to draw.</summary>
        private static void PrepareDollFrame(Player doll)
        {
            // --- Match TEDisplayDoll.Draw's ResetEffects through PlayerFrame sequence.

            doll.ResetEffects();
            doll.ResetVisibleAccessories();
            doll.UpdateDyes();
            doll.DisplayDollUpdate();
            doll.UpdateSocialShadow();
            doll.PlayerFrame();
        }

        /// <summary>Draws the dummy player so its world position lands on the preview sprite batch.</summary>
        private static void DrawDollAtLocal(in PreviewDrawContext ctx, Player doll, Vector2 localPixels)
        {
            // --- Convert preview-local pixels to a Camera world position, then scale the player sprite.

            Vector2 worldPos = ctx.ToScreen(localPixels) + Main.screenPosition;
            doll.position = worldPos;
            Main.PlayerRenderer.DrawPlayer(
                Main.Camera,
                doll,
                doll.position,
                0f,
                doll.fullRotationOrigin,
                0f,
                ctx.Scale);
            UsedPlayerRenderer = true;
        }

        /// <summary>Returns the cached dummy player, creating it with vanilla display-doll defaults.</summary>
        private static Player GetOrCreateDollPlayer()
        {
            // --- One Player instance for all dolls this session; appearance is reset per draw.

            if (_dollPlayer is not null)
                return _dollPlayer;

            _dollPlayer = new Player();
            ResetDollAppearance(_dollPlayer);
            return _dollPlayer;
        }

        /// <summary>Applies vanilla display-doll hair, skin, and inanimate flags.</summary>
        private static void ResetDollAppearance(Player doll)
        {
            // --- White skin, hair 15, and skinVariant 10 match TEDisplayDoll's constructor.

            doll.hair = 15;
            doll.skinColor = Color.White;
            doll.skinVariant = 10;
            doll.isDisplayDollOrInanimate = true;
            doll.isFullbright = true;
        }

        /// <summary>Ensures scratch armor and dye arrays exist at least <paramref name="length"/> slots long.</summary>
        private static void EnsureScratchInventories(int length)
        {
            // --- Grow the cached arrays when switching between hat racks and mannequins.

            if (_armorScratch is null || _armorScratch.Length < length)
                _armorScratch = CreateEmptyItems(length);
            if (_dyeScratch is null || _dyeScratch.Length < length)
                _dyeScratch = CreateEmptyItems(length);
        }

        /// <summary>Allocates an Item array filled with empty item instances.</summary>
        private static Item[] CreateEmptyItems(int length)
        {
            // --- PlayerIO.LoadInventory expects pre-sized slots, not null entries.

            var items = new Item[length];
            for (int i = 0; i < length; i++)
                items[i] = new Item();
            return items;
        }

        /// <summary>Clears every scratch slot back to air before loading payload inventories.</summary>
        private static void ClearInventory(Item[] items)
        {
            // --- Drop leftover armor from the previous entity so empty dolls stay empty.

            for (int i = 0; i < items.Length; i++)
            {
                items[i] ??= new Item();
                items[i].TurnToAir();
            }
        }

        /// <summary>Loads ItemIO items and dyes lists from a payload tag when those keys exist.</summary>
        private static void TryLoadInventories(TagCompound? data, Item[] items, Item[] dyes)
        {
            // --- Empty d is valid: the dummy body still draws without clothes.

            if (data is null)
                return;

            try
            {
                if (data.ContainsKey("items"))
                    LoadInventorySlots(items, data.GetList<TagCompound>("items"));
                if (data.ContainsKey("dyes"))
                    LoadInventorySlots(dyes, data.GetList<TagCompound>("dyes"));
            }
            catch
            {
                // Leave slots empty when the tag is not an ItemIO inventory.
            }
        }

        /// <summary>Applies sparse ItemIO slot compounds onto a pre-sized item array.</summary>
        private static void LoadInventorySlots(Item[] dest, IList<TagCompound> slots)
        {
            // --- Each compound is ItemIO.Save plus a slot index, matching capture and PlayerIO lists.

            for (int i = 0; i < slots.Count; i++)
            {
                TagCompound tag = slots[i];
                int slot = TryReadSlotIndex(tag, i);
                if (slot < 0 || slot >= dest.Length)
                    continue;

                Item loaded = ItemIO.Load(tag);
                if (loaded is null || loaded.IsAir)
                    continue;

                dest[slot] = loaded;
            }
        }

        /// <summary>Reads an ItemIO <c>slot</c> index stored as short or int, or the list index when omitted.</summary>
        private static int TryReadSlotIndex(TagCompound tag, int fallback) =>
            PayloadEntityHandlers.ReadSlotIndex(tag, fallback);

        /// <summary>Loads a non-air item from an ItemIO <c>item</c> compound on the entity tag.</summary>
        private static bool TryLoadSingleItem(TagCompound? data, out Item item)
        {
            // --- Require a compound ItemIO blob; skip air and malformed tags.

            item = null!;
            if (data is null || !data.ContainsKey("item") || data["item"] is not TagCompound itemTag)
                return false;

            try
            {
                item = ItemIO.Load(itemTag);
            }
            catch
            {
                return false;
            }

            return item is { IsAir: false };
        }

        /// <summary>Creates a detached TileEntity instance from a payload FullName without placing it in the world.</summary>
        private static bool TryCreateScratchTileEntity(string fullName, out TileEntity? te)
        {
            // --- Prefer ModTileEntity.ConstructFromBase; vanilla names fall back to Activator.

            te = null;
            if (string.IsNullOrEmpty(fullName))
                return false;

            foreach (ModTileEntity template in ModContent.GetContent<ModTileEntity>())
            {
                if (!string.Equals(template.FullName, fullName, StringComparison.Ordinal))
                    continue;

                te = ModTileEntity.ConstructFromBase(template);
                return te is not null;
            }

            if (fullName.StartsWith("Terraria:", StringComparison.Ordinal))
            {
                string className = fullName["Terraria:".Length..];
                Type? type = typeof(TileEntity).Assembly.GetType($"Terraria.GameContent.Tile_Entities.{className}");
                if (type is null || !typeof(TileEntity).IsAssignableFrom(type))
                    return false;

                te = Activator.CreateInstance(type) as TileEntity;
                return te is not null;
            }

            return false;
        }
    }
}
