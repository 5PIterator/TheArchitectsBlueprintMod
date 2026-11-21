#if DEBUG
#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.Layers;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Core.TestCases.LayerTests
{
    /// <summary>
    /// Test Fixture chain for layer Identify on a live world cell:
    /// LayerColumnsPlaced → IdentifyReady → (Case ownership asserts).
    /// </summary>
    internal static class LayerIdentifyFixture
    {
        /// <summary>Join at IdentifyReady: build a 1x1 paste lookup around a live world tile.</summary>
        public static bool TryJoinLookup(
            Point world,
            out PasteTile pasteTile,
            out PasteTileLookup lookup,
            out string? error)
        {
            // --- Snapshot the live tile into a payload cell and wrap it as a one-cell paste grid.

            pasteTile = default;
            lookup = null!;
            error = null;

            if (!WorldGen.InWorld(world.X, world.Y))
            {
                error = $"Tile out of world: ({world.X},{world.Y}).";
                return false;
            }

            Tile live = Main.tile[world.X, world.Y];
            if (!live.HasTile)
            {
                error = $"Expected a tile at ({world.X},{world.Y}).";
                return false;
            }

            // Convert the live tile into a payload cell for planning/Identify.
            PayloadCell cell = PayloadCell.FromTile(live, world.X, world.Y);
            if (cell.IsEmpty || !cell.HasTile())
            {
                error = $"PayloadCell.FromTile produced an empty cell at ({world.X},{world.Y}).";
                return false;
            }

            // Resolve interaction and wrap as a single-cell PasteTileLookup.
            InteractionEntry interaction = PastePlanningLookup.ResolveInteraction(cell.ToTile());
            pasteTile = new PasteTile(
                payloadCell: cell,
                interaction: interaction,
                source: live,
                target: live,
                relative: new Point(0, 0),
                world: world);

            var grid = new PasteTile[1, 1];
            grid[0, 0] = pasteTile;
            lookup = new PasteTileLookup(grid);
            return true;
        }

        /// <summary>Runs Identify for a single paste tile with the composed pipeline.</summary>
        public static List<LayerIdentifyResult> IdentifyPayload(
            in PasteTile pasteTile,
            in PasteTileLookup lookup)
        {
            // --- Compose the live pipeline and identify with payload subject + default paste policy.

            LayerPipeline pipeline = LayerSystem.ComposePipeline();
            PastingConfig config = ModContent.GetInstance<PastingConfig>();
            EffectivePastePolicy policy = EffectivePastePolicy.Resolve(config, PasteClientHints.FromClientConfig());
            var context = new PasteContext(
                playerIndex: Main.myPlayer,
                inventoryLookup: null,
                policy: policy,
                lookup: lookup,
                effectSession: null,
                interactionBudget: null,
                payload: null);
            return LayerSystem.Identify(in pasteTile, in lookup, pipeline, in context, config, PasteSubject.Payload);
        }

        /// <summary>
        /// Observes Frame/Plant ownership for a subject tile (never Block).
        /// Setup-only failure strings are for the Case to report.
        /// </summary>
        public static bool TryObserveAnchoredOrPlantOwnership(
            Point world,
            string label,
            out string? failure)
        {
            // --- Check direct classifiers first, then confirm Identify does not claim Block.

            failure = null;
            Tile live = Main.tile[world.X, world.Y];
            if (!live.HasTile)
            {
                failure = $"{label}: no tile at ({world.X},{world.Y}).";
                return false;
            }

            bool isFrame = FrameLayer.IsFrameTile(live);
            bool isPlant = PlantLayer.Definition.IsCustom(live.TileType);
            bool isBlock = BlockLayer.IsBlock(live);

            // Block claim on an anchored/plant subject is the regression we guard against.
            if (isBlock)
            {
                failure =
                    $"{label}: type {live.TileType} classified as Block "
                    + $"(frame={isFrame}, plant={isPlant}); expected Frame or Plant.";
                return false;
            }

            // Require at least one of Frame or Plant classifiers.
            if (!isFrame && !isPlant)
            {
                failure =
                    $"{label}: type {live.TileType} claimed by neither Frame nor Plant "
                    + $"(block={isBlock}); expected Frame or Plant.";
                return false;
            }

            if (!TryJoinLookup(world, out PasteTile pasteTile, out PasteTileLookup lookup, out string? lookupError))
            {
                failure = $"{label}: {lookupError}";
                return false;
            }

            List<LayerIdentifyResult> identified = IdentifyPayload(in pasteTile, in lookup);
            for (int i = 0; i < identified.Count; i++)
            {
                Layer layer = identified[i].Layer;
                if (layer.LayerType == LayerType.Block && layer.Id == "Block")
                {
                    failure =
                        $"{label}: Identify claimed Block for type {live.TileType} "
                        + $"(also frame={isFrame}, plant={isPlant}).";
                    return false;
                }
            }

            // Classifiers already passed; empty Identify is acceptable if Block did not claim.
            return true;
        }

        /// <summary>Observes that stone remains a Block residual (control case).</summary>
        public static bool TryObserveBlockOwnership(Point world, string label, out string? failure)
        {
            // --- Require Block classifier and Identify Block claim for the control stone tile.

            failure = null;
            Tile live = Main.tile[world.X, world.Y];
            if (!live.HasTile || live.TileType != TileID.Stone)
            {
                failure = $"{label}: expected stone at ({world.X},{world.Y}).";
                return false;
            }

            if (!BlockLayer.IsBlock(live))
            {
                failure = $"{label}: stone was not classified as Block.";
                return false;
            }

            if (!TryJoinLookup(world, out PasteTile pasteTile, out PasteTileLookup lookup, out string? lookupError))
            {
                failure = $"{label}: {lookupError}";
                return false;
            }

            List<LayerIdentifyResult> identified = IdentifyPayload(in pasteTile, in lookup);
            if (!HasLayer(identified, LayerType.Block, "Block"))
            {
                failure = $"{label}: Identify did not claim Block for stone.";
                return false;
            }

            return true;
        }

        /// <summary>Observes Frame ownership for ambient rubble / furniture subjects (never Block).</summary>
        public static bool TryObserveFrameOwnership(Point world, string label, out string? failure)
        {
            // --- Require Frame classification and reject Block residual claims.

            failure = null;
            Tile live = Main.tile[world.X, world.Y];
            if (!live.HasTile)
            {
                failure = $"{label}: no tile at ({world.X},{world.Y}).";
                return false;
            }

            if (!FrameLayer.IsFrameTile(live))
            {
                failure =
                    $"{label}: type {live.TileType} was not Frame "
                    + $"(block={BlockLayer.IsBlock(live)}, plant={PlantLayer.Definition.IsCustom(live.TileType)}).";
                return false;
            }

            if (BlockLayer.IsBlock(live))
            {
                failure = $"{label}: type {live.TileType} classified as Block; expected Frame.";
                return false;
            }

            if (!TryJoinLookup(world, out PasteTile pasteTile, out PasteTileLookup lookup, out string? lookupError))
            {
                failure = $"{label}: {lookupError}";
                return false;
            }

            List<LayerIdentifyResult> identified = IdentifyPayload(in pasteTile, in lookup);
            for (int i = 0; i < identified.Count; i++)
            {
                Layer layer = identified[i].Layer;
                if (layer.LayerType == LayerType.Block && layer.Id == "Block")
                {
                    failure = $"{label}: Identify claimed Block for type {live.TileType}.";
                    return false;
                }
            }

            return true;
        }

        /// <summary>Observes zero-count .itr availability for a plant style and its visual item id.</summary>
        public static bool TryObserveZeroCountPlantChannel(
            ushort tileType,
            int style,
            int expectedVisualItemId,
            out string? failure) =>
            TryObserveZeroCountTileChannel(tileType, style, expectedVisualItemId, out failure);

        /// <summary>Observes zero-count .itr availability for a tile style and its visual item id.</summary>
        public static bool TryObserveZeroCountTileChannel(
            int tileType,
            int style,
            int expectedVisualItemId,
            out string? failure)
        {
            // --- Prepare the paste map, then require Available with requireInventory=false and a mapped visual.

            failure = null;
            ushort typed = (ushort)tileType;
            ItrStorage.PrepareForPaste(new HashSet<int> { typed }, wallIds: null);
            try
            {
                var lookup = new InventoryLookup();
                TileItemAvailability availability = lookup.GetTileItemAvailability(
                    typed,
                    style,
                    requireInventory: false);
                if (availability != TileItemAvailability.Available)
                {
                    failure =
                        $"Zero-count channel for tile {tileType} style {style} returned {availability}; "
                        + "expected Available (pin/compile InteractionMap if missing).";
                    return false;
                }

                if (!InteractionSelection.TrySelectFiniteFirst(
                        InteractionMapProvider.EnumerateTileInteractions(typed, style, baseId: null),
                        isMet: null,
                        out InteractionEntry? entry,
                        out _,
                        out _,
                        out _)
                    || entry is null)
                {
                    failure = $"Zero-count channel: no finite-first .itr row for tile {tileType} style {style}.";
                    return false;
                }

                int visual = entry.GetVisualItemId();
                if (visual != expectedVisualItemId)
                {
                    failure =
                        $"Zero-count channel visual item {visual} for tile {tileType} style {style}; "
                        + $"expected {expectedVisualItemId}.";
                    return false;
                }

                return true;
            }
            finally
            {
                ItrStorage.Release();
            }
        }

        /// <summary>Observes that a rubble row places via Fake Echo and consumes the expected ammo item.</summary>
        public static bool TryObserveEchoAmmoRow(
            int tileType,
            int style,
            int expectedFakeTileType,
            int expectedAmmoItemId,
            out string? failure)
        {
            // --- Load the tile's interactions and require FakeId + AmmoId on the finite-first row.

            failure = null;
            ushort typed = (ushort)tileType;
            ItrStorage.PrepareForPaste(new HashSet<int> { typed }, wallIds: null);
            try
            {
                if (!InteractionSelection.TrySelectFiniteFirst(
                        InteractionMapProvider.EnumerateTileInteractions(typed, style, baseId: null),
                        isMet: null,
                        out InteractionEntry? entry,
                        out _,
                        out _,
                        out _)
                    || entry is null)
                {
                    failure = $"Echo ammo row: no finite-first .itr row for tile {tileType} style {style}.";
                    return false;
                }

                if (entry.FakeId != expectedFakeTileType)
                {
                    failure =
                        $"Echo ammo row FakeId={entry.FakeId?.ToString() ?? "null"} for tile {tileType} style {style}; "
                        + $"expected {expectedFakeTileType}.";
                    return false;
                }

                if (entry.AmmoId != expectedAmmoItemId)
                {
                    failure =
                        $"Echo ammo row AmmoId={entry.AmmoId?.ToString() ?? "null"} for tile {tileType} style {style}; "
                        + $"expected {expectedAmmoItemId}.";
                    return false;
                }

                return true;
            }
            finally
            {
                ItrStorage.Release();
            }
        }

        /// <summary>Observes that an unmapped tile id fails as UnresolvedInteraction under zero-count rules.</summary>
        public static bool TryObserveUnresolvedWhenUnmapped(out string? failure)
        {
            // --- Use a vanilla tile with no interaction rows so .itr authority rejects placement.

            failure = null;
            const ushort unmappedTile = TileID.Dirt;
            ItrStorage.PrepareForPaste(new HashSet<int> { unmappedTile }, wallIds: null);
            try
            {
                // Dirt is normally mapped; pick a style that has no row, or clear Current to simulate miss.
                // Prefer a synthetic check: availability with Current null should be UnresolvedInteraction.
                InteractionMapProvider.Current = null;
                var lookup = new InventoryLookup();
                TileItemAvailability availability = lookup.GetTileItemAvailability(
                    unmappedTile,
                    style: 0,
                    requireInventory: false);
                if (availability != TileItemAvailability.UnresolvedInteraction)
                {
                    failure =
                        $"Unmapped zero-count channel returned {availability}; expected UnresolvedInteraction.";
                    return false;
                }

                return true;
            }
            finally
            {
                ItrStorage.Release();
            }
        }

        private static bool HasLayer(List<LayerIdentifyResult> identified, LayerType type, string id)
        {
            // --- Scan Identify results for a matching layer type and id.
            for (int i = 0; i < identified.Count; i++)
            {
                Layer layer = identified[i].Layer;
                if (layer.LayerType == type && layer.Id == id)
                    return true;
            }

            return false;
        }
    }
}
#endif

