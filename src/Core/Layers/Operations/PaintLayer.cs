using System.Collections.Generic;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;
using Terraria;
using Terraria.ID;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    internal sealed class PaintLayer : Layer
    {
        public static readonly PaintLayer Definition = new();

        private PaintLayer() : base("Paint", LayerType.Paint) { }

        public override LayerResult Identify(in PasteTile tile, in PasteTileLookup _, in PasteContext context, PasteSubject identifySubject)
        {
            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            if (!HasPaintOrCoating(subjectTile))
                return LayerResult.IdentifyNone();

            return LayerResult.Identified(
                TileBatch.FromCells([new Blueprint.BlueprintCell(tile.Relative, tile.World, true)]),
                LayerLogFormat.FormatPaintState(subjectTile));
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context) =>
            HasPaintOrCoating(tile.Target);

        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool Match(in PasteTile tile)
        {
            if (TileComparison.IsSourceCutIntent(in tile))
                return PaintStateMatches(tile.Source, tile.Target);
            return PaintStateMatches(tile.PayloadTile, tile.Target);
        }

        public override LayerResult Clear(TileBatch batch, in PasteContext context)
        {
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0)
                return LayerResult.Ok();

            var tile = tiles[0];
            int x = tile.World.X, y = tile.World.Y;
            Tile live = Main.tile[x, y];
            string message = LayerLogFormat.FormatPaintState(live);

            if (!HasPaintOrCoating(live))
                return LayerResult.Ok();

            if (!TileExecutor.EnqueueApplyPaintState(
                    x,
                    y,
                    PaintID.None,
                    PaintID.None,
                    tileInvisible: false,
                    wallInvisible: false,
                    tileFullbright: false,
                    wallFullbright: false,
                    in context))
            {
                return LayerResult.Fail(LayerFailReason.ClearBlocked, message);
            }

            return LayerResult.Ok(message, LayerLogFormat.ClearedLabel);
        }

        public override LayerResult Place(TileBatch batch, in PasteContext context)
        {
            // --- Reject missing paint/coating items before enqueue so failures stay synchronous.

            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0)
                return LayerResult.Ok();

            var tile = tiles[0];
            PayloadCell.PaintData src = tile.PayloadCell.Paint;
            int wx = tile.World.X, wy = tile.World.Y;
            Tile live = Main.tile[wx, wy];
            string message = LayerLogFormat.FormatPaintState(tile.PayloadCell);

            if (PaintStateMatches(tile.PayloadTile, live))
                return LayerResult.Ok();

            bool consumeItem = context.RequireItems && !context.IsSweep;
            if (consumeItem && !LayerItemChecks.HasRequiredPaintItems(in src, live, in context))
            {
                LayerFailReason reason = ResolvePaintItemFailReason(in src, live, in context);
                return LayerResult.Fail(reason, message);
            }

            if (!TileExecutor.EnqueueApplyPaintState(
                    wx,
                    wy,
                    src.TileColor,
                    src.WallColor,
                    src.TileInvisible,
                    src.WallInvisible,
                    src.TileFullbright,
                    src.WallFullbright,
                    in context,
                    consumeItem))
            {
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            return LayerResult.Ok(message, LayerLogFormat.PaintedLabel);
        }

        /// <summary>Chooses MissingItems vs UnresolvedInteraction for a failed paint preflight.</summary>
        private static LayerFailReason ResolvePaintItemFailReason(in PayloadCell.PaintData intent, in Tile target, in PasteContext context)
        {
            // --- Unresolved when no map candidates exist; otherwise inventory is missing.

            if (intent.TileColor != PaintID.None && target.TileColor != intent.TileColor
                && !HasPaintInteractions(intent.TileColor))
                return LayerFailReason.UnresolvedInteraction;
            if (intent.WallColor != PaintID.None && target.WallColor != intent.WallColor
                && !HasPaintInteractions(intent.WallColor))
                return LayerFailReason.UnresolvedInteraction;
            if (NeedsEchoCoating(in intent, in target)
                && !HasPaintInteractions(ContentNameResolver.EchoCoatingId))
                return LayerFailReason.UnresolvedInteraction;
            if (NeedsIlluminantCoating(in intent, in target)
                && !HasPaintInteractions(ContentNameResolver.IlluminantCoatingId))
                return LayerFailReason.UnresolvedInteraction;
            return LayerFailReason.MissingItems;
        }

        private static bool HasPaintInteractions(int paintMainId)
        {
            foreach (InteractionEntry _ in InteractionMapProvider.EnumeratePaintInteractions(paintMainId))
                return true;
            return false;
        }

        private static bool NeedsEchoCoating(in PayloadCell.PaintData intent, in Tile target) =>
            (intent.TileInvisible && target.HasTile && !target.IsTileInvisible)
            || (intent.WallInvisible && target.WallType != WallID.None && !target.IsWallInvisible);

        private static bool NeedsIlluminantCoating(in PayloadCell.PaintData intent, in Tile target) =>
            (intent.TileFullbright && target.HasTile && !target.IsTileFullbright)
            || (intent.WallFullbright && target.WallType != WallID.None && !target.IsWallFullbright);

        private static bool HasPaintOrCoating(Tile tile) =>
            tile.TileColor != PaintID.None
            || tile.WallColor != PaintID.None
            || tile.IsTileInvisible
            || tile.IsWallInvisible
            || tile.IsTileFullbright
            || tile.IsWallFullbright;

        /// <summary>True when every paint color and coating flag matches between intent and target.</summary>
        internal static bool PaintStateMatches(Tile intent, Tile target) =>
            intent.TileColor == target.TileColor
            && intent.WallColor == target.WallColor
            && intent.IsTileInvisible == target.IsTileInvisible
            && intent.IsWallInvisible == target.IsWallInvisible
            && intent.IsTileFullbright == target.IsTileFullbright
            && intent.IsWallFullbright == target.IsWallFullbright;

        /// <summary>True when payload paint/coating facets match the live world tile.</summary>
        internal static bool PaintStateMatches(in PayloadCell.PaintData intent, Tile target) =>
            intent.TileColor == target.TileColor
            && intent.WallColor == target.WallColor
            && intent.TileInvisible == target.IsTileInvisible
            && intent.WallInvisible == target.IsWallInvisible
            && intent.TileFullbright == target.IsTileFullbright
            && intent.WallFullbright == target.IsWallFullbright;
    }
}
