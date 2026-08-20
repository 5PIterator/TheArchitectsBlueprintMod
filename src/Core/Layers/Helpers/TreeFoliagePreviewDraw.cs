using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.Drawing;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.UI.Preview;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    /// <summary>Draws vanilla tree-top and branch overlays for payload preview cells.</summary>
    internal static class TreeFoliagePreviewDraw
    {
        private const int FoliageMinFrameY = 198;
        private const int TopFrameX = 22;
        private const int LeftBranchFrameX = 44;
        private const int RightBranchFrameX = 66;
        private const int TreeTopPadding = 2;
        private const int TreeBranchSize = 40;
        private const int TreeBranchStride = 42;
        private const int PalmTopMinFrameX = 88;
        private const int PalmTopMaxFrameX = 132;

        /// <summary>Draws tree-top or branch foliage using vanilla FrameX/FrameY overlay gates.</summary>
        public static void Draw(in PreviewDrawContext ctx, PayloadCell cell, int relX, int relY)
        {
            // --- Skip non-tree cells, then overlay tops/branches only on foliage frames.

            if (!cell.Detail.HasTile)
                return;

            ushort tileType = cell.Block.Type;
            if (!TreePreviewResolve.IsFoliageTreeType(tileType))
                return;

            short frameX = cell.Block.FrameX;
            short frameY = cell.Block.FrameY;
            TreePreviewResolve.Style style = TreePreviewResolve.Resolve(in ctx, tileType, frameX, frameY, relX, relY);

            // Palms use a separate FrameX 88-132 overlay, not the forest foliage frames.
            if (tileType == TileID.PalmTree)
            {
                DrawPalmTop(in ctx, cell, relX, relY, frameX, frameY, style);
                return;
            }

            // DrawTrees only overlays leafy frames: FrameY >= 198 and FrameX 22/44/66.
            if (frameY < FoliageMinFrameY)
                return;

            // Read the 0/1/2 foliage variant from the payload tile's stored frames.
            Tile tile = cell.ToTile();
            int treeFrame = Math.Max(0, WorldGen.GetTreeFrame(tile)) + style.TreeFrameBonus;

            // FrameX 22 is a crown; 44 is a left branch; 66 is a right branch.
            switch (frameX)
            {
                case TopFrameX:
                    DrawTreeTop(in ctx, cell, relX, relY, style, treeFrame);
                    break;
                case LeftBranchFrameX:
                    DrawBranch(in ctx, cell, relX, relY, style, treeFrame, left: true);
                    break;
                case RightBranchFrameX:
                    DrawBranch(in ctx, cell, relX, relY, style, treeFrame, left: false);
                    break;
            }
        }

        /// <summary>Draws a TreeTop sprite with vanilla origin at the top trunk cell.</summary>
        private static void DrawTreeTop(
            in PreviewDrawContext ctx,
            PayloadCell cell,
            int relX,
            int relY,
            TreePreviewResolve.Style style,
            int treeFrame)
        {
            // --- Place the biome-sized crown with origin at the bottom-center of the top trunk tile.

            if (!TryGetSheetTexture(style.FoliageIndex, isTop: true, cell.Paint.TileColor, paintStyle: 0, out Texture2D texture))
                return;

            int width = style.TopWidth;
            int height = style.TopHeight;

            // Sample the crown column for this foliage variant at the biome's frame size.
            var source = new Rectangle(treeFrame * (width + TreeTopPadding), 0, width, height);

            // Anchor at the bottom-center of the top trunk tile, matching DrawTrees.
            Vector2 pos = ctx.Position + new Vector2(
                relX * PreviewDrawContext.TilePixelSize + 8f,
                relY * PreviewDrawContext.TilePixelSize + PreviewDrawContext.TilePixelSize);
            Vector2 origin = new(width / 2f, height);

            ctx.SpriteBatch.Draw(texture, pos * ctx.Scale, source, ctx.Tint, 0f, origin, ctx.Scale, SpriteEffects.None, 0f);
        }

        /// <summary>Draws a TreeBranch sprite using vanilla left/right origins and source rows.</summary>
        private static void DrawBranch(
            in PreviewDrawContext ctx,
            PayloadCell cell,
            int relX,
            int relY,
            TreePreviewResolve.Style style,
            int treeFrame,
            bool left)
        {
            // --- Sample the 40x40 branch frame and anchor it like DrawTrees.

            if (!TryGetSheetTexture(style.FoliageIndex, isTop: false, cell.Paint.TileColor, paintStyle: 0, out Texture2D texture))
                return;

            // Left branches occupy column 0; right branches occupy column 42.
            int sourceX = left ? 0 : TreeBranchStride;
            var source = new Rectangle(sourceX, treeFrame * TreeBranchStride, TreeBranchSize, TreeBranchSize);

            // Use DrawTrees origins: left (40, 24) at +16,+12; right (0, 30) at +0,+18.
            Vector2 pos;
            Vector2 origin;
            if (left)
            {
                pos = ctx.Position + new Vector2(
                    relX * PreviewDrawContext.TilePixelSize + PreviewDrawContext.TilePixelSize,
                    relY * PreviewDrawContext.TilePixelSize + 12f);
                origin = new Vector2(40f, 24f);
            }
            else
            {
                pos = ctx.Position + new Vector2(
                    relX * PreviewDrawContext.TilePixelSize,
                    relY * PreviewDrawContext.TilePixelSize + 18f);
                origin = new Vector2(0f, 30f);
            }

            ctx.SpriteBatch.Draw(texture, pos * ctx.Scale, source, ctx.Tint, 0f, origin, ctx.Scale, SpriteEffects.None, 0f);
        }

        /// <summary>Draws a palm crown when FrameX is in the vanilla palm-top range.</summary>
        private static void DrawPalmTop(
            in PreviewDrawContext ctx,
            PayloadCell cell,
            int relX,
            int relY,
            short frameX,
            short frameY,
            TreePreviewResolve.Style style)
        {
            // --- Palms overlay only FrameX 88/110/132; sheet row comes from the sand biome below.

            if (frameX < PalmTopMinFrameX || frameX > PalmTopMaxFrameX)
                return;

            // FrameX 88/110/132 select palm crown variants 0/1/2.
            int variant = frameX switch
            {
                110 => 1,
                132 => 2,
                _ => 0,
            };

            const int width = 80;
            const int height = 80;
            const int originX = 32;
            const int originY = 0;
            int sourceY = Math.Max(0, style.PalmBiome) * 82;

            if (!TryGetSheetTexture(style.FoliageIndex, isTop: true, cell.Paint.TileColor, style.PalmBiome, out Texture2D texture))
                return;

            // Place the crown with vanilla beach offsets; stored FrameY is a horizontal sway.
            var source = new Rectangle(variant * (width + 2), sourceY, width, height);
            Vector2 pos = ctx.Position + new Vector2(
                relX * PreviewDrawContext.TilePixelSize - originX + frameY + width / 2f,
                relY * PreviewDrawContext.TilePixelSize + PreviewDrawContext.TilePixelSize + originY);
            Vector2 origin = new(width / 2f, height);

            ctx.SpriteBatch.Draw(texture, pos * ctx.Scale, source, ctx.Tint, 0f, origin, ctx.Scale, SpriteEffects.None, 0f);
        }

        /// <summary>Resolves a TreeTop/TreeBranch sheet by index, with paint fallback to the raw asset.</summary>
        private static bool TryGetSheetTexture(
            int treeTopIndex,
            bool isTop,
            byte paint,
            int paintStyle,
            out Texture2D texture)
        {
            // --- Ask the paint system first, then unwrap TextureAssets.TreeTop or TreeBranch.

            texture = null!;
            if (treeTopIndex < 0)
                return false;

            // Prefer a painted sheet when the paint system has this variant ready.
            TilePaintSystemV2? paintSystem = PreviewTileDraw.TryGetPaintSystem();
            Texture2D? painted = isTop
                ? paintSystem?.TryGetTreeTopAndRequestIfNotReady(treeTopIndex, paintStyle, paint)
                : paintSystem?.TryGetTreeBranchAndRequestIfNotReady(treeTopIndex, paintStyle, paint);
            if (painted is not null)
            {
                texture = painted;
                return true;
            }

            // Fall back to the unpainted TreeTop or TreeBranch asset.
            Asset<Texture2D>[] sheets = isTop ? TextureAssets.TreeTop : TextureAssets.TreeBranch;
            if (treeTopIndex >= sheets.Length || sheets[treeTopIndex] is null)
                return false;

            texture = sheets[treeTopIndex].Value;
            return texture is not null;
        }
    }
}
