using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.UI;

using TheArchitectsBlueprintMod.Core.Architect;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Classifies included blueprint cells for capture/paste safeguards and writes <see cref="Blueprint.BlueprintCell.Protected"/>.
    /// </summary>
    internal static class BlueprintGuard
    {
        /// <summary>Blueprint area (W×H) above which guard visuals use cheap rects instead of per-tile points.</summary>
        private const int LargeAreaThreshold = 4_000;

        /// <summary>Max grid cells examined for budgeted protection refresh in one frame.</summary>
        private const int GuardScanCellsPerFrame = 6_000;

        private static readonly Color GuardMaskColor = new(0, 0, 0, 220);
        private static readonly List<Rectangle> ScratchReachRects = new(4);
        private static readonly List<Point> ScratchProtectedTiles = new();

        private static GuardScanBatch? _activeScan;

        /// <summary>One blueprint queued for a budgeted protection refresh and overlay rebuild.</summary>
        private sealed class GuardScanTarget(Blueprint blueprint, string id, string? family)
        {
            public Blueprint Blueprint { get; } = blueprint;
            public string Id { get; } = id;
            public string? Family { get; } = family;
        }

        /// <summary>In-flight multi-blueprint protection scan that consumes a per-frame cell budget.</summary>
        private sealed class GuardScanBatch(int ownerWhoAmI, List<GuardScanTarget> targets)
        {
            public int OwnerWhoAmI { get; } = ownerWhoAmI;
            public List<GuardScanTarget> Targets { get; } = targets;
            public int TargetIndex { get; set; }
            public int CellX { get; set; }
            public int CellY { get; set; }
        }

        /// <summary>Classifies one included cell without mutating the grid.</summary>
        public static BlueprintGuardFailure ClassifyIncludedCell(Player player, Blueprint.BlueprintCell cell) =>
            ClassifyWorldTile(player, cell.World.X, cell.World.Y);

        /// <summary>Classifies a world tile coordinate for guard policy.</summary>
        public static BlueprintGuardFailure ClassifyWorldTile(Player player, int worldX, int worldY)
        {
            // --- Apply OOW, then optional reveal/reach checks with one config read.

            if (!WorldGen.InWorld(worldX, worldY))
                return BlueprintGuardFailure.OutOfWorld;

            ArchitectBlueprintConfig config = ModContent.GetInstance<ArchitectBlueprintConfig>();
            return ClassifyWorldTile(player, worldX, worldY, config, player.Center.ToTileCoordinates());
        }

        /// <summary>Recomputes <see cref="Blueprint.BlueprintCell.Protected"/> on included cells; returns the first guard failure found.</summary>
        public static BlueprintGuardFailure RefreshProtection(Player player, Blueprint blueprint) =>
            RefreshProtection(player, blueprint, out _);

        /// <summary>Synchronously recomputes protection flags for every included cell.</summary>
        public static BlueprintGuardFailure RefreshProtection(Player player, Blueprint blueprint, out bool anyChanged)
        {
            // --- Full walk with cached config/anchor; used by capture/paste validation and forced overlay rebuilds.

            anyChanged = false;
            if (player is null || blueprint is null || !blueprint.IsDefined || blueprint.Tiles is null)
                return BlueprintGuardFailure.None;

            ArchitectBlueprintConfig config = ModContent.GetInstance<ArchitectBlueprintConfig>();
            Point playerTile = player.Center.ToTileCoordinates();
            BlueprintGuardFailure firstFailure = BlueprintGuardFailure.None;

            foreach (Blueprint.BlueprintCell cell in blueprint.Tiles)
            {
                if (!cell.Included)
                    continue;

                BlueprintGuardFailure failure = ClassifyWorldTile(player, cell.World.X, cell.World.Y, config, playerTile);
                bool protectedNow = failure != BlueprintGuardFailure.None;
                if (cell.Protected != protectedNow)
                {
                    cell.Protected = protectedNow;
                    anyChanged = true;
                }

                if (firstFailure == BlueprintGuardFailure.None && protectedNow)
                    firstFailure = failure;
            }

            return firstFailure;
        }

        /// <summary>Refreshes protection and returns whether capture/paste is allowed.</summary>
        public static bool TryValidateIncludedTiles(Player player, Blueprint blueprint, out BlueprintGuardFailure failure)
        {
            // Validation must be complete and current; abandon any budgeted scan first.
            CancelClientGuardScan();
            failure = RefreshProtection(player, blueprint);
            return failure == BlueprintGuardFailure.None;
        }

        /// <summary>Validates included cells on a server paste execution snapshot.</summary>
        public static bool TryValidateIncludedTiles(Player player, PasteExecutionContext execution, out BlueprintGuardFailure failure)
        {
            failure = BlueprintGuardFailure.None;
            if (player is null || execution is null || !execution.IsDefined)
                return true;

            if (execution.ClientProjection is Projection projection)
                return TryValidateIncludedTiles(player, projection, out failure);

            ArchitectBlueprintConfig config = ModContent.GetInstance<ArchitectBlueprintConfig>();
            Point playerTile = player.Center.ToTileCoordinates();

            for (int x = 0; x < execution.GridWidth; x++)
            {
                for (int y = 0; y < execution.GridHeight; y++)
                {
                    if (!execution.Included[x, y])
                        continue;

                    int wx = execution.TargetArea.X + x;
                    int wy = execution.TargetArea.Y + y;
                    BlueprintGuardFailure cellFailure = ClassifyWorldTile(player, wx, wy, config, playerTile);
                    if (cellFailure == BlueprintGuardFailure.None)
                        continue;

                    failure = cellFailure;
                    return false;
                }
            }

            return true;
        }

        /// <summary>Returns world positions of included cells marked protected (call after <see cref="RefreshProtection"/>).</summary>
        public static Point[] CollectProtectedIncludedTiles(Blueprint blueprint, Rectangle? visibleClip = null)
        {
            if (blueprint is null || !blueprint.IsDefined || blueprint.Tiles is null)
                return [];

            ScratchProtectedTiles.Clear();
            foreach (Blueprint.BlueprintCell cell in blueprint.Tiles)
            {
                if (!cell.Included || !cell.Protected)
                    continue;

                if (visibleClip is { } clip && !clip.Contains(cell.World))
                    continue;

                ScratchProtectedTiles.Add(cell.World);
            }

            return ScratchProtectedTiles.Count == 0 ? [] : ScratchProtectedTiles.ToArray();
        }

        /// <summary>Refreshes protection and registers the guarded-tile mask overlay for <paramref name="blueprint"/>.</summary>
        public static void RegisterGuardMask(Player player, Blueprint blueprint, string id, string? family)
        {
            if (Main.dedServ || player is null || blueprint is null || !blueprint.IsDefined)
                return;

            // Structural overlay rebuild needs a finished classification for this blueprint.
            CancelClientGuardScan();
            RefreshProtection(player, blueprint);
            ApplyGuardOverlay(player, blueprint, id, family);
        }

        /// <summary>
        /// Starts a budgeted guard refresh for the local player's selection and projections.
        /// Cancels any in-progress scan so the newest request wins.
        /// </summary>
        /// <param name="skipWhenPasteActive">When true, skips refresh while a paste operation is running.</param>
        public static void RefreshClientGuardMasks(Player player, bool skipWhenPasteActive = true)
        {
            // --- Drop the old batch, queue every owned blueprint, spend this frame's cell budget immediately.

            if (Main.dedServ || player is null || Main.gameMenu)
                return;

            if (skipWhenPasteActive && HasActivePasteFor(player))
            {
                CancelClientGuardScan();
                return;
            }

            if (!NeedsDynamicGuardRefresh())
            {
                CancelClientGuardScan();
                return;
            }

            var targets = new List<GuardScanTarget>();
            BlueprintSystem blueprintSystem = ModContent.GetInstance<BlueprintSystem>();

            Selection? selection = blueprintSystem.GetTopmostSelection();
            if (selection is { IsDefined: true, Owner: var selectionOwner } && selectionOwner == player.whoAmI)
                targets.Add(new GuardScanTarget(selection, "guarded", $"selection:{player.whoAmI}"));

            foreach (Projection projection in blueprintSystem.GetProjections())
            {
                if (projection is not { IsDefined: true } || projection.Owner != player.whoAmI)
                    continue;

                string key = projection.OverlayInstanceKey.ToString();
                targets.Add(new GuardScanTarget(projection, $"{key}:guarded", $"projection:{player.whoAmI}"));
            }

            // Newest request replaces any unfinished scan.
            CancelClientGuardScan();

            if (targets.Count == 0)
                return;

            // Instant coarse reach veil while the budgeted cell walk catches up.
            ArchitectBlueprintConfig config = ModContent.GetInstance<ArchitectBlueprintConfig>();
            if (config.LimitCaptureAndPasteToPlayerReach)
            {
                Point playerTile = player.Center.ToTileCoordinates();
                foreach (GuardScanTarget target in targets)
                    ApplyReachOnlyOverlay(target, playerTile, config.MaxReachDistanceTiles);
            }

            _activeScan = new GuardScanBatch(player.whoAmI, targets);
        }

        /// <summary>Continues the active budgeted guard scan using a fresh per-frame cell budget.</summary>
        public static void TickClientGuardScan(Player player)
        {
            // --- Resume the current batch when present; abort while pasting or when the owner is gone.

            if (_activeScan is null || player is null || Main.dedServ || Main.gameMenu)
                return;

            if (player.whoAmI != _activeScan.OwnerWhoAmI || HasActivePasteFor(player))
            {
                CancelClientGuardScan();
                return;
            }

            ProcessActiveGuardScan(player);
        }

        /// <summary>True when a budgeted protection scan is still unfinished.</summary>
        public static bool HasActiveClientGuardScan => _activeScan is not null;

        /// <summary>Abandons any unfinished budgeted guard scan.</summary>
        public static void CancelClientGuardScan() => _activeScan = null;

        /// <summary>True when any owned defined blueprint's area exceeds the large-area threshold.</summary>
        public static bool HasLargeOwnedBlueprint(Player player)
        {
            if (player is null)
                return false;

            BlueprintSystem blueprintSystem = ModContent.GetInstance<BlueprintSystem>();
            Selection? selection = blueprintSystem.GetTopmostSelection();
            if (selection is { IsDefined: true } && selection.Owner == player.whoAmI && IsLargeArea(selection.Area))
                return true;

            foreach (Projection projection in blueprintSystem.GetProjections())
            {
                if (projection is { IsDefined: true } && projection.Owner == player.whoAmI && IsLargeArea(projection.Area))
                    return true;
            }

            return false;
        }

        /// <summary>True when reach or reveal policy can change without blueprint geometry changing.</summary>
        public static bool NeedsDynamicGuardRefresh()
        {
            ArchitectBlueprintConfig config = ModContent.GetInstance<ArchitectBlueprintConfig>();
            return config.LimitCaptureAndPasteToPlayerReach || config.RestrictCaptureAndPasteToRevealedTiles;
        }

        /// <summary>True when player-tile moves should trigger an immediate guard refresh.</summary>
        public static bool NeedsMoveDrivenGuardRefresh()
        {
            ArchitectBlueprintConfig config = ModContent.GetInstance<ArchitectBlueprintConfig>();
            return config.LimitCaptureAndPasteToPlayerReach;
        }

        /// <summary>True when map exploration can change guard visuals without the player moving.</summary>
        public static bool NeedsRevealDrivenGuardRefresh()
        {
            ArchitectBlueprintConfig config = ModContent.GetInstance<ArchitectBlueprintConfig>();
            return config.RestrictCaptureAndPasteToRevealedTiles;
        }

        /// <summary>Returns true when <paramref name="player"/> has an in-flight paste operation.</summary>
        public static bool HasActivePasteFor(Player player)
        {
            if (player is null)
                return false;

            return ModContent.GetInstance<PastingSystem>().GetActiveOperationCountForPlayer(player.whoAmI) > 0;
        }

        /// <summary>Maps a guard failure to a localized capture-block chat key.</summary>
        public static string GetCaptureBlockLocalizationKey(BlueprintGuardFailure failure) =>
            failure switch
            {
                BlueprintGuardFailure.OutOfWorld => "Mods.TheArchitectsBlueprintMod.UI.Capture.BlockedOutOfWorld",
                BlueprintGuardFailure.Unrevealed => "Mods.TheArchitectsBlueprintMod.UI.Capture.BlockedUnrevealed",
                BlueprintGuardFailure.OutOfRange => "Mods.TheArchitectsBlueprintMod.UI.Capture.BlockedOutOfRange",
                _ => "Mods.TheArchitectsBlueprintMod.UI.Capture.BlockedOutOfWorld",
            };

        /// <summary>Maps a guard failure to a paste reject reason.</summary>
        public static PasteRejectReason ToPasteRejectReason(BlueprintGuardFailure failure) =>
            failure switch
            {
                BlueprintGuardFailure.OutOfWorld => PasteRejectReason.OutOfWorldTiles,
                BlueprintGuardFailure.Unrevealed => PasteRejectReason.UnrevealedTiles,
                BlueprintGuardFailure.OutOfRange => PasteRejectReason.OutOfRange,
                _ => PasteRejectReason.InvalidProjection,
            };

        /// <summary>Prints a localized capture-block chat line on the local client.</summary>
        public static void NotifyCaptureBlocked(BlueprintGuardFailure failure)
        {
            if (Main.netMode == NetmodeID.Server || failure == BlueprintGuardFailure.None)
                return;

            Main.NewText(Language.GetTextValue(GetCaptureBlockLocalizationKey(failure)), Color.OrangeRed);
        }

        /// <summary>Spends this frame's cell budget advancing the active scan batch.</summary>
        private static void ProcessActiveGuardScan(Player player)
        {
            // --- Classify grid cells until the budget empties or every target finishes.

            GuardScanBatch? batch = _activeScan;
            if (batch is null)
                return;

            ArchitectBlueprintConfig config = ModContent.GetInstance<ArchitectBlueprintConfig>();
            Point playerTile = player.Center.ToTileCoordinates();
            int budget = GuardScanCellsPerFrame;

            while (budget > 0 && batch.TargetIndex < batch.Targets.Count)
            {
                GuardScanTarget target = batch.Targets[batch.TargetIndex];
                Blueprint blueprint = target.Blueprint;

                // Drop targets that vanished or were redefined out from under the scan.
                if (blueprint is null || !blueprint.IsDefined || blueprint.Tiles is null || blueprint.Owner != player.whoAmI)
                {
                    batch.TargetIndex++;
                    batch.CellX = 0;
                    batch.CellY = 0;
                    continue;
                }

                int width = blueprint.Area.Width;
                int height = blueprint.Area.Height;

                while (budget > 0 && batch.CellY < height)
                {
                    // Examine one grid cell toward the frame budget.
                    Blueprint.BlueprintCell cell = blueprint.Tiles[batch.CellX, batch.CellY];
                    if (cell.Included)
                    {
                        BlueprintGuardFailure failure = ClassifyWorldTile(
                            player,
                            cell.World.X,
                            cell.World.Y,
                            config,
                            playerTile);
                        cell.Protected = failure != BlueprintGuardFailure.None;
                    }

                    budget--;
                    batch.CellX++;
                    if (batch.CellX < width)
                        continue;

                    batch.CellX = 0;
                    batch.CellY++;
                }

                // Blueprint finished: publish its overlay, then advance.
                if (batch.CellY < height)
                    break;

                ApplyGuardOverlay(player, blueprint, target.Id, target.Family);
                batch.TargetIndex++;
                batch.CellX = 0;
                batch.CellY = 0;
            }

            if (batch.TargetIndex >= batch.Targets.Count)
                CancelClientGuardScan();
        }

        /// <summary>Registers the precise or coarse guard overlay for a fully scanned blueprint.</summary>
        private static void ApplyGuardOverlay(Player player, Blueprint blueprint, string id, string? family)
        {
            // --- Large areas use rect fills; small areas use view-clipped protected points.

            OverlaySystem.Unregister(id, family);

            if (!HasAnyProtectedIncludedCell(blueprint))
                return;

            if (IsLargeArea(blueprint.Area))
            {
                ArchitectBlueprintConfig config = ModContent.GetInstance<ArchitectBlueprintConfig>();
                ScratchReachRects.Clear();

                if (config.LimitCaptureAndPasteToPlayerReach)
                {
                    AppendOutOfReachRects(
                        blueprint.Area,
                        player.Center.ToTileCoordinates(),
                        config.MaxReachDistanceTiles,
                        ScratchReachRects);
                }

                // Cover remaining protected causes (unrevealed / OOW) with a full-area veil when reach left nothing.
                if (ScratchReachRects.Count == 0)
                    ScratchReachRects.Add(blueprint.Area);

                OverlaySystem.RegTileRectFill(id, ScratchReachRects.ToArray(), GuardMaskColor, family);
                return;
            }

            Point[] tiles = CollectProtectedIncludedTiles(blueprint, GetVisibleTileClip());
            if (tiles.Length > 0)
                OverlaySystem.RegGuardedTileMask(id, tiles, family);
        }

        /// <summary>Publishes an immediate geometric out-of-reach veil before the cell scan finishes.</summary>
        private static void ApplyReachOnlyOverlay(GuardScanTarget target, Point playerTile, int maxReach)
        {
            // --- Replace the prior mask with reach strips so walking feels responsive during a long scan.

            OverlaySystem.Unregister(target.Id, target.Family);
            ScratchReachRects.Clear();
            AppendOutOfReachRects(target.Blueprint.Area, playerTile, maxReach, ScratchReachRects);
            if (ScratchReachRects.Count > 0)
                OverlaySystem.RegTileRectFill(target.Id, ScratchReachRects.ToArray(), GuardMaskColor, target.Family);
        }

        /// <summary>True when blueprint area product exceeds the per-tile mask budget.</summary>
        private static bool IsLargeArea(Rectangle area) =>
            area.Width > 0 && area.Height > 0 && (long)area.Width * area.Height > LargeAreaThreshold;

        /// <summary>Adds up to four rectangles covering <paramref name="area"/> outside the Chebyshev reach square.</summary>
        private static void AppendOutOfReachRects(Rectangle area, Point playerTile, int maxReach, List<Rectangle> dest)
        {
            // --- Subtract the inclusive reach square from the blueprint area as axis-aligned strips.

            int reachLeft = playerTile.X - maxReach;
            int reachTop = playerTile.Y - maxReach;
            int reachRight = playerTile.X + maxReach + 1;
            int reachBottom = playerTile.Y + maxReach + 1;

            var reach = Rectangle.Intersect(
                area,
                new Rectangle(reachLeft, reachTop, reachRight - reachLeft, reachBottom - reachTop));

            if (reach.IsEmpty)
            {
                dest.Add(area);
                return;
            }

            if (reach.Y > area.Y)
                dest.Add(new Rectangle(area.X, area.Y, area.Width, reach.Y - area.Y));

            int bottomY = reach.Bottom;
            if (bottomY < area.Bottom)
                dest.Add(new Rectangle(area.X, bottomY, area.Width, area.Bottom - bottomY));

            if (reach.X > area.X)
                dest.Add(new Rectangle(area.X, reach.Y, reach.X - area.X, reach.Height));

            int rightX = reach.Right;
            if (rightX < area.Right)
                dest.Add(new Rectangle(rightX, reach.Y, area.Right - rightX, reach.Height));
        }

        /// <summary>Returns the currently visible tile rectangle (world screen or fullscreen map).</summary>
        private static Rectangle GetVisibleTileClip()
        {
            // --- Convert the screen (or map view) to tile space with a small margin.

            const int margin = 2;

            if (ArchitectMapTransform.IsMapViewActive
                && ArchitectMapTransform.TryScreenToTile(Vector2.Zero, out Point topLeft)
                && ArchitectMapTransform.TryScreenToTile(new Vector2(Main.screenWidth, Main.screenHeight), out Point bottomRight))
            {
                int left = Math.Min(topLeft.X, bottomRight.X) - margin;
                int top = Math.Min(topLeft.Y, bottomRight.Y) - margin;
                int right = Math.Max(topLeft.X, bottomRight.X) + margin;
                int bottom = Math.Max(topLeft.Y, bottomRight.Y) + margin;
                return new Rectangle(left, top, Math.Max(0, right - left + 1), Math.Max(0, bottom - top + 1));
            }

            int worldLeft = (int)(Main.screenPosition.X / 16f) - margin;
            int worldTop = (int)(Main.screenPosition.Y / 16f) - margin;
            int worldRight = (int)((Main.screenPosition.X + Main.screenWidth) / 16f) + margin;
            int worldBottom = (int)((Main.screenPosition.Y + Main.screenHeight) / 16f) + margin;
            return new Rectangle(
                worldLeft,
                worldTop,
                Math.Max(0, worldRight - worldLeft + 1),
                Math.Max(0, worldBottom - worldTop + 1));
        }

        private static bool HasAnyProtectedIncludedCell(Blueprint blueprint)
        {
            if (blueprint.Tiles is null)
                return false;

            foreach (Blueprint.BlueprintCell cell in blueprint.Tiles)
            {
                if (cell.Included && cell.Protected)
                    return true;
            }

            return false;
        }

        private static BlueprintGuardFailure ClassifyWorldTile(
            Player player,
            int worldX,
            int worldY,
            ArchitectBlueprintConfig config,
            Point playerTile)
        {
            if (!WorldGen.InWorld(worldX, worldY))
                return BlueprintGuardFailure.OutOfWorld;

            if (config.RestrictCaptureAndPasteToRevealedTiles && !IsRevealed(worldX, worldY))
                return BlueprintGuardFailure.Unrevealed;

            if (config.LimitCaptureAndPasteToPlayerReach && !IsWithinReach(playerTile, worldX, worldY, config.MaxReachDistanceTiles))
                return BlueprintGuardFailure.OutOfRange;

            return BlueprintGuardFailure.None;
        }

        private static bool IsRevealed(int x, int y)
        {
            // --- Exploration map is client-only; dedicated servers have no Main.Map instance.

            if (!WorldGen.InWorld(x, y))
                return false;

            // Server paste validation still enforces OOW/reach; reveal is gated on the requesting client.
            if (Main.Map is null)
                return true;

            return Main.Map.IsRevealed(x, y);
        }

        private static bool IsWithinReach(Point playerTile, int worldX, int worldY, int maxDistanceTiles)
        {
            int dx = Math.Abs(worldX - playerTile.X);
            int dy = Math.Abs(worldY - playerTile.Y);
            return Math.Max(dx, dy) <= maxDistanceTiles;
        }
    }
}
