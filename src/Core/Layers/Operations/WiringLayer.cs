using System.Collections.Generic;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;
using Terraria;
using Terraria.ID;

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

        public override bool HasRequiredItems(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            if (!context.RequireItems || context.Items is null || context.IsSweepCheat) return true;
            PayloadCell.WireData intent = tile.PayloadCell.Wire;
            return HasRequiredWireItems(in intent, tile.Target, in context);
        }

        public override bool CanConsume(in PasteTile tile, in PasteContext context)
        {
            if (!context.RequireItems || context.Items is null) return true;
            PayloadCell.WireData intent = tile.PayloadCell.Wire;
            return HasRequiredWireItems(in intent, tile.Target, in context);
        }

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

        public override IReadOnlyList<int>? GetItem(in PasteTile tile)
        {
            List<int> ids = [];
            PayloadCell.WireData intent = tile.PayloadCell.Wire;
            CollectToolItemIds(in intent, tile.Target, ids);
            return ids.Count > 0 ? ids : null;
        }

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

            bool changed = false;
            if (!ClearWiringAt(x, y, in context, ref changed))
                return LayerResult.Fail(LayerFailReason.ClearBlocked, message);

            return changed
                ? LayerResult.Ok(message, LayerLogFormat.ClearedLabel)
                : LayerResult.Ok();
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

            bool changed = false;
            if (!ApplyWireIntent(x, y, in intent, in context, ref changed))
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);

            return changed
                ? LayerResult.Ok(message, LayerLogFormat.WiredLabel)
                : LayerResult.Ok();
        }

        private static bool HasRequiredWireItems(in PayloadCell.WireData intent, in Tile target, in PasteContext context)
        {
            if (intent.RedWire && !target.RedWire && !HasWireKindItems(0, in context)) return false;
            if (intent.BlueWire && !target.BlueWire && !HasWireKindItems(2, in context)) return false;
            if (intent.GreenWire && !target.GreenWire && !HasWireKindItems(1, in context)) return false;
            if (intent.YellowWire && !target.YellowWire && !HasWireKindItems(3, in context)) return false;
            if (intent.HasActuator && !target.HasActuator && !HasWireKindItems(4, in context)) return false;
            return true;
        }

        private static void CollectToolItemIds(in PayloadCell.WireData intent, in Tile target, List<int> ids)
        {
            TryAddToolId(0, intent.RedWire && !target.RedWire, ids);
            TryAddToolId(2, intent.BlueWire && !target.BlueWire, ids);
            TryAddToolId(1, intent.GreenWire && !target.GreenWire, ids);
            TryAddToolId(3, intent.YellowWire && !target.YellowWire, ids);
            TryAddToolId(4, intent.HasActuator && !target.HasActuator, ids);
        }

        private static void TryAddToolId(int wireKind, bool needed, List<int> ids)
        {
            if (!needed)
                return;

            // Collect every mapped tool that can place this wire kind.
            IReadOnlyList<int> toolIds = InteractionMapProvider.GetWireItemIds(wireKind);
            for (int i = 0; i < toolIds.Count; i++)
            {
                int toolId = toolIds[i];
                if (toolId > 0 && !ids.Contains(toolId))
                    ids.Add(toolId);
            }
        }

        private static bool HasWireKindItems(int wireKind, in PasteContext context)
        {
            // --- Any mapped tool for this color counts (Grand Design, Wire Kite, single wrench, etc.).
            if (!context.RequireItems || context.IsSweepCheat)
                return true;

            if (context.InventoryLookup is not null)
            {
                TileItemAvailability availability = context.InventoryLookup.GetWireItemAvailability(
                    wireKind,
                    context.InteractionBudget);
                if (availability == TileItemAvailability.Available)
                    return true;
                if (availability != TileItemAvailability.UnresolvedInteraction)
                    return false;
            }

            Item[]? items = context.Items;
            if (items is null)
                return false;

            int fallbackItem = wireKind == 4 ? ItemID.Actuator : ItemID.Wire;
            for (int i = 0; i < items.Length; i++)
            {
                Item item = items[i];
                if (item.type == fallbackItem && item.stack > 0)
                    return true;
            }

            return false;
        }

        /// <summary>Queues removal of every wire color and actuator at the world cell.</summary>
        private static bool ClearWiringAt(int x, int y, in PasteContext context, ref bool changed)
        {
            if (Main.tile[x, y].RedWire)
            {
                changed = true;
                if (!TileExecutor.EnqueueClearWire(x, y, in context))
                    return false;
            }

            if (Main.tile[x, y].BlueWire)
            {
                changed = true;
                if (!TileExecutor.EnqueueClearWire2(x, y, in context))
                    return false;
            }

            if (Main.tile[x, y].GreenWire)
            {
                changed = true;
                if (!TileExecutor.EnqueueClearWire3(x, y, in context))
                    return false;
            }

            if (Main.tile[x, y].YellowWire)
            {
                changed = true;
                if (!TileExecutor.EnqueueClearWire4(x, y, in context))
                    return false;
            }

            if (Main.tile[x, y].HasActuator)
            {
                changed = true;
                if (!TileExecutor.EnqueueClearActuator(x, y, in context))
                    return false;
            }

            return true;
        }

        /// <summary>Queues wire/actuator reconciliation to payload intent through TileExecutor.</summary>
        private static bool ApplyWireIntent(int x, int y, in PayloadCell.WireData intent, in PasteContext context, ref bool changed)
        {
            bool consumeItem = context.RequireItems && !context.IsSweepCheat;

            if (Main.tile[x, y].RedWire != intent.RedWire)
            {
                changed = true;
                bool queued = intent.RedWire
                    ? TileExecutor.EnqueuePlaceWire(x, y, in context, consumeItem)
                    : TileExecutor.EnqueueClearWire(x, y, in context);
                if (!queued)
                    return false;
            }

            if (Main.tile[x, y].BlueWire != intent.BlueWire)
            {
                changed = true;
                bool queued = intent.BlueWire
                    ? TileExecutor.EnqueuePlaceWire2(x, y, in context, consumeItem)
                    : TileExecutor.EnqueueClearWire2(x, y, in context);
                if (!queued)
                    return false;
            }

            if (Main.tile[x, y].GreenWire != intent.GreenWire)
            {
                changed = true;
                bool queued = intent.GreenWire
                    ? TileExecutor.EnqueuePlaceWire3(x, y, in context, consumeItem)
                    : TileExecutor.EnqueueClearWire3(x, y, in context);
                if (!queued)
                    return false;
            }

            if (Main.tile[x, y].YellowWire != intent.YellowWire)
            {
                changed = true;
                bool queued = intent.YellowWire
                    ? TileExecutor.EnqueuePlaceWire4(x, y, in context, consumeItem)
                    : TileExecutor.EnqueueClearWire4(x, y, in context);
                if (!queued)
                    return false;
            }

            if (Main.tile[x, y].HasActuator != intent.HasActuator)
            {
                bool queued = intent.HasActuator
                    ? TileExecutor.EnqueuePlaceActuator(x, y, in context, consumeItem)
                    : TileExecutor.EnqueueClearActuator(x, y, in context);
                changed = true;
                if (!queued)
                    return false;
            }

            return true;
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
