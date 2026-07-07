using System.Collections.Generic;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Utils;
using Terraria;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    /// <summary>
    /// Wire colors and actuators on existing tiles. Mechanism tiles are placed by Block or anchored layers;
    /// this layer applies <see cref="PayloadCell.Wire"/> via <see cref="WorldGen"/> after those layers run.
    /// </summary>
    internal sealed class WiringLayer : Layer
    {
        public static readonly WiringLayer Definition = new();

        private WiringLayer() : base("Wiring", LayerType.Wiring) { }

        public override LayerResult Identify(in PasteTile tile, in PasteTileLookup _, PasteSubject identifySubject)
        {
            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            bool hasWiring = subjectTile.RedWire || subjectTile.BlueWire || subjectTile.GreenWire || subjectTile.YellowWire || subjectTile.HasActuator;
            if (!hasWiring)
                return LayerResult.IdentifyNone();

            return LayerResult.Identified(
                TileBatch.FromCells([new Blueprint.BlueprintCell(tile.Relative, tile.World, true)]),
                LayerLogFormat.FormatWireState(subjectTile));
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            Tile t = tile.Target;
            return t.RedWire || t.BlueWire || t.GreenWire || t.YellowWire || t.HasActuator;
        }

        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool HasRequiredItems(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool CanConsume(in PasteTile tile, in PasteContext context) => true;

        public override bool Consume(in PasteTile tile, in PasteContext context) => true;

        public override bool Match(in PasteTile tile)
        {
            if (TileComparison.IsSourceCutIntent(in tile))
                return tile.Source.RedWire == tile.Target.RedWire
                    && tile.Source.BlueWire == tile.Target.BlueWire
                    && tile.Source.GreenWire == tile.Target.GreenWire
                    && tile.Source.YellowWire == tile.Target.YellowWire
                    && tile.Source.HasActuator == tile.Target.HasActuator;
            Tile intent = tile.PayloadTile;
            return intent.RedWire == tile.Target.RedWire
                && intent.BlueWire == tile.Target.BlueWire
                && intent.GreenWire == tile.Target.GreenWire
                && intent.YellowWire == tile.Target.YellowWire
                && intent.HasActuator == tile.Target.HasActuator;
        }

        public override IReadOnlyList<int>? GetItem(in PasteTile tile) => null;

        public override LayerResult Clear(TileBatch batch, in PasteContext context)
        {
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0)
                return LayerResult.Ok();

            var tile = tiles[0];
            int x = tile.World.X, y = tile.World.Y;
            string message = LayerLogFormat.FormatWireState(Main.tile[x, y]);

            if (IsWireClear(Main.tile[x, y]))
                return LayerResult.Ok();

            bool changed = ClearWiringAt(x, y, in context);
            if (changed)
                LayerSync.SyncTileSquare(x, y);

            return IsWireClear(Main.tile[x, y])
                ? (changed ? LayerResult.Ok(message, LayerLogFormat.ClearedLabel) : LayerResult.Ok())
                : LayerResult.Fail(LayerFailReason.ClearBlocked, message);
        }

        public override LayerResult Place(TileBatch batch, in PasteContext context)
        {
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0)
                return LayerResult.Ok();

            var tile = tiles[0];
            int x = tile.World.X, y = tile.World.Y;
            var intent = tile.PayloadCell.Wire;
            string message = LayerLogFormat.FormatWireState(in intent);

            if (WireMatches(in intent, Main.tile[x, y]))
                return LayerResult.Ok();

            bool changed = ApplyWireIntent(x, y, in intent);
            if (changed)
                LayerSync.SyncTileSquare(x, y);

            return WireMatches(in intent, Main.tile[x, y])
                ? LayerResult.Ok(message, LayerLogFormat.WiredLabel)
                : LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
        }

        /// <summary>Removes every wire color and any actuator at the world cell.</summary>
        private static bool ClearWiringAt(int x, int y, in PasteContext context)
        {
            bool changed = false;

            if (Main.tile[x, y].RedWire) { TileExecutor.EnqueueClearWire(x, y, in context); changed = true; }
            if (Main.tile[x, y].BlueWire) { TileExecutor.EnqueueClearWire2(x, y, in context); changed = true; }
            if (Main.tile[x, y].GreenWire) { TileExecutor.EnqueueClearWire3(x, y, in context); changed = true; }
            if (Main.tile[x, y].YellowWire) { TileExecutor.EnqueueClearWire4(x, y, in context); changed = true; }
            if (Main.tile[x, y].HasActuator) { TileExecutor.EnqueueClearActuator(x, y, in context); changed = true; }

            return changed;
        }

        /// <summary>Reconciles live wire/actuator state to payload intent using WorldGen place/kill APIs.</summary>
        private static bool ApplyWireIntent(int x, int y, in PayloadCell.WireData intent)
        {
            bool changed = false;

            if (Main.tile[x, y].RedWire != intent.RedWire)
            {
                if (intent.RedWire) WorldGen.PlaceWire(x, y);
                else SuppressTileDrop.KillWire(x, y);
                changed = true;
            }

            if (Main.tile[x, y].BlueWire != intent.BlueWire)
            {
                if (intent.BlueWire) WorldGen.PlaceWire2(x, y);
                else SuppressTileDrop.KillWire2(x, y);
                changed = true;
            }

            if (Main.tile[x, y].GreenWire != intent.GreenWire)
            {
                if (intent.GreenWire) WorldGen.PlaceWire3(x, y);
                else SuppressTileDrop.KillWire3(x, y);
                changed = true;
            }

            if (Main.tile[x, y].YellowWire != intent.YellowWire)
            {
                if (intent.YellowWire) WorldGen.PlaceWire4(x, y);
                else SuppressTileDrop.KillWire4(x, y);
                changed = true;
            }

            if (Main.tile[x, y].HasActuator != intent.HasActuator)
            {
                if (intent.HasActuator) WorldGen.PlaceActuator(x, y);
                else SuppressTileDrop.KillActuator(x, y);
                changed = true;
            }

            return changed;
        }

        private static bool WireMatches(in PayloadCell.WireData intent, in Tile live) =>
            live.RedWire == intent.RedWire
            && live.BlueWire == intent.BlueWire
            && live.GreenWire == intent.GreenWire
            && live.YellowWire == intent.YellowWire
            && live.HasActuator == intent.HasActuator;

        private static bool IsWireClear(in Tile live) =>
            !live.RedWire && !live.BlueWire && !live.GreenWire && !live.YellowWire && !live.HasActuator;
    }
}
