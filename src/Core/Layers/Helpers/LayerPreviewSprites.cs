using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Enums;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.UI.Preview;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    /// <summary>Shared payload-cell sprite resolve and draw helpers for preview layers.</summary>
    internal static class LayerPreviewSprites
    {
        /// <summary>Draws a resolved tile sprite at the payload-relative cell origin.</summary>
        public static void DrawSprite(
            in PreviewDrawContext ctx,
            int relX,
            int relY,
            in PreviewTileDraw.DrawSprite sprite,
            Color? tintOverride = null)
        {
            Vector2 pos = PreviewTileDraw.GetDrawPosition(ctx.Position, relX, relY, in sprite);
            Color tint = tintOverride ?? ctx.Tint;
            ctx.SpriteBatch.Draw(sprite.Texture, pos * ctx.Scale, sprite.Source, tint, 0f, Vector2.Zero, ctx.Scale, sprite.Effects, 0f);
        }

        /// <summary>Resolves a payload cell sprite, using payload-soil tree wood when the tile is a tree.</summary>
        public static bool TryResolveSprite(
            in PreviewDrawContext ctx,
            PayloadCell cell,
            int relX,
            int relY,
            out PreviewTileDraw.DrawSprite sprite)
        {
            sprite = default;
            if (!PreviewTileDraw.TryPrepare(cell, out PreviewTileDraw.PreparedTile prepared))
                return false;

            if (prepared.TileType == TileID.Trees || prepared.TileType == TileID.PalmTree)
            {
                TreePreviewResolve.Style style = TreePreviewResolve.Resolve(
                    in ctx,
                    prepared.TileType,
                    prepared.Tile.TileFrameX,
                    prepared.Tile.TileFrameY,
                    relX,
                    relY);
                return PreviewTileDraw.TryResolveTreeWoodSprite(in prepared, style.WoodVariant, style.PalmBiome, out sprite);
            }

            return PreviewTileDraw.TryResolve(in prepared, out sprite);
        }

        /// <summary>Returns true when detail state defers plain-block drawing to DetailLayer.</summary>
        public static bool HasDetailModifiedSprite(PayloadCell cell)
        {
            PayloadCell.DetailData detail = cell.Detail;
            return detail.IsActuated
                || detail.IsHalfBlock
                || detail.Slope != SlopeType.Solid
                || detail.BlockType != BlockType.Solid;
        }
    }
}
