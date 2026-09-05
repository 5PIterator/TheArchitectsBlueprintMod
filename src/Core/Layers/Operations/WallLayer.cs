using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.UI.Preview;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    internal sealed class WallLayer : Layer
    {
        public static readonly WallLayer Definition = new();

        private WallLayer() : base("Wall", LayerType.Wall) { }

        public override LayerResult Identify(in PasteTile tile, in PasteTileLookup _, in PasteContext context, PasteSubject identifySubject)
        {
            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            if (subjectTile.WallType == WallID.None)
                return LayerResult.IdentifyNone();

            return LayerResult.Identified(
                TileBatch.FromCells([new Blueprint.BlueprintCell(tile.Relative, tile.World, true)]),
                LayerLogFormat.FormatWall(subjectTile));
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context) =>
            Main.tile[tile.World.X, tile.World.Y].WallType != WallID.None;

        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool Match(in PasteTile tile)
        {
            // --- Wall layer owns wall type only; paint/coatings are PaintLayer responsibility.

            if (TileComparison.IsSourceCutIntent(in tile))
                return tile.Source.WallType == tile.Target.WallType;

            return tile.PayloadTile.WallType == tile.Target.WallType;
        }

        public override LayerResult Clear(TileBatch batch, in PasteContext context)
        {
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0)
                return LayerResult.Ok();

            string? message = null;
            bool changed = false;
            for (int i = 0; i < tiles.Count; i++)
            {
                int x = tiles[i].World.X, y = tiles[i].World.Y;
                Tile live = Main.tile[x, y];
                if (live.WallType == WallID.None)
                    continue;

                message ??= LayerLogFormat.FormatWall(live);
                TileExecutor.EnqueueClearWall(x, y, in context);
                changed = true;
            }

            return changed ? LayerResult.Ok(message, LayerLogFormat.ClearedLabel) : LayerResult.Ok();
        }

        public override LayerResult Place(TileBatch batch, in PasteContext context)
        {
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0)
                return LayerResult.Ok();

            var tile = tiles[0];
            int x = tile.World.X, y = tile.World.Y;
            ushort wallType = (ushort)tile.PayloadCell.Wall.Type;
            if (Main.tile[x, y].WallType == wallType)
                return LayerResult.Ok();

            string message = LayerLogFormat.FormatWall(tile.PayloadTile);

            // Unknown walls need a mapped interaction before place; ignore-items must not skip this.
            if (TileLoaderSupport.IsUnknownWallType(wallType)
                && !InteractionMapProvider.HasResolvedWallInteraction(wallType))
                return LayerResult.Fail(LayerFailReason.UnresolvedInteraction, message);

            if (!TileExecutor.TryCheckWallItem(wallType, in context, out LayerFailReason itemFailReason))
                return LayerResult.Fail(itemFailReason, message);

            if (!TileExecutor.EnqueuePlaceWall(x, y, wallType, in context, consumeItem: true))
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);

            return LayerResult.Ok(message, LayerLogFormat.PlacedLabel(in context));
        }

        public override bool DrawsCells => true;

        public override void DrawCell(in PreviewDrawContext ctx, PayloadCell cell, int relX, int relY)
        {
            Tile tile = cell.ToTile();
            if (tile.WallType == WallID.None)
                return;

            if ((cell.Paint.WallInvisible || tile.WallType == WallID.EchoWall) && !Main.ShouldShowInvisibleWalls())
                return;

            if (!PreviewTileDraw.TryGetWallTexture(tile.WallType, tile.WallColor, out Texture2D textureWall))
                return;

            int wallFrame = Main.wallFrame[tile.WallType] * 180;
            var source = new Rectangle(tile.WallFrameX, tile.WallFrameY + wallFrame, 32, 32);
            Vector2 pos = ctx.Position + new Vector2(
                relX * PreviewDrawContext.TilePixelSize - 8,
                relY * PreviewDrawContext.TilePixelSize - 8);
            ctx.SpriteBatch.Draw(textureWall, pos * ctx.Scale, source, ctx.Tint, 0f, Vector2.Zero, ctx.Scale, SpriteEffects.None, 0f);
        }
    }
}
