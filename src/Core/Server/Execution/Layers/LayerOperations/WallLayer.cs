using System.Collections.Generic;
using ArchitectsStencilMod.Core;
using ArchitectsStencilMod.Core.TileMapping;
using Terraria;
using Terraria.ID;

namespace ArchitectsStencilMod.Core.Layers
{
    internal sealed class WallLayer : Layer
    {
        public static readonly WallLayer Definition = new();

        private WallLayer() : base("Wall", LayerType.Wall) { }

        public override IReadOnlyList<Blueprint.BlueprintCell> Identify(in PasteTile tile, in PasteTileLookup _) =>
            tile.Subject.WallType == WallID.None ? [] : [new Blueprint.BlueprintCell(tile.Relative, tile.World, true)];

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context) =>
            Main.tile[tile.World.X, tile.World.Y].WallType != WallID.None;

        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool HasRequiredItems(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            ushort wallType = (ushort)tile.PayloadCell.Wall.Type;
            if (wallType == WallID.None) return true;
            if (!context.NeedsItems || context.Items is null) return true;
            return InventoryManager.FindItemByWall(context.Items, wallType) is not null;
        }

        public override bool CanConsume(in PasteTile tile, in PasteContext context)
        {
            ushort wallType = (ushort)tile.PayloadCell.Wall.Type;
            if (wallType == WallID.None || !context.NeedsItems || context.Items is null) return true;
            return InventoryManager.FindItemByWall(context.Items, wallType) is not null;
        }

        public override bool Consume(in PasteTile tile, in PasteContext context)
        {
            ushort wallType = (ushort)tile.PayloadCell.Wall.Type;
            if (wallType == WallID.None || !context.NeedsItems || context.Items is null) return true;
            Item? item = InventoryManager.FindItemByWall(context.Items, wallType);
            if (item is null) return false;
            InventoryManager.ConsumeItem(item);
            return true;
        }

        public override bool Match(in PasteTile tile) =>
            tile.Source.WallType == tile.Target.WallType;

        public override IReadOnlyList<int>? GetItem(in PasteTile tile)
        {
            ushort wallType = (ushort)tile.PayloadCell.Wall.Type;
            if (wallType == WallID.None) return null;
            return InteractionMapProvider.GetWallItemIds(wallType);
        }

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            var tile = tiles[0];
            int x = tile.World.X, y = tile.World.Y;
            if (Main.tile[x, y].WallType == WallID.None)
                return LayerResult.Ok(tile.World);

            Main.tile[x, y].WallType = WallID.None;
            LayerSync.SyncTileSquare(x, y);
            return Main.tile[x, y].WallType == WallID.None ? LayerResult.Ok(tile.World) : LayerResult.Fail(tile.World, LayerFailReason.ClearBlocked);
        }

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            var tile = tiles[0];
            int x = tile.World.X, y = tile.World.Y;
            ushort wallType = (ushort)tile.PayloadCell.Wall.Type;
            if (Main.tile[x, y].WallType == wallType) return LayerResult.Ok(tile.World);

            if (context.NeedsItems && context.Items != null && InventoryManager.FindItemByWall(context.Items, wallType) is null)
                return LayerResult.Fail(tile.World, LayerFailReason.MissingItems);

            WorldGen.PlaceWall(x, y, wallType, mute: true);
            if (Main.tile[x, y].WallType != wallType) return LayerResult.Fail(tile.World, LayerFailReason.InvalidPlacement);
            LayerSync.SyncTileSquare(x, y);

            if (!context.CheatSweep && !Consume(in tile, in context))
                return LayerResult.Fail(tile.World, LayerFailReason.MissingItems);

            return LayerResult.Ok(tile.World);
        }
    }
}
