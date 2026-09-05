using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Enums;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.UI.Preview;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    internal sealed class DetailLayer : Layer
    {
        public static readonly DetailLayer Definition = new();

        private DetailLayer() : base("Detail", LayerType.Detail) { }

        public override LayerResult Identify(in PasteTile tile, in PasteTileLookup _, in PasteContext context, PasteSubject identifySubject)
        {
            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            bool hasDetail = subjectTile.IsActuated || subjectTile.BlockType != BlockType.Solid;
            if (!hasDetail)
                return LayerResult.IdentifyNone();

            return LayerResult.Identified(
                TileBatch.FromCells([new Blueprint.BlueprintCell(tile.Relative, tile.World, true)]),
                LayerLogFormat.FormatDetailState(subjectTile));
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            Tile t = tile.Target;
            return t.IsActuated || t.BlockType != BlockType.Solid;
        }

        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool Match(in PasteTile tile)
        {
            if (TileComparison.IsSourceCutIntent(in tile))
                return tile.Source.IsActuated == tile.Target.IsActuated && tile.Source.BlockType == tile.Target.BlockType;
            Tile intent = tile.PayloadTile;
            return intent.IsActuated == tile.Target.IsActuated && intent.BlockType == tile.Target.BlockType;
        }

        public override LayerResult Clear(TileBatch batch, in PasteContext context)
        {
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0)
                return LayerResult.Ok();

            var tile = tiles[0];
            int wx = tile.World.X, wy = tile.World.Y;
            Tile live = Main.tile[wx, wy];
            string message = LayerLogFormat.FormatDetailState(live);

            bool alreadyCleared = !live.IsActuated
                && !live.IsHalfBlock
                && live.Slope == SlopeType.Solid
                && live.BlockType == BlockType.Solid;
            if (alreadyCleared)
                return LayerResult.Ok();

            if (!TileExecutor.EnqueueApplyDetailState(
                    wx,
                    wy,
                    isActuated: false,
                    isHalfBlock: false,
                    slope: SlopeType.Solid,
                    blockType: BlockType.Solid,
                    in context))
            {
                return LayerResult.Fail(LayerFailReason.ClearBlocked, message);
            }

            return LayerResult.Ok(message, LayerLogFormat.ClearedLabel);
        }

        public override LayerResult Place(TileBatch batch, in PasteContext context)
        {
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0)
                return LayerResult.Ok();

            var tile = tiles[0];
            var src = tile.PayloadCell.Detail;
            int wx = tile.World.X, wy = tile.World.Y;
            if (!Main.tile[wx, wy].HasTile)
                return LayerResult.Ok();

            string message = LayerLogFormat.FormatDetailState(in src);

            Tile live = Main.tile[wx, wy];
            bool alreadyMatching = src.IsHalfBlock == live.IsHalfBlock
                && src.Slope == live.Slope
                && src.IsActuated == live.IsActuated
                && src.BlockType == live.BlockType;
            if (alreadyMatching)
                return LayerResult.Ok();

            if (!TileExecutor.EnqueueApplyDetailState(
                    wx,
                    wy,
                    isActuated: src.IsActuated,
                    isHalfBlock: src.IsHalfBlock,
                    slope: src.Slope,
                    blockType: src.BlockType,
                    in context))
            {
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            return LayerResult.Ok(message, LayerLogFormat.DetailedLabel);
        }

        public override bool DrawsCells => true;

        public override void DrawCell(in PreviewDrawContext ctx, PayloadCell cell, int relX, int relY)
        {
            PayloadCell.DetailData detail = cell.Detail;
            if (!detail.IsActuated && !detail.IsHalfBlock && detail.Slope == SlopeType.Solid && detail.BlockType == BlockType.Solid)
                return;

            if (!LayerPreviewSprites.TryResolveSprite(in ctx, cell, relX, relY, out PreviewTileDraw.DrawSprite sprite))
                return;

            Vector2 pos = PreviewTileDraw.GetDrawPosition(ctx.Position, relX, relY, in sprite);
            Color drawTint = detail.IsActuated ? ctx.Tint * 0.4f : ctx.Tint;
            SlopeType slope = detail.Slope;
            if (slope != SlopeType.Solid)
                PreviewTileDraw.DrawSlope(ctx.SpriteBatch, sprite.Texture, sprite.Source, pos, ctx.Scale, sprite.Effects, slope, drawTint);
            else
                ctx.SpriteBatch.Draw(sprite.Texture, pos * ctx.Scale, sprite.Source, drawTint, 0f, Vector2.Zero, ctx.Scale, sprite.Effects, 0f);
        }
    }
}
