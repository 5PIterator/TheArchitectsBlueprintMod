using System.Collections.Generic;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.UI.Preview;
using Terraria;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    internal sealed class UnknownLayer : Layer
    {
        public static readonly UnknownLayer Definition = new();

        private UnknownLayer() : base("Unknown", LayerType.Block) { }

        public static bool IsUnknownTileType(ushort tileType) => TileLoaderSupport.IsUnknownTileType(tileType);

        public override bool Match(in PasteTile tile)
        {
            if (TileComparison.IsSourceCutIntent(in tile))
                return tile.Source.HasTile == tile.Target.HasTile && (!tile.Source.HasTile || tile.Source.TileType == tile.Target.TileType);
            Tile intent = tile.PayloadTile;
            return intent.HasTile == tile.Target.HasTile && (!intent.HasTile || intent.TileType == tile.Target.TileType);
        }

        public override LayerResult Identify(in PasteTile tile, in PasteTileLookup _, in PasteContext context, PasteSubject identifySubject)
        {
            Tile view = PasteTile.GetTileForIdentify(in tile, identifySubject);
            if (!view.HasTile || !IsUnknownTileType(view.TileType))
                return LayerResult.IdentifyNone();

            return LayerResult.Identified(
                TileBatch.FromCells([new Blueprint.BlueprintCell(tile.Relative, tile.World, true)]),
                LayerLogFormat.FormatTile(view));
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context) =>
            tile.Target.HasTile;

        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override LayerResult Clear(TileBatch batch, in PasteContext context)
        {
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0)
                return LayerResult.Ok();

            var tile = tiles[0];
            int x = tile.World.X, y = tile.World.Y;
            bool hadTile = Main.tile[x, y].HasTile;
            string? message = hadTile ? LayerLogFormat.FormatTile(Main.tile[x, y]) : null;

            LayerResult res = TileOperations.Clear(in tile, in context, message);
            if (!res.Success)
                return LayerResult.Fail(res.FailReason, message);

            return hadTile ? LayerResult.Ok(message, LayerLogFormat.ClearedLabel) : LayerResult.Ok();
        }

        public override LayerResult Place(TileBatch batch, in PasteContext context)
        {
            // --- Require a mapped interaction; otherwise fail so ignore-items cannot silently succeed.

            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0)
                return LayerResult.Ok();

            var origin = tiles[0];
            if (!origin.PayloadCell.HasTile())
                return LayerResult.Ok();

            ushort tileType = (ushort)origin.PayloadCell.Block.Type;
            int style = origin.PayloadCell.ResolvePlacementStyle();
            string message = LayerLogFormat.FormatTile(origin.PayloadTile);

            // Fail when no interaction exists, even if item requirements are disabled.
            if (!InteractionMapProvider.HasResolvedTileInteraction(tileType, style))
                return LayerResult.Fail(LayerFailReason.UnresolvedInteraction, message);

            if (!TileExecutor.TryCheckTileItem(tileType, style, in context, out LayerFailReason itemFailReason))
                return LayerResult.Fail(itemFailReason, message);

            LayerResult res = TileOperations.Place(tiles, in context);
            if (!res.Success)
                return LayerResult.Fail(res.FailReason, message);

            if (res.RemainingTiles.Count > 0)
                return LayerResult.OkWithRemaining(batch.WithRemainingTiles(res.RemainingTiles), message, LayerLogFormat.PlacedLabel(in context));

            return LayerResult.Ok(message, LayerLogFormat.PlacedLabel(in context));
        }

        public override bool DrawsCells => true;

        public override void DrawCell(in PreviewDrawContext ctx, PayloadCell cell, int relX, int relY)
        {
            if (LayerPreviewSprites.HasDetailModifiedSprite(cell))
                return;

            Tile tile = cell.ToTile();
            if (!tile.HasTile || !IsUnknownTileType(tile.TileType))
                return;

            if (!LayerPreviewSprites.TryResolveSprite(in ctx, cell, relX, relY, out PreviewTileDraw.DrawSprite sprite))
                return;

            LayerPreviewSprites.DrawSprite(in ctx, relX, relY, in sprite);
        }
    }
}
