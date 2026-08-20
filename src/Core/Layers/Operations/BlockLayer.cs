using System.Collections.Generic;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.Debug;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    /// <summary>Residual tiles not claimed by Frame/Falling/Custom; dependency-anchored and multi-style objects belong to Frame.</summary>
    internal sealed class BlockLayer : Layer
    {
        public static readonly BlockLayer Definition = new();

        private BlockLayer() : base("Block", LayerType.Block) { }

        public override bool Match(in PasteTile tile) => TileComparison.MatchForPaste(in tile);

        /// <summary>At most one <see cref="Blueprint.BlueprintCell"/> (this relative only). No multi-cell frame expansion.</summary>
        public override LayerResult Identify(in PasteTile tile, in PasteTileLookup _, PasteSubject identifySubject)
        {
            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            if (!IsBlock(subjectTile))
                return LayerResult.IdentifyNone();

            return LayerResult.Identified(
                TileBatch.FromCells([new Blueprint.BlueprintCell(tile.Relative, tile.World, true)]),
                LayerLogFormat.FormatTile(subjectTile));
        }

        /// <summary>Classifies <paramref name="subjectTile"/> (source vs target is chosen by <see cref="PasteSubject"/> in <see cref="Layer.Identify"/>).</summary>
        private static bool IsBlock(Tile subjectTile)
        {
            if (!subjectTile.HasTile) return false;
            if (!TileLoaderSupport.IsKnownTileType(subjectTile.TileType)) return false;
            if (TileID.Sets.Falling[subjectTile.TileType]) return false;
            if (FrameLayer.IsFrameTile(subjectTile)) return false;
            if (IsLeafBlock(subjectTile.TileType)) return true;
            return !CustomLayer.TileTypeIsClaimedByAttributedLayer(subjectTile.TileType);
        }

        internal static bool IsLeafBlock(ushort tileType) =>
            TileLoaderSupport.IsKnownTileType(tileType) && TileID.Sets.Leaves[tileType];

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context) =>
            Main.tile[tile.World.X, tile.World.Y].HasTile;

        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            if (!IsBlock(tile.PayloadTile)) return true;
            return PlacementValidSingle(in tile, in context);
        }

        public override bool HasRequiredItems(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            if (!context.RequireItems || context.Items is null || context.IsSweepCheat) return true;
            if (!IsBlock(tile.PayloadTile)) return true;
            return HasRequiredItemsSingle(in tile, in context);
        }

        public override bool CanConsume(in PasteTile tile, in PasteContext context)
        {
            if (!tile.PayloadCell.HasTile()) return true;
            if (!context.RequireItems || context.Items is null) return true;
            ushort tileType = tile.PayloadCell.Block.Type;
            int style = tile.PayloadCell.ResolvePlacementStyle();
            return TileInteractionChecks.HasTilePlacementItems(
                tileType,
                style,
                tile.World.X,
                tile.World.Y,
                in context);
        }

        public override bool Consume(in PasteTile tile, in PasteContext context) =>
            TileOperations.Consume(in tile, in context);

        public override IReadOnlyList<int>? GetItem(in PasteTile tile)
        {
            Tile intent = tile.PayloadTile;
            if (!intent.HasTile) return null;
            int style = tile.PayloadCell.ResolvePlacementStyle();
            return TileInteractionChecks.GetToolItemIds(intent.TileType, style, tile.World.X, tile.World.Y);
        }

        public override LayerResult Clear(TileBatch batch, in PasteContext context)
        {
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0)
                return LayerResult.Ok();

            var tile = tiles[0];
            int x = tile.World.X, y = tile.World.Y;
            bool hadTile = Main.tile[x, y].HasTile;
            string? message = hadTile ? LayerLogFormat.FormatTile(Main.tile[x, y]) : null;

            LayerResult res = FrameOperations.Clear(tiles, in context);
            if (!res.Success)
                return LayerResult.Fail(res.FailReason, message);

            return hadTile ? LayerResult.Ok(message, LayerLogFormat.ClearedLabel) : LayerResult.Ok();
        }

        public override LayerResult Place(TileBatch batch, in PasteContext context)
        {
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (TABLog.IsDebugEnabled && tiles.Count > 0)
            {
                var samplePasteTile = tiles[0];
                TABLog.Debug(
                    $"BlockLayer.Place batch={tiles.Count} "+
                    $"rel=({samplePasteTile.Relative.X},{samplePasteTile.Relative.Y}) "+
                    $"world=({samplePasteTile.World.X},{samplePasteTile.World.Y}) "+
                    $"payload[hasTile={samplePasteTile.PayloadCell.HasTile()} type={samplePasteTile.PayloadCell.Block.Type} "+
                    $"frame=({samplePasteTile.PayloadCell.Block.FrameX},{samplePasteTile.PayloadCell.Block.FrameY})]",
                    "Paste");
            }

            if (tiles.Count == 0)
                return LayerResult.Ok();

            var origin = FrameOperations.GetPlacementOriginTile(tiles);
            bool hasPayload = origin.PayloadCell.HasTile();
            string? message = hasPayload ? LayerLogFormat.FormatTile(origin.PayloadTile) : null;

            var res = TileOperations.Place(tiles, in context);
            if (!res.Success)
                return LayerResult.Fail(res.FailReason, message);

            if (res.RemainingTiles.Count > 0)
            {
                TABLog.Debug(
                    $"BlockLayer.Place partial world=({origin.World.X},{origin.World.Y}) "
                    + $"remaining={res.RemainingTiles.Count}",
                    "Paste");
                return LayerResult.OkWithRemaining(batch.WithRemainingTiles(res.RemainingTiles), message, LayerLogFormat.PlacedLabel(in context));
            }

            if (!context.IsSweepCheat && !Consume(in origin, in context))
                return LayerResult.Fail(LayerFailReason.MissingItems, message);

            return hasPayload ? LayerResult.Ok(message, LayerLogFormat.PlacedLabel(in context)) : LayerResult.Ok();
        }

        private static bool PlacementValidSingle(in PasteTile tile, in PasteContext context)
        {
            int x = tile.World.X, y = tile.World.Y;
            Tile intent = tile.PayloadTile;
            if (!intent.HasTile) return true;

            ushort tileType = intent.TileType;
            if (!TileLoaderSupport.IsKnownTileType(tileType)) return false;

            int style = tile.PayloadCell.ResolvePlacementStyle();
            int alternate = tile.PayloadCell.Detail.Alternate;
            Tile targetTile = Main.tile[x, y];

            if (targetTile.HasTile && targetTile.TileType == tileType) return true;

            int? baseTileId = InteractionMapProvider.GetRequiredBaseTile(tileType, style);
            if (baseTileId.HasValue)
            {
                ushort baseType = (ushort)baseTileId.Value;
                if (targetTile.HasTile && targetTile.TileType != baseType)
                    return false;
                return true;
            }

            if (!TileLoaderSupport.IsReplaceAllowedAt(x, y, tileType, in context)) return false;

            if (targetTile.HasTile && !context.IsSweepCheat)
                return false;

            return context.IsSweepCheat || TileLoader.CanPlace(x, y, tileType);
        }

        private static bool HasRequiredItemsSingle(in PasteTile tile, in PasteContext context)
        {
            Tile intent = tile.PayloadTile;
            if (!intent.HasTile) return true;

            ushort tileType = intent.TileType;
            if (!TileLoaderSupport.IsKnownTileType(tileType)) return true;

            int style = tile.PayloadCell.ResolvePlacementStyle();
            Tile targetTile = Main.tile[tile.World.X, tile.World.Y];
            if (targetTile.HasTile && targetTile.TileType == tileType) return true;

            int? baseTileId = InteractionMapProvider.GetRequiredBaseTile(tileType, style);
            if (!baseTileId.HasValue && targetTile.HasTile && !context.IsSweepCheat)
                return false;

            return TileInteractionChecks.HasTilePlacementItems(
                tileType,
                style,
                tile.World.X,
                tile.World.Y,
                in context);
        }
    }
}
