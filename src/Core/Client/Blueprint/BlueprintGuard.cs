using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.UI;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Classifies included blueprint cells for capture/paste safeguards and writes <see cref="Blueprint.BlueprintCell.Protected"/>.
    /// </summary>
    internal static class BlueprintGuard
    {
        private const int LargeMaskVolumeThreshold = 50_000;
        private static readonly Color GuardMaskColor = new(0, 0, 0, 220);

        /// <summary>Classifies one included cell without mutating the grid.</summary>
        public static BlueprintGuardFailure ClassifyIncludedCell(Player player, Blueprint.BlueprintCell cell) =>
            ClassifyWorldTile(player, cell.World.X, cell.World.Y);

        /// <summary>Classifies a world tile coordinate for guard policy.</summary>
        public static BlueprintGuardFailure ClassifyWorldTile(Player player, int worldX, int worldY)
        {
            if (!WorldGen.InWorld(worldX, worldY))
                return BlueprintGuardFailure.OutOfWorld;

            ArchitectBlueprintConfig config = ModContent.GetInstance<ArchitectBlueprintConfig>();
            if (config.RestrictCaptureAndPasteToRevealedTiles && !IsRevealed(worldX, worldY))
                return BlueprintGuardFailure.Unrevealed;

            if (config.LimitCaptureAndPasteToPlayerReach && !IsWithinReach(player, worldX, worldY, config.MaxReachDistanceTiles))
                return BlueprintGuardFailure.OutOfRange;

            return BlueprintGuardFailure.None;
        }

        /// <summary>Recomputes <see cref="Blueprint.BlueprintCell.Protected"/> on included cells; returns the first guard failure found.</summary>
        public static BlueprintGuardFailure RefreshProtection(Player player, Blueprint blueprint)
        {
            if (player is null || blueprint is null || !blueprint.IsDefined || blueprint.Tiles is null)
                return BlueprintGuardFailure.None;

            BlueprintGuardFailure firstFailure = BlueprintGuardFailure.None;
            foreach (Blueprint.BlueprintCell cell in blueprint.Tiles)
            {
                if (!cell.Included)
                    continue;

                BlueprintGuardFailure failure = ClassifyIncludedCell(player, cell);
                cell.Protected = failure != BlueprintGuardFailure.None;
                if (firstFailure == BlueprintGuardFailure.None && cell.Protected)
                    firstFailure = failure;
            }

            return firstFailure;
        }

        /// <summary>Refreshes protection and returns whether capture/paste is allowed.</summary>
        public static bool TryValidateIncludedTiles(Player player, Blueprint blueprint, out BlueprintGuardFailure failure)
        {
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

            for (int x = 0; x < execution.GridWidth; x++)
            {
                for (int y = 0; y < execution.GridHeight; y++)
                {
                    if (!execution.Included[x, y])
                        continue;

                    int wx = execution.TargetArea.X + x;
                    int wy = execution.TargetArea.Y + y;
                    BlueprintGuardFailure cellFailure = ClassifyWorldTile(player, wx, wy);
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

            var points = new List<Point>();
            foreach (Blueprint.BlueprintCell cell in blueprint.Tiles)
            {
                if (!cell.Included || !cell.Protected)
                    continue;

                if (visibleClip is { } clip && !clip.Contains(cell.World))
                    continue;

                points.Add(cell.World);
            }

            return points.Count == 0 ? [] : points.ToArray();
        }

        /// <summary>Refreshes protection and registers the guarded-tile mask overlay for <paramref name="blueprint"/>.</summary>
        public static void RegisterGuardMask(Player player, Blueprint blueprint, string id, string? family)
        {
            if (Main.dedServ || player is null || blueprint is null || !blueprint.IsDefined)
                return;

            UpdateGuardMask(player, blueprint, id, family);
        }

        /// <summary>
        /// Recomputes guard state and guarded-tile masks for the local player's selection and projections.
        /// </summary>
        /// <param name="skipWhenPasteActive">When true, skips refresh while a paste operation is running.</param>
        public static void RefreshClientGuardMasks(Player player, bool skipWhenPasteActive = true)
        {
            if (Main.dedServ || player is null || Main.gameMenu)
                return;

            if (skipWhenPasteActive && HasActivePasteFor(player))
                return;

            BlueprintSystem blueprintSystem = ModContent.GetInstance<BlueprintSystem>();

            Selection? selection = blueprintSystem.GetTopmostSelection();
            if (selection is { IsDefined: true, Owner: var selectionOwner } && selectionOwner == player.whoAmI)
                UpdateGuardMask(player, selection, "guarded", $"selection:{player.whoAmI}");

            foreach (Projection projection in blueprintSystem.GetProjections())
            {
                if (projection is not { IsDefined: true } || projection.Owner != player.whoAmI)
                    continue;

                string key = projection.OverlayInstanceKey.ToString();
                UpdateGuardMask(player, projection, $"{key}:guarded", $"projection:{player.whoAmI}");
            }
        }

        /// <summary>Returns true when <paramref name="player"/> has an in-flight paste operation.</summary>
        public static bool HasActivePasteFor(Player player)
        {
            if (player is null)
                return false;

            return ModContent.GetInstance<PastingSystem>().GetActiveOperationCountForPlayer(player.whoAmI) > 0;
        }

        private static void UpdateGuardMask(Player player, Blueprint blueprint, string id, string? family)
        {
            RefreshProtection(player, blueprint);
            OverlaySystem.Unregister(id, family);

            if (!HasAnyProtectedIncludedCell(blueprint))
                return;

            int includedVolume = PasteLimits.CountIncludedVolume(blueprint);
            if (includedVolume > LargeMaskVolumeThreshold)
            {
                OverlaySystem.RegTileRectFill(id, [blueprint.Area], GuardMaskColor, family);
                return;
            }

            Point[] tiles = CollectProtectedIncludedTiles(blueprint);
            if (tiles.Length > 0)
                OverlaySystem.RegGuardedTileMask(id, tiles, family);
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

        private static bool IsRevealed(int x, int y) =>
            WorldGen.InWorld(x, y) && Main.Map.IsRevealed(x, y);

        private static bool IsWithinReach(Player player, int worldX, int worldY, int maxDistanceTiles)
        {
            Point playerTile = player.Center.ToTileCoordinates();
            int dx = Math.Abs(worldX - playerTile.X);
            int dy = Math.Abs(worldY - playerTile.Y);
            return Math.Max(dx, dy) <= maxDistanceTiles;
        }
    }
}
