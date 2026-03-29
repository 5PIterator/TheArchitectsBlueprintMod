using System.Collections.Generic;
using ArchitectsStencilMod.Core;
using ArchitectsStencilMod.Core.TileMapping;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace ArchitectsStencilMod.Core.Layers
{
    internal sealed class WallLayer : Layer
    {
        public static readonly WallLayer Definition = new();

        private WallLayer() : base("Wall", LayerType.Wall) { }

        public override IReadOnlyList<Blueprint.BoundedTile> Identify(Point rel, in Tile tileRef, in PasteLookupQuery lookup) =>
            tileRef.WallType == WallID.None ? NoIdentify() : [new Blueprint.BoundedTile(rel, selected: true)];

        public override bool CanPlace(IReadOnlyList<Blueprint.BoundedTile> identifiedTiles, in PasteLookupQuery lookup, in PasteContext context) =>
            LayerPlacementPlan.TryReserveForPlacement(this, identifiedTiles, in lookup, in context, reserveEachTileInStack: false);

        public override bool Match(in PasteTile tile) =>
            tile.Source.WallType == tile.Target.WallType;

        public override IReadOnlyList<int>? GetItem(in Tile sourceTile)
        {
            if (sourceTile.WallType == WallID.None) return null;
            return InteractionMapProvider.GetWallItemIds(sourceTile.WallType);
        }

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            var tile = tiles[0];
            int x = tile.X, y = tile.Y;
            if (Main.tile[x, y].WallType == WallID.None)
                return LayerResult.Ok(tile.World);

            Main.tile[x, y].WallType = WallID.None;
            LayerSync.SyncTileSquare(x, y);
            return Main.tile[x, y].WallType == WallID.None ? LayerResult.Ok(tile.World) : LayerResult.Fail(tile.World);
        }

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            var tile = tiles[0];
            int x = tile.X, y = tile.Y;
            ushort wallType = tile.Source.WallType;
            if (Main.tile[x, y].WallType == wallType) return LayerResult.Ok(tile.World);

            if (context.NeedsItems && context.Slots != null && context.Slots.FindWallSlot(wallType) < 0)
                return LayerResult.Fail(tile.World);

            WorldGen.PlaceWall(x, y, wallType, mute: true);
            if (Main.tile[x, y].WallType != wallType) return LayerResult.Fail(tile.World);
            LayerSync.SyncTileSquare(x, y);
            return LayerResult.Ok(tile.World);
        }

        public override bool Consume(in PasteTile tile, in PasteContext context)
        {
            ushort wallType = tile.Source.WallType;
            if (wallType == WallID.None || !context.NeedsItems || context.Slots is null) return true;
            int slotHandle = context.Slots.FindWallSlot(wallType);
            if (slotHandle < 0) return false;
            context.Slots.ConsumeAt(slotHandle);
            return true;
        }
    }
}
