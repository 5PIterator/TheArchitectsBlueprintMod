using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Matches payload entity FullNames to live world entities and applies opaque TagCompound data.
    /// Place/Clear are called from EntityLayer; Sweep applies every payload sidecar after tiles are blitted.
    /// </summary>
    internal static class WorldEntityOps
    {
        private const string LogCategory = "Paste";

        /// <summary>
        /// Wipes and applies payload entity data when the expected host exists at the payload origin.
        /// </summary>
        public static bool TryPasteEntity(Point world, in PayloadEntity entity, in PasteContext ctx)
        {
            // --- Prefer the host Frame/Block just created; recreate the table entry if framing left tiles without one.

            if (!PayloadEntityHandlers.TryFind(entity.FullName, world.X, world.Y)
                && !TryEnsureHostForPlacedTiles(world, in entity))
            {
                TABLog.Warn(
                    $"EntityPaste ({world.X},{world.Y}) rel=({entity.Relative.X},{entity.Relative.Y}) "
                    + $"n={entity.FullName} result=missing",
                    LogCategory);
                return false;
            }

            return TryReplaceEntityData(world, in entity, "EntityPaste");
        }

        /// <summary>
        /// Recreates a missing chest/sign/TE table entry when live tiles already occupy the payload origin.
        /// </summary>
        private static bool TryEnsureHostForPlacedTiles(Point world, in PayloadEntity entity)
        {
            // --- Only when Frame left tiles; skip empty air so intentionally unplaced frames stay missing.

            if (!WorldGen.InWorld(world.X, world.Y))
                return false;

            Tile tile = Main.tile[world.X, world.Y];
            if (!tile.HasTile)
                return false;

            if (!PayloadEntityHandlers.TryEnsureHost(entity.FullName, world.X, world.Y))
            {
                TABLog.Warn(
                    $"EntityPaste ({world.X},{world.Y}) rel=({entity.Relative.X},{entity.Relative.Y}) "
                    + $"n={entity.FullName} result=ensureFail",
                    LogCategory);
                return false;
            }

            if (!PayloadEntityHandlers.TryFind(entity.FullName, world.X, world.Y))
                return false;

            TABLog.Info(
                $"EntityPaste ({world.X},{world.Y}) rel=({entity.Relative.X},{entity.Relative.Y}) "
                + $"n={entity.FullName} result=ensured",
                LogCategory);
            return true;
        }

        /// <summary>Wipes live chest, sign, or TileEntity contents at the payload origin.</summary>
        public static bool TryClearEntity(Point world, in PasteContext ctx)
        {
            // --- Direct wipe at the entity sidecar coordinates.

            if (!PayloadEntityHandlers.TryGetLiveFullName(world.X, world.Y, out string liveName))
            {
                TABLog.Debug($"EntityClear ({world.X},{world.Y}) result=missing", LogCategory);
                return true;
            }

            try
            {
                if (!PayloadEntityHandlers.TryWipe(liveName, world.X, world.Y))
                {
                    TABLog.Warn(
                        $"EntityClear ({world.X},{world.Y}) n={liveName} result=fail",
                        LogCategory);
                    return false;
                }

                TABLog.Info($"EntityClear ({world.X},{world.Y}) n={liveName} result=ok", LogCategory);
                return true;
            }
            catch (Exception ex)
            {
                TABLog.Error($"EntityClear ({world.X},{world.Y}) n={liveName} result=fail", ex, LogCategory);
                return false;
            }
        }

        /// <summary>Removes every chest, sign, and TileEntity in the sweep destination before tiles are blitted.</summary>
        public static void PurgeHostsInSweepArea(Rectangle worldBounds)
        {
            // --- Clear host tables against the live destination tiles, not the post-blit replacement frames.

            PayloadEntityHandlers.RemoveAllHostsInBounds(worldBounds);
        }

        /// <summary>
        /// After Sweep finishes blitting tiles, creates and applies every payload entity sidecar.
        /// </summary>
        public static void ApplyAllPayloadEntitiesForSweep(in PasteContext ctx)
        {
            // --- Payload origins recreate hosts on the blitted tiles, then wipe/apply sidecar data.

            if (!ctx.IsSweep)
                return;

            Payload? payload = ctx.Payload;
            if (payload is null || !payload.IsDefined || payload.Entities.Count == 0)
            {
                TABLog.Info("SweepEntity done total=0 applied=0 failed=0", LogCategory);
                return;
            }

            if (ctx.Lookup is null)
            {
                TABLog.Warn("SweepEntity apply skipped: paste lookup missing", LogCategory);
                return;
            }

            IReadOnlyList<PayloadEntity> entities = payload.Entities;
            int applied = 0;
            int failed = 0;
            for (int i = 0; i < entities.Count; i++)
            {
                PayloadEntity entity = entities[i];
                if (!ctx.Lookup.TryGetPasteTile(entity.Relative, out PasteTile pasteTile))
                {
                    TABLog.Warn(
                        $"SweepEntity skip rel=({entity.Relative.X},{entity.Relative.Y}) n={entity.FullName} result=noLookup",
                        LogCategory);
                    failed++;
                    continue;
                }

                if (TryPasteEntityBlind(pasteTile.World, in entity))
                    applied++;
                else
                    failed++;
            }

            TABLog.Info(
                $"SweepEntity done total={entities.Count} applied={applied} failed={failed}",
                LogCategory);
        }

        /// <summary>Creates the host at the payload origin and applies sidecar data.</summary>
        private static bool TryPasteEntityBlind(Point world, in PayloadEntity entity)
        {
            // --- Sweep recreates hosts at exact payload coordinates, then wipe/apply there.

            if (!PayloadEntityHandlers.TryCreateHostForSweep(entity.FullName, world.X, world.Y))
            {
                TABLog.Warn(
                    $"SweepEntity ({world.X},{world.Y}) rel=({entity.Relative.X},{entity.Relative.Y}) "
                    + $"n={entity.FullName} result=createFail",
                    LogCategory);
                return false;
            }

            return TryReplaceEntityData(world, in entity, "SweepEntity");
        }

        /// <summary>Wipes live contents then writes payload data at exact coordinates.</summary>
        private static bool TryReplaceEntityData(Point origin, in PayloadEntity entity, string logPrefix)
        {
            // --- Wipe first so unlisted chest slots and leftover TE items do not survive the override.

            try
            {
                if (!PayloadEntityHandlers.TryWipe(entity.FullName, origin.X, origin.Y))
                {
                    TABLog.Warn(
                        $"{logPrefix} ({origin.X},{origin.Y}) rel=({entity.Relative.X},{entity.Relative.Y}) "
                        + $"n={entity.FullName} result=wipeFail",
                        LogCategory);
                    return false;
                }

                if (!entity.HasData)
                {
                    TABLog.Info(
                        $"{logPrefix} ({origin.X},{origin.Y}) rel=({entity.Relative.X},{entity.Relative.Y}) "
                        + $"n={entity.FullName} result=ok detail=empty",
                        LogCategory);
                    return true;
                }

                if (!PayloadEntityHandlers.TryApply(entity.FullName, origin.X, origin.Y, entity.Data!))
                {
                    TABLog.Warn(
                        $"{logPrefix} ({origin.X},{origin.Y}) rel=({entity.Relative.X},{entity.Relative.Y}) "
                        + $"n={entity.FullName} result=fail",
                        LogCategory);
                    return false;
                }

                TABLog.Info(
                    $"{logPrefix} ({origin.X},{origin.Y}) rel=({entity.Relative.X},{entity.Relative.Y}) "
                    + $"n={entity.FullName} result=ok",
                    LogCategory);
                return true;
            }
            catch (Exception ex)
            {
                TABLog.Error(
                    $"{logPrefix} ({origin.X},{origin.Y}) rel=({entity.Relative.X},{entity.Relative.Y}) "
                    + $"n={entity.FullName} result=fail",
                    ex,
                    LogCategory);
                return false;
            }
        }
    }
}
