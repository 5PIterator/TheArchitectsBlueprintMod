using System.Collections.Generic;
using ArchitectsStencilMod.Core;
using ArchitectsStencilMod.Core.TileMapping;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace ArchitectsStencilMod.Core.Layers
{
    /// <summary>Simple solid tiles: one cell per <see cref="Identify"/>; multi-tile objects and frames belong to other layers.</summary>
    internal sealed class BlockLayer : Layer
    {
        public static readonly BlockLayer Definition = new();

        private BlockLayer() : base("Block", LayerType.Block) { }

        public override bool Match(in PasteTile tile) => TileComparison.MatchTileType(in tile);

        /// <summary>At most one <see cref="Blueprint.BoundedTile"/> (this relative only). No multi-cell frame expansion.</summary>
        public override IReadOnlyList<Blueprint.BoundedTile> Identify(in PasteTile tile, in PasteTileLookup _) =>
            IsBlock(tile.IdentifyFace) ? [new Blueprint.BoundedTile(tile.Relative, selected: true)] : [];

        /// <summary>Classifies <paramref name="view"/> (source payload or target world per <see cref="PasteTile.SubjectType"/>).</summary>
        private static bool IsBlock(Tile view)
        {
            if (!view.HasTile) return false;
            if (view.TileType >= TileLoader.TileCount) return false;
            if (TileID.Sets.Falling[view.TileType]) return false;
            if (FrameOperations.HasTileAnchor(view)) return false;
            if (FrameOperations.HasWallAnchor(view)) return false;
            if (FrameOperations.HasOtherAnchor(view)) return false;
            return !CustomLayer.TileTypeIsClaimedByAttributedLayer(view.TileType);
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context) =>
            Main.tile[tile.World.X, tile.World.Y].HasTile;

        public override bool CanPlace(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            if (!context.NeedsItems || context.Slots is null || context.CheatSweep) return true;
            if (!IsBlock(tile.Blueprint)) return true;
            return CanPlaceSingleTile(in tile, in context);
        }

        public override bool CanConsume(in PasteTile tile, in PasteContext context)
        {
            if (!tile.Blueprint.HasTile || !context.NeedsItems || context.Slots is null) return true;

            ushort tileType = tile.Blueprint.TileType;
            int style = TileObjectData.GetTileStyle(tile.Blueprint);
            ushort placedType = (ushort)Main.tile[tile.World.X, tile.World.Y].TileType;
            int lookupStyle = placedType == tileType ? (style >= 0 ? style : 0) : 0;
            return context.Slots.FindTileSlot(placedType, lookupStyle) >= 0;
        }

        public override IReadOnlyList<int>? GetItem(in PasteTile tile)
        {
            Tile blueprint = tile.Blueprint;
            if (!blueprint.HasTile) return null;
            int style = TileObjectData.GetTileStyle(blueprint);
            return InteractionMapProvider.GetTileItemIds(blueprint.TileType, style);
        }

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context) =>
            FrameOperations.Clear(tiles, in context);

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context) =>
            TileOperations.Place(tiles, in context);

        public override bool Consume(in PasteTile tile, in PasteContext context) =>
            TileOperations.Consume(in tile, in context);

        private static bool CanPlaceSingleTile(in PasteTile tile, in PasteContext context)
        {
            int x = tile.World.X, y = tile.World.Y;
            if (!tile.Blueprint.HasTile) return true;

            ushort tileType = tile.Blueprint.TileType;
            if (tileType >= TileLoader.TileCount) return false;

            int style = TileObjectData.GetTileStyle(tile.Blueprint);
            int? baseTileId = InteractionMapProvider.GetRequiredBaseTile(tileType, style);
            Tile dest = Main.tile[x, y];

            if (dest.HasTile && dest.TileType == tileType) return true;

            if (baseTileId.HasValue)
            {
                ushort baseType = (ushort)baseTileId.Value;
                if (!dest.HasTile || dest.TileType != baseType)
                {
                    if (context.Slots.FindTileSlot(baseType, 0) < 0) return false;
                    return context.Slots.FindTileSlot(tileType, style) >= 0;
                }
            }
            else if (dest.HasTile && !context.CheatSweep)
            {
                return false;
            }

            return context.Slots.FindTileSlot(tileType, style) >= 0;
        }
    }
}
