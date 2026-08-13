using System.Collections.Generic;
using Microsoft.Xna.Framework;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    [CustomPasteLayer]
    /// <summary>Identifies and places hanging and reverse vine columns.</summary>
    internal sealed class VineLayer : Layer
    {
        public static readonly VineLayer Definition = new();

        private VineLayer() : base("Vine", LayerType.Custom) { }

        /// <summary>Checks tile-type equality between payload/source intent and target world.</summary>
        public override bool Match(in PasteTile tile) => TileComparison.MatchTileType(in tile);

        /// <summary>Returns the contiguous vine column around this tile in paste bounds.</summary>
        public override LayerResult Identify(in PasteTile tile, in PasteTileLookup lookup, PasteSubject identifySubject)
        {
            // --- Identify only vine-family tiles, then gather vertical neighbors with source-cut support.
            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            if (!subjectTile.HasTile || !IsVineTileType(subjectTile.TileType))
                return LayerResult.IdentifyNone();

            return IdentifyVineColumn(in tile, in lookup, identifySubject, subjectTile.TileType, subjectTile);
        }

        /// <summary>Returns true when this tile type is a standard or reverse vine.</summary>
        private static bool IsVineTileType(ushort tileType) =>
            TileLoaderSupport.IsKnownTileType(tileType)
            && (TileID.Sets.IsVine[tileType] || TileID.Sets.ReverseVineThreads[tileType]);

        /// <summary>Returns true when this tile type is a standard downward vine.</summary>
        private static bool IsStandardVine(ushort tileType) =>
            TileLoaderSupport.IsKnownTileType(tileType) && TileID.Sets.IsVine[tileType];

        /// <summary>Builds a full vertical vine column around the anchor tile.</summary>
        private static LayerResult IdentifyVineColumn(
            in PasteTile tile,
            in PasteTileLookup lookup,
            PasteSubject identifySubject,
            ushort tileType,
            in Tile subjectTile)
        {
            // --- Walk upward and downward while tile type matches to produce one grouped batch for this column.
            bool sourceCut = TileComparison.IsSourceCutIntent(in tile);
            PasteSubject columnSubject = sourceCut ? PasteSubject.Source : identifySubject;

            var list = new List<Blueprint.BlueprintCell>();
            for (int dy = 0; ; dy--)
            {
                Point relative = new(tile.Relative.X, tile.Relative.Y + dy);
                if (!lookup.TryGetPasteTile(relative, out PasteTile lookupTile))
                    break;

                Tile scanTile = PasteTile.GetTileForIdentify(in lookupTile, columnSubject);
                if (!scanTile.HasTile || scanTile.TileType != tileType)
                    break;

                list.Insert(0, new Blueprint.BlueprintCell(relative, ColumnCellWorld(in tile, relative, in lookupTile, sourceCut), true));
            }

            for (int dy = 1; ; dy++)
            {
                Point relative = new(tile.Relative.X, tile.Relative.Y + dy);
                if (!lookup.TryGetPasteTile(relative, out PasteTile lookupTile))
                    break;

                Tile scanTile = PasteTile.GetTileForIdentify(in lookupTile, columnSubject);
                if (!scanTile.HasTile || scanTile.TileType != tileType)
                    break;

                list.Add(new Blueprint.BlueprintCell(relative, ColumnCellWorld(in tile, relative, in lookupTile, sourceCut), true));
            }

            return list.Count > 0
                ? LayerResult.Identified(TileBatch.FromCells(list), LayerLogFormat.FormatTile(subjectTile))
                : LayerResult.IdentifyNone();
        }

        /// <summary>Resolves world coordinates for identified column cells during normal and source-cut passes.</summary>
        private static Point ColumnCellWorld(in PasteTile anchor, Point relative, in PasteTile lookupTile, bool sourceCut) =>
            sourceCut
                ? new Point(anchor.World.X + relative.X - anchor.Relative.X, anchor.World.Y + relative.Y - anchor.Relative.Y)
                : lookupTile.World;

        /// <summary>Checks whether the target tile currently has anything to clear.</summary>
        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context) =>
            Main.tile[tile.World.X, tile.World.Y].HasTile;

        /// <summary>Validates world-side placement constraints for this layer.</summary>
        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        /// <summary>Checks whether inventory has the required item for this tile when item checks are enabled.</summary>
        public override bool HasRequiredItems(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            // --- Skip checks when requirements are disabled; otherwise query interaction-map item ids for payload tile + style.
            if (!context.RequireItems || context.Items is null || context.IsSweepCheat) return true;
            if (!tile.PayloadCell.HasTile()) return true;
            int style = tile.PayloadCell.ResolvePlacementStyle();
            return TileExecutor.HasItemForTile((ushort)tile.PayloadCell.Block.Type, style, in context);
        }

        /// <summary>Checks whether this tile can consume an inventory item now.</summary>
        public override bool CanConsume(in PasteTile tile, in PasteContext context)
        {
            // --- Use the same tile+style item lookup as planning checks to keep queue behavior deterministic.
            if (!tile.PayloadCell.HasTile() || !context.RequireItems || context.Items is null) return true;
            int style = tile.PayloadCell.ResolvePlacementStyle();
            return TileExecutor.HasItemForTile((ushort)tile.PayloadCell.Block.Type, style, in context);
        }

        /// <summary>Consumes one item for a successful vine placement.</summary>
        public override bool Consume(in PasteTile tile, in PasteContext context) =>
            TileOperations.Consume(in tile, in context);

        /// <summary>Returns candidate inventory items that can place this payload tile.</summary>
        public override IReadOnlyList<int>? GetItem(in PasteTile tile)
        {
            // --- Resolve tile-item mappings from the interaction map using payload tile type + style.
            if (!tile.PayloadCell.HasTile()) return null;
            int style = tile.PayloadCell.ResolvePlacementStyle();
            return InteractionMapProvider.GetTileItemIds((ushort)tile.PayloadCell.Block.Type, style);
        }

        /// <summary>Clears one vine segment per pass to preserve deterministic ordering.</summary>
        public override LayerResult Clear(TileBatch batch, in PasteContext context)
        {
            // --- Clear in the opposite direction of placement to avoid unsupported leftovers during multi-pass processing.
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0)
                return LayerResult.Ok();

            bool clearLowestFirst = IsStandardVine(tiles[0].PayloadCell.Block.Type);
            int index = SelectNextSegmentIndex(tiles, clearLowestFirst);
            PasteTile segment = tiles[index];

            LayerResult result = TileOperations.Clear(segment, in context);
            if (!result.Success)
                return result;

            var remaining = new List<PasteTile>(tiles.Count - 1);
            for (int i = 0; i < tiles.Count; i++)
            {
                if (i != index)
                    remaining.Add(tiles[i]);
            }

            return remaining.Count > 0
                ? LayerResult.OkWithRemaining(batch.WithRemainingTiles(remaining), result.Message, result.OkLabel)
                : result;
        }

        /// <summary>Places one vine segment per pass, respecting vine direction.</summary>
        public override LayerResult Place(TileBatch batch, in PasteContext context)
        {
            // --- Place columns from anchor outward: standard vines top-down, reverse vines bottom-up.
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0)
                return LayerResult.Ok();

            bool placeTopDown = IsStandardVine(tiles[0].PayloadCell.Block.Type);
            int index = SelectNextSegmentIndex(tiles, placeTopDown);
            PasteTile segment = tiles[index];
            string message = LayerLogFormat.FormatTile(segment.PayloadTile);
            string okLabel = LayerLogFormat.PlacedLabel(in context);

            LayerResult result = TileOperations.Place([segment], in context);
            if (!result.Success)
            {
                if (result.FailReason == LayerFailReason.InvalidPlacement && context.RetryOnInvalidPlacement)
                    return LayerResult.OkWithRemaining(batch, message, okLabel);
                return result;
            }

            if (!context.IsSweepCheat && !Consume(in segment, in context))
                return LayerResult.Fail(LayerFailReason.MissingItems, message);

            var remaining = new List<PasteTile>(tiles.Count - 1);
            for (int i = 0; i < tiles.Count; i++)
            {
                if (i != index)
                    remaining.Add(tiles[i]);
            }

            return remaining.Count > 0
                ? LayerResult.OkWithRemaining(batch.WithRemainingTiles(remaining), result.Message ?? message, result.OkLabel ?? okLabel)
                : result;
        }

        /// <summary>Returns the next segment index by relative Y order.</summary>
        private static int SelectNextSegmentIndex(IReadOnlyList<PasteTile> pending, bool selectSmallestY)
        {
            // --- Use relative Y ordering so each retry continues from a stable next segment.
            int selected = 0;
            for (int i = 1; i < pending.Count; i++)
            {
                int candidateY = pending[i].Relative.Y;
                int selectedY = pending[selected].Relative.Y;
                if (selectSmallestY ? candidateY < selectedY : candidateY > selectedY)
                    selected = i;
            }

            return selected;
        }

        /// <summary>Returns true when this custom layer owns the given tile type.</summary>
        public override bool IsCustom(ushort tileType) => IsVineTileType(tileType);
    }
}
