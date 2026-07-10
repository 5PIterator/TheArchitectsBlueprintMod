using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Content.Projectiles;
using TheArchitectsBlueprintMod.Core.Layers;
using TheArchitectsBlueprintMod.Core.Phases;
using TheArchitectsBlueprintMod.Debug;
using TheArchitectsBlueprintMod.Utils;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Central entry point for queued paste tile/wall/wire execution.
    /// </summary>
    internal static class TileExecutor
    {
        private const int DetailActuatedFlag = 1 << 0;
        private const int DetailHalfBlockFlag = 1 << 1;

        private const int PaintTileInvisibleFlag = 1 << 0;
        private const int PaintWallInvisibleFlag = 1 << 1;
        private const int PaintTileFullbrightFlag = 1 << 2;
        private const int PaintWallFullbrightFlag = 1 << 3;
        private const int ArrowSpeedMinTicks = 10;
        private const int ArrowSpeedMaxTicks = 600;
        private const int ArrowManeuverLeadTicks = 6;

        public static bool EnqueuePlaceTile(int x, int y, ushort tileType, int style, in PasteContext ctx, bool forced, bool consumeItem)
        {
            if (!TryGetSession(in ctx, out TileEffectSession session))
                return false;

            PasteProjectileMode? mode = ShouldUsePlaceProjectile(in ctx, consumeItem) ? PasteProjectileMode.Place : null;
            session.Enqueue(TileMutation.New(
                mutationId: session.NextMutationId(),
                operationId: session.OperationId,
                kind: TileMutationKind.PlaceTile,
                world: new Point(x, y),
                type: tileType,
                style: style,
                forced: forced,
                consumeItem: consumeItem,
                projectileMode: mode));
            return true;
        }

        public static bool EnqueuePlaceObject(int x, int y, ushort tileType, int style, int alternate, in PasteContext ctx, bool consumeItem)
        {
            if (!TryGetSession(in ctx, out TileEffectSession session))
                return false;

            PasteProjectileMode? mode = ShouldUsePlaceProjectile(in ctx, consumeItem) ? PasteProjectileMode.Place : null;
            session.Enqueue(TileMutation.New(
                mutationId: session.NextMutationId(),
                operationId: session.OperationId,
                kind: TileMutationKind.PlaceObject,
                world: new Point(x, y),
                type: tileType,
                style: style,
                alternate: alternate,
                consumeItem: consumeItem,
                projectileMode: mode));
            return true;
        }

        public static bool EnqueuePlaceWall(int x, int y, ushort wallType, in PasteContext ctx, bool consumeItem)
        {
            if (!TryGetSession(in ctx, out TileEffectSession session))
                return false;

            PasteProjectileMode? mode = ShouldUsePlaceProjectile(in ctx, consumeItem) ? PasteProjectileMode.Place : null;
            session.Enqueue(TileMutation.New(
                mutationId: session.NextMutationId(),
                operationId: session.OperationId,
                kind: TileMutationKind.PlaceWall,
                world: new Point(x, y),
                type: wallType,
                consumeItem: consumeItem,
                projectileMode: mode));
            return true;
        }

        public static bool EnqueuePlaceWire(int x, int y, in PasteContext ctx) =>
            EnqueueSimpleMutation(TileMutationKind.PlaceWireRed, new Point(x, y), in ctx);
        public static bool EnqueuePlaceWire2(int x, int y, in PasteContext ctx) =>
            EnqueueSimpleMutation(TileMutationKind.PlaceWireBlue, new Point(x, y), in ctx);
        public static bool EnqueuePlaceWire3(int x, int y, in PasteContext ctx) =>
            EnqueueSimpleMutation(TileMutationKind.PlaceWireGreen, new Point(x, y), in ctx);
        public static bool EnqueuePlaceWire4(int x, int y, in PasteContext ctx) =>
            EnqueueSimpleMutation(TileMutationKind.PlaceWireYellow, new Point(x, y), in ctx);
        public static bool EnqueuePlaceActuator(int x, int y, in PasteContext ctx) =>
            EnqueueSimpleMutation(TileMutationKind.PlaceActuator, new Point(x, y), in ctx);

        public static bool EnqueueClearTile(int x, int y, in PasteContext ctx)
        {
            return EnqueueClear(
                kind: TileMutationKind.ClearTile,
                world: new Point(x, y),
                itemType: ResolveStoreItemForClearTile(x, y, ctx.ItemDropStyle),
                in ctx);
        }

        public static bool EnqueueClearWall(int x, int y, in PasteContext ctx)
        {
            return EnqueueClear(
                kind: TileMutationKind.ClearWall,
                world: new Point(x, y),
                itemType: ResolveStoreItemForClearWall(x, y, ctx.ItemDropStyle),
                in ctx);
        }

        public static bool EnqueueClearWire(int x, int y, in PasteContext ctx) =>
            EnqueueClear(TileMutationKind.ClearWireRed, new Point(x, y), ItemID.Wire, in ctx);
        public static bool EnqueueClearWire2(int x, int y, in PasteContext ctx) =>
            EnqueueClear(TileMutationKind.ClearWireBlue, new Point(x, y), ItemID.Wire, in ctx);
        public static bool EnqueueClearWire3(int x, int y, in PasteContext ctx) =>
            EnqueueClear(TileMutationKind.ClearWireGreen, new Point(x, y), ItemID.Wire, in ctx);
        public static bool EnqueueClearWire4(int x, int y, in PasteContext ctx) =>
            EnqueueClear(TileMutationKind.ClearWireYellow, new Point(x, y), ItemID.Wire, in ctx);
        public static bool EnqueueClearActuator(int x, int y, in PasteContext ctx) =>
            EnqueueClear(TileMutationKind.ClearActuator, new Point(x, y), ItemID.Actuator, in ctx);

        public static bool EnqueueVoidKillTile(int x, int y, in PasteContext ctx)
        {
            if (!TryGetSession(in ctx, out TileEffectSession session))
                return false;

            session.Enqueue(TileMutation.New(
                mutationId: session.NextMutationId(),
                operationId: session.OperationId,
                kind: TileMutationKind.VoidKillTile,
                world: new Point(x, y)));
            return true;
        }

        public static bool EnqueueSweep(in PasteTile tile, in PasteContext ctx)
        {
            if (!TryGetSession(in ctx, out TileEffectSession session))
                return false;

            session.Enqueue(TileMutation.New(
                mutationId: session.NextMutationId(),
                operationId: session.OperationId,
                kind: TileMutationKind.Sweep,
                world: tile.World,
                payload: tile.PayloadCell));
            return true;
        }

        public static bool EnqueueApplyDetailState(
            int x,
            int y,
            bool isActuated,
            bool isHalfBlock,
            SlopeType slope,
            BlockType blockType,
            in PasteContext ctx)
        {
            if (!TryGetSession(in ctx, out TileEffectSession session))
                return false;

            int detailFlags = PackDetailFlags(isActuated, isHalfBlock);
            session.Enqueue(TileMutation.New(
                mutationId: session.NextMutationId(),
                operationId: session.OperationId,
                kind: TileMutationKind.ApplyDetailState,
                world: new Point(x, y),
                type: (ushort)blockType,
                style: (int)slope,
                alternate: detailFlags));
            return true;
        }

        public static bool EnqueueApplyPaintState(
            int x,
            int y,
            byte tileColor,
            byte wallColor,
            bool tileInvisible,
            bool wallInvisible,
            bool tileFullbright,
            bool wallFullbright,
            in PasteContext ctx)
        {
            if (!TryGetSession(in ctx, out TileEffectSession session))
                return false;

            int paintFlags = PackPaintFlags(tileInvisible, wallInvisible, tileFullbright, wallFullbright);
            session.Enqueue(TileMutation.New(
                mutationId: session.NextMutationId(),
                operationId: session.OperationId,
                kind: TileMutationKind.ApplyPaintState,
                world: new Point(x, y),
                type: tileColor,
                style: wallColor,
                alternate: paintFlags));
            return true;
        }

        public static bool EnqueueSetLiquid(int x, int y, int liquidType, byte amount, in PasteContext ctx)
        {
            if (!TryGetSession(in ctx, out TileEffectSession session))
                return false;

            session.Enqueue(TileMutation.New(
                mutationId: session.NextMutationId(),
                operationId: session.OperationId,
                kind: TileMutationKind.SetLiquid,
                world: new Point(x, y),
                type: (ushort)Math.Clamp(liquidType, 0, 255),
                style: amount));
            return true;
        }

        public static bool EnqueueApplyTileFrame(int x, int y, short frameX, short frameY, in PasteContext ctx)
        {
            if (!TryGetSession(in ctx, out TileEffectSession session))
                return false;

            session.Enqueue(TileMutation.New(
                mutationId: session.NextMutationId(),
                operationId: session.OperationId,
                kind: TileMutationKind.ApplyTileFrame,
                world: new Point(x, y),
                style: frameX,
                alternate: frameY));
            return true;
        }

        public static bool EnqueueApplyPayloadBlock(int x, int y, in PayloadCell payload, in PasteContext ctx)
        {
            if (!TryGetSession(in ctx, out TileEffectSession session))
                return false;

            session.Enqueue(TileMutation.New(
                mutationId: session.NextMutationId(),
                operationId: session.OperationId,
                kind: TileMutationKind.ApplyPayloadBlock,
                world: new Point(x, y),
                payload: payload));
            return true;
        }

        public static bool HasItemForTile(ushort tileType, int style, in PasteContext ctx)
        {
            if (!ctx.RequireItems || ctx.InventoryLookup is null || ctx.IsSweepCheat)
                return true;
            return ctx.InventoryLookup.HasItemForTile(tileType, style);
        }

        /// <summary>Whether required inventory contains an item for the given wall type.</summary>
        public static bool HasItemForWall(ushort wallType, in PasteContext ctx)
        {
            if (!ctx.RequireItems || ctx.InventoryLookup is null || ctx.IsSweepCheat)
                return true;
            return ctx.InventoryLookup.HasItemForWall(wallType);
        }

        internal static bool ProcessQueuedMutation(TileEffectSession session, TileMutation mutation)
        {
            PasteContext ctx = session.ContextFactory();
            bool wantsProjectile = mutation.ProjectileMode is not null;
            int arrowsBefore = session.PendingImpactCount;

            bool result;
            if (mutation.ProjectileMode is null)
                result = ApplyMutation(mutation, in ctx);
            else
                result = PasteArrow.TryFire(session, mutation, in ctx);

            if (wantsProjectile && session.PendingImpactCount == arrowsBefore)
                TileExecutorLog.Projectile(in mutation, fired: false);

            return result;
        }

        internal static void ProcessActiveArrows(TileEffectSession session)
        {
            PasteContext ctx = session.ContextFactory();
            session.CollectArrowsToResolve(Main.GameUpdateCount, session.ResolveScratch);
            List<int> resolveIds = session.ResolveScratch;
            for (int i = 0; i < resolveIds.Count; i++)
                ResolveArrow(session, resolveIds[i], in ctx);
        }

        /// <summary>
        /// Drops in-flight arrow payloads and kills bound projectiles when a paste session ends early.
        /// </summary>
        internal static void TerminateSession(TileEffectSession session)
        {
            if (session.PendingImpactCount == 0)
                return;

            PasteContext ctx = session.ContextFactory();
            session.CopyActiveArrowIds(session.ResolveScratch);
            List<int> arrowIds = session.ResolveScratch;
            for (int i = 0; i < arrowIds.Count; i++)
            {
                int arrowId = arrowIds[i];
                if (!session.TryGetArrowSlot(arrowId, out _, out PasteArrowSlot slot))
                    continue;

                Point dropTile = ResolveAbortDropTile(in slot, session);
                AbortArrow(session, arrowId, dropTile, in ctx);
                KillBoundProjectile(in slot);
            }
        }

        internal static bool ApplyMutation(TileMutation mutation, in PasteContext ctx)
        {
            int x = mutation.World.X;
            int y = mutation.World.Y;
            if (!WorldGen.InWorld(x, y))
                return false;

            bool applied;
            switch (mutation.Kind)
            {
                case TileMutationKind.PlaceTile:
                    applied = WorldGen.PlaceTile(x, y, mutation.Type, mute: false, forced: mutation.Forced, plr: ctx.Player.whoAmI, style: Math.Max(0, mutation.Style));
                    break;
                case TileMutationKind.PlaceObject:
                    applied = WorldGen.PlaceObject(x, y, mutation.Type, mute: true, Math.Max(0, mutation.Style), mutation.Alternate, random: -1, direction: -1);
                    break;
                case TileMutationKind.PlaceWall:
                    WorldGen.PlaceWall(x, y, mutation.Type, mute: false);
                    applied = Main.tile[x, y].WallType == mutation.Type;
                    break;
                case TileMutationKind.PlaceWireRed:
                    WorldGen.PlaceWire(x, y);
                    applied = Main.tile[x, y].RedWire;
                    break;
                case TileMutationKind.PlaceWireBlue:
                    WorldGen.PlaceWire2(x, y);
                    applied = Main.tile[x, y].BlueWire;
                    break;
                case TileMutationKind.PlaceWireGreen:
                    WorldGen.PlaceWire3(x, y);
                    applied = Main.tile[x, y].GreenWire;
                    break;
                case TileMutationKind.PlaceWireYellow:
                    WorldGen.PlaceWire4(x, y);
                    applied = Main.tile[x, y].YellowWire;
                    break;
                case TileMutationKind.PlaceActuator:
                    WorldGen.PlaceActuator(x, y);
                    applied = Main.tile[x, y].HasActuator;
                    break;
                case TileMutationKind.ClearTile:
                    applied = ApplyClearTile(x, y, mutation, in ctx);
                    break;
                case TileMutationKind.ClearWall:
                    applied = ApplyClearWall(x, y, mutation, in ctx);
                    break;
                case TileMutationKind.ClearWireRed:
                    applied = ApplyClearWire(x, y, mutation, ItemID.Wire, () => WorldGen.KillWire(x, y), () => SuppressTileDrop.KillWire(x, y), in ctx);
                    break;
                case TileMutationKind.ClearWireBlue:
                    applied = ApplyClearWire(x, y, mutation, ItemID.Wire, () => WorldGen.KillWire2(x, y), () => SuppressTileDrop.KillWire2(x, y), in ctx);
                    break;
                case TileMutationKind.ClearWireGreen:
                    applied = ApplyClearWire(x, y, mutation, ItemID.Wire, () => WorldGen.KillWire3(x, y), () => SuppressTileDrop.KillWire3(x, y), in ctx);
                    break;
                case TileMutationKind.ClearWireYellow:
                    applied = ApplyClearWire(x, y, mutation, ItemID.Wire, () => WorldGen.KillWire4(x, y), () => SuppressTileDrop.KillWire4(x, y), in ctx);
                    break;
                case TileMutationKind.ClearActuator:
                    applied = ApplyClearWire(x, y, mutation, ItemID.Actuator, () => WorldGen.KillActuator(x, y), () => SuppressTileDrop.KillActuator(x, y), in ctx);
                    break;
                case TileMutationKind.ApplyDetailState:
                    applied = ApplyDetailStateMutation(x, y, mutation);
                    break;
                case TileMutationKind.ApplyPaintState:
                    applied = ApplyPaintStateMutation(x, y, mutation);
                    break;
                case TileMutationKind.SetLiquid:
                    applied = ApplyLiquidMutation(x, y, mutation);
                    break;
                case TileMutationKind.ApplyTileFrame:
                    applied = ApplyFrameMutation(x, y, mutation);
                    break;
                case TileMutationKind.ApplyPayloadBlock:
                    applied = ApplyPayloadBlockMutation(x, y, mutation);
                    break;
                case TileMutationKind.VoidKillTile:
                    SuppressTileDrop.KillTile(x, y);
                    applied = true;
                    break;
                case TileMutationKind.Sweep:
                    applied = ApplySweepMutation(mutation, x, y);
                    break;
                default:
                    applied = false;
                    break;
            }

            if (applied)
            {
                SyncTileSquare(x, y);
                if (mutation.Kind == TileMutationKind.SetLiquid)
                    SyncLiquid(x, y);
            }

            TileExecutorLog.Applied(in mutation, applied);
            return applied;
        }

        private static bool TryGetSession(in PasteContext ctx, out TileEffectSession session)
        {
            session = ctx.EffectSession!;
            if (session is not null)
                return true;

            TABLog.Warn("Enqueue rejected: no effect session on paste context", TABLog.TileExecutorCategory);
            return false;
        }

        private static bool EnqueueClear(TileMutationKind kind, Point world, int itemType, in PasteContext ctx)
        {
            if (!TryGetSession(in ctx, out TileEffectSession session))
                return false;

            PasteProjectileMode? mode = ResolveClearProjectileMode(itemType, ctx.ItemDropStyle, in ctx);
            session.Enqueue(TileMutation.New(
                mutationId: session.NextMutationId(),
                operationId: session.OperationId,
                kind: kind,
                world: world,
                itemDropStyle: ctx.ItemDropStyle,
                projectileMode: mode,
                itemType: itemType,
                itemStack: 1));
            return true;
        }

        private static bool EnqueueSimpleMutation(TileMutationKind kind, Point world, in PasteContext ctx)
        {
            if (!TryGetSession(in ctx, out TileEffectSession session))
                return false;

            session.Enqueue(TileMutation.New(
                mutationId: session.NextMutationId(),
                operationId: session.OperationId,
                kind: kind,
                world: world));
            return true;
        }

        internal static bool TryFirePasteArrow(
            TileEffectSession session,
            TileMutation mutation,
            in PasteContext ctx)
        {
            if (mutation.ProjectileMode == PasteProjectileMode.Place)
                return TryFirePlacePasteArrow(session, mutation, in ctx);
            if (mutation.ProjectileMode == PasteProjectileMode.Clear)
                return TryFireStorePasteArrow(session, mutation, in ctx);
            return ApplyMutation(mutation with { ProjectileMode = null }, in ctx);
        }

        internal static void ResolveArrow(TileEffectSession session, int arrowId, in PasteContext ctx)
        {
            if (!session.TryTakeArrow(arrowId, out PasteArrowSlot removed, out TileMutation mutation))
                return;

            bool arrivedAtDestination = removed.ArrivedAtDestination;
            Point dropTile = removed.DropTile == default ? mutation.World : removed.DropTile;
            ResolveTakenArrow(in mutation, in removed, dropTile, in ctx);
        }

        private static void AbortArrow(TileEffectSession session, int arrowId, Point dropTile, in PasteContext ctx)
        {
            if (!session.TryTakeArrow(arrowId, out PasteArrowSlot removed, out TileMutation mutation))
                return;

            Point resolvedDropTile = dropTile == default ? mutation.World : dropTile;
            ResolveTakenArrow(in mutation, in removed, resolvedDropTile, in ctx, arrivedAtDestination: false);
            TileExecutorLog.ImpactResolved(in mutation, arrived: false, ok: false, $"drop=({resolvedDropTile.X},{resolvedDropTile.Y}) mode=terminate");
        }

        private static void ResolveTakenArrow(
            in TileMutation mutation,
            in PasteArrowSlot removed,
            Point dropTile,
            in PasteContext ctx,
            bool? arrivedAtDestination = null)
        {
            bool arrived = arrivedAtDestination ?? removed.ArrivedAtDestination;
            bool actionSucceeded = false;
            TileExecutorLog.ArrowState(
                in mutation,
                state: arrivedAtDestination.HasValue ? "Abort" : "Resolve",
                detail: $"arrived={(arrived ? "yes" : "no")} mode={(removed.IsHeadless ? "headless" : "bound")}");

            if (mutation.ProjectileMode == PasteProjectileMode.Place)
            {
                actionSucceeded = arrived && ApplyMutation(mutation with { ProjectileMode = null }, in ctx);
                if (!actionSucceeded && mutation.ItemType > 0 && mutation.ItemStack > 0)
                    InventoryManager.DropItemAtTile(mutation.ItemType, mutation.ItemStack, dropTile);
                if (!arrivedAtDestination.HasValue)
                    TileExecutorLog.ImpactResolved(in mutation, arrived, actionSucceeded, $"drop=({dropTile.X},{dropTile.Y})");
                return;
            }

            if (mutation.ProjectileMode == PasteProjectileMode.Clear)
            {
                if (!arrived || ctx.InventoryLookup is null)
                {
                    if (mutation.ItemType > 0 && mutation.ItemStack > 0)
                        InventoryManager.DropItemAtTile(mutation.ItemType, mutation.ItemStack, dropTile);
                    if (!arrivedAtDestination.HasValue)
                        TileExecutorLog.ImpactResolved(in mutation, arrived, ok: false, $"drop=({dropTile.X},{dropTile.Y}) mode=abort");
                    return;
                }

                int remaining = ctx.InventoryLookup.TryStoreAtDestination(
                    mutation.ItemType,
                    mutation.ItemStack,
                    mutation.TargetInventoryIndex);
                actionSucceeded = remaining <= 0;

                if (mutation.TargetInventoryIndex >= 0
                    && mutation.TargetInventoryIndex < ctx.InventoryLookup.Inventories.Count
                    && ctx.InventoryLookup.Inventories[mutation.TargetInventoryIndex].Chest is Chest destinationChest)
                {
                    InventoryManager.PlayChestEatAnimation(destinationChest);
                }

                if (remaining > 0)
                    InventoryManager.DropItemAtTile(mutation.ItemType, remaining, ResolveDestinationDropTile(in ctx, mutation, dropTile));

                if (!arrivedAtDestination.HasValue)
                {
                    TileExecutorLog.ImpactResolved(
                        in mutation,
                        arrived,
                        actionSucceeded,
                        $"remaining={remaining} targetIndex={mutation.TargetInventoryIndex}");
                }
            }
        }

        private static Point ResolveAbortDropTile(in PasteArrowSlot slot, TileEffectSession session)
        {
            if (!slot.IsHeadless && slot.ProjectileIndex >= 0 && slot.ProjectileIndex < Main.maxProjectiles)
            {
                Projectile projectile = Main.projectile[slot.ProjectileIndex];
                if (projectile.active && projectile.type == ModContent.ProjectileType<PasteArrow>())
                {
                    return new Point(
                        (int)(projectile.Center.X / 16f),
                        (int)(projectile.Center.Y / 16f));
                }
            }

            if (slot.DropTile != default)
                return slot.DropTile;

            return session.GetMutation(slot.MutationIndex).World;
        }

        private static void KillBoundProjectile(in PasteArrowSlot slot)
        {
            if (slot.IsHeadless)
                return;

            int index = slot.ProjectileIndex;
            if (index < 0 || index >= Main.maxProjectiles)
                return;

            Projectile projectile = Main.projectile[index];
            if (projectile.active && projectile.type == ModContent.ProjectileType<PasteArrow>())
                projectile.Kill();
        }

        private static bool TryFirePlacePasteArrow(
            TileEffectSession session,
            TileMutation mutation,
            in PasteContext ctx)
        {
            if (!ShouldConsumeOnPlace(in ctx, mutation.ConsumeItem))
                return ApplyMutation(mutation with { ProjectileMode = null }, in ctx);

            int placeStyle = mutation.Style;
            int itemType;
            Vector2 origin;
            bool fromChest;
            Chest? sourceChest;
            if (ctx.InventoryLookup is null)
                return false;

            bool reserved = mutation.Kind == TileMutationKind.PlaceWall
                ? ctx.InventoryLookup.TryReserveForWallMutation(
                    mutation.Type,
                    ctx.Player,
                    out itemType,
                    out origin,
                    out fromChest,
                    out sourceChest)
                : ctx.InventoryLookup.TryReserveForTileMutation(
                    mutation.Type,
                    mutation.Style,
                    ctx.Player,
                    out placeStyle,
                    out itemType,
                    out origin,
                    out fromChest,
                    out sourceChest);
            if (!reserved)
                return false;

            var impactMutation = mutation with
            {
                Style = placeStyle,
                ItemType = itemType,
                ItemStack = 1
            };

            Vector2 destination = InventoryManager.GetTileWorldCenter(mutation.World);
            uint firedAt = Main.GameUpdateCount;
            ArrowFlightProfile flight = ComputeArrowFlightProfile(in ctx);
            uint arriveBy = firedAt + (uint)flight.ManeuverTicks;
            int projectileIndex = -1;
            bool spawned = SpawnProjectile(
                impactMutation,
                origin,
                destination,
                ctx.Player.whoAmI,
                out projectileIndex);
            if (!spawned)
                TileExecutorLog.LaunchFailed(in impactMutation, "projectile spawn failed, switching to headless");

            int mutationIndex = session.StoreMutation(impactMutation);
            session.RegisterArrow(new PasteArrowSlot
            {
                ArrowId = impactMutation.MutationId,
                MutationIndex = mutationIndex,
                Destination = destination,
                ProjectileIndex = spawned ? projectileIndex : -1,
                ArriveByUpdate = arriveBy,
                TimeoutTicks = flight.TimeoutTicks,
                ManeuverTicks = flight.ManeuverTicks,
                MinSpeed = flight.MinSpeed,
                MaxSpeed = flight.MaxSpeed,
                DropTile = impactMutation.World,
                ItemType = impactMutation.ItemType,
            });
            TileExecutorLog.ArrowState(
                in impactMutation,
                state: spawned ? "Fire" : "Headless",
                detail: $"eta={arriveBy - firedAt}");
            TileExecutorLog.Projectile(in impactMutation, fired: true);
            if (fromChest && sourceChest is not null)
                InventoryManager.PlayChestEatAnimation(sourceChest);
            return true;
        }

        private static bool TryFireStorePasteArrow(
            TileEffectSession session,
            TileMutation mutation,
            in PasteContext ctx)
        {
            if (mutation.ItemType <= 0 || ctx.InventoryLookup is null)
                return ApplyMutation(mutation with { ProjectileMode = null }, in ctx);

            if (!ApplyMutation(mutation with { ItemDropStyle = ClearItemDropStyle.Void, ProjectileMode = null }, in ctx))
            {
                TileExecutorLog.LaunchFailed(in mutation, "clear extraction failed");
                return false;
            }

            int inventoryIndex = -1;
            if (!ctx.InventoryLookup.TryResolveStoreDestinationForItem(
                    mutation.ItemType,
                    ctx.Player,
                    out Vector2 destination,
                    out inventoryIndex))
            {
                destination = ctx.Player.Center;
            }

            var trackedMutation = mutation with
            {
                ItemStack = Math.Max(1, mutation.ItemStack),
                TargetInventoryIndex = inventoryIndex
            };

            Vector2 origin = InventoryManager.GetTileWorldCenter(mutation.World);
            uint firedAt = Main.GameUpdateCount;
            ArrowFlightProfile flight = ComputeArrowFlightProfile(in ctx);
            uint arriveBy = firedAt + (uint)flight.ManeuverTicks;
            int projectileIndex = -1;
            bool spawned = SpawnProjectile(
                trackedMutation,
                origin,
                destination,
                ctx.Player.whoAmI,
                out projectileIndex);
            if (!spawned)
                TileExecutorLog.LaunchFailed(in trackedMutation, "projectile spawn failed, switching to headless");

            int mutationIndex = session.StoreMutation(trackedMutation);
            session.RegisterArrow(new PasteArrowSlot
            {
                ArrowId = trackedMutation.MutationId,
                MutationIndex = mutationIndex,
                Destination = destination,
                ProjectileIndex = spawned ? projectileIndex : -1,
                ArriveByUpdate = arriveBy,
                TimeoutTicks = flight.TimeoutTicks,
                ManeuverTicks = flight.ManeuverTicks,
                MinSpeed = flight.MinSpeed,
                MaxSpeed = flight.MaxSpeed,
                DropTile = trackedMutation.World,
                ItemType = trackedMutation.ItemType,
            });
            TileExecutorLog.ArrowState(
                in trackedMutation,
                state: spawned ? "Fire" : "Headless",
                detail: $"eta={arriveBy - firedAt}");
            TileExecutorLog.Projectile(in trackedMutation, fired: true);
            return true;
        }

        internal static bool SpawnProjectile(
            TileMutation mutation,
            Vector2 origin,
            Vector2 destination,
            int ownerPlayerId,
            out int projectileIndex)
        {
            projectileIndex = -1;
            int projType = ModContent.ProjectileType<PasteArrow>();
            int itemType = Math.Max(0, mutation.ItemType);
            int projIndex = Projectile.NewProjectile(
                new EntitySource_Misc("TAB.TileMutation"),
                origin.X,
                origin.Y,
                0f,
                0f,
                projType,
                itemType,
                0f,
                ownerPlayerId,
                (float)mutation.ProjectileMode!.Value,
                mutation.OperationId,
                mutation.MutationId);
            if (projIndex < 0 || projIndex >= Main.maxProjectiles)
            {
                TABLog.Warn(
                    $"Projectile spawn failed id={mutation.MutationId} {mutation.ProjectileMode}",
                    TABLog.TileExecutorCategory);
                return false;
            }

            Projectile p = Main.projectile[projIndex];
            p.localAI[0] = destination.X;
            p.localAI[1] = destination.Y;
            p.localAI[2] = itemType;
            p.netUpdate = true;

            projectileIndex = projIndex;
            return true;
        }

        private static bool ApplyClearTile(int x, int y, TileMutation mutation, in PasteContext ctx)
        {
            switch (mutation.ItemDropStyle)
            {
                case ClearItemDropStyle.Void:
                    SuppressTileDrop.KillTile(x, y);
                    return true;
                case ClearItemDropStyle.Drop:
                    WorldGen.KillTile(x, y, fail: false, effectOnly: false, noItem: false);
                    return true;
                case ClearItemDropStyle.Store:
                    SuppressTileDrop.KillTile(x, y);
                    if (mutation.ItemType > 0)
                        ctx.InventoryLookup?.ApplyStoreOnArrival(mutation.ItemType, mutation.ItemStack, mutation.World);
                    return true;
                default:
                    return false;
            }
        }

        private static bool ApplyClearWall(int x, int y, TileMutation mutation, in PasteContext ctx)
        {
            switch (mutation.ItemDropStyle)
            {
                case ClearItemDropStyle.Void:
                    SuppressTileDrop.KillWall(x, y);
                    return true;
                case ClearItemDropStyle.Drop:
                    WorldGen.KillWall(x, y, fail: false);
                    return true;
                case ClearItemDropStyle.Store:
                    SuppressTileDrop.KillWall(x, y);
                    if (mutation.ItemType > 0)
                        ctx.InventoryLookup?.ApplyStoreOnArrival(mutation.ItemType, mutation.ItemStack, mutation.World);
                    return true;
                default:
                    return false;
            }
        }

        private static bool ApplyClearWire(
            int x,
            int y,
            TileMutation mutation,
            int itemId,
            Action dropKill,
            Action voidKill,
            in PasteContext ctx)
        {
            switch (mutation.ItemDropStyle)
            {
                case ClearItemDropStyle.Void:
                    voidKill();
                    return true;
                case ClearItemDropStyle.Drop:
                    dropKill();
                    return true;
                case ClearItemDropStyle.Store:
                    voidKill();
                    ctx.InventoryLookup?.ApplyStoreOnArrival(itemId, 1, mutation.World);
                    return true;
                default:
                    return false;
            }
        }

        private static bool ApplyDetailStateMutation(int x, int y, TileMutation mutation)
        {
            Tile live = Main.tile[x, y];
            if (!live.HasTile)
                return false;

            bool desiredActuated = (mutation.Alternate & DetailActuatedFlag) != 0;
            bool desiredHalfBlock = (mutation.Alternate & DetailHalfBlockFlag) != 0;
            SlopeType desiredSlope = (SlopeType)Math.Clamp(mutation.Style, 0, 4);
            BlockType desiredBlockType = (BlockType)mutation.Type;

            if (live.IsHalfBlock != desiredHalfBlock)
            {
                WorldGen.PoundTile(x, y);
                live = Main.tile[x, y];
            }

            if (live.Slope != desiredSlope)
            {
                if (desiredSlope == SlopeType.Solid)
                    WorldGen.SlopeTile(x, y, 0);
                else
                    WorldGen.SlopeTile(x, y, (int)desiredSlope, noEffects: false);
            }

            TileWallWireStateData state = Main.tile[x, y].Get<TileWallWireStateData>();
            if (state.IsActuated != desiredActuated || state.BlockType != desiredBlockType)
            {
                state.IsActuated = desiredActuated;
                state.BlockType = desiredBlockType;
                Main.tile[x, y].Get<TileWallWireStateData>() = state;
            }

            Tile result = Main.tile[x, y];
            TileWallWireStateData resultState = result.Get<TileWallWireStateData>();
            return result.HasTile
                && result.IsHalfBlock == desiredHalfBlock
                && result.Slope == desiredSlope
                && resultState.IsActuated == desiredActuated
                && resultState.BlockType == desiredBlockType;
        }

        private static bool ApplyPaintStateMutation(int x, int y, TileMutation mutation)
        {
            Tile live = Main.tile[x, y];
            byte tileColor = (byte)Math.Clamp((int)mutation.Type, 0, 255);
            byte wallColor = (byte)Math.Clamp(mutation.Style, 0, 255);
            bool tileInvisible = HasPaintFlag(mutation.Alternate, PaintTileInvisibleFlag);
            bool wallInvisible = HasPaintFlag(mutation.Alternate, PaintWallInvisibleFlag);
            bool tileFullbright = HasPaintFlag(mutation.Alternate, PaintTileFullbrightFlag);
            bool wallFullbright = HasPaintFlag(mutation.Alternate, PaintWallFullbrightFlag);

            if (live.HasTile && live.TileColor != tileColor)
                WorldGen.paintTile(x, y, tileColor);
            if (live.WallType != WallID.None && live.WallColor != wallColor)
                WorldGen.paintWall(x, y, wallColor);
            if (live.HasTile)
                ApplyBlockCoating(x, y, tileInvisible, tileFullbright);
            if (live.WallType != WallID.None)
                ApplyWallCoating(x, y, wallInvisible, wallFullbright);

            Tile result = Main.tile[x, y];
            TileColorCache block = result.BlockColorAndCoating();
            TileColorCache wall = result.WallColorAndCoating();
            bool tileOk = !result.HasTile || (result.TileColor == tileColor && block.Invisible == tileInvisible && block.FullBright == tileFullbright);
            bool wallOk = result.WallType == WallID.None || (result.WallColor == wallColor && wall.Invisible == wallInvisible && wall.FullBright == wallFullbright);
            return tileOk && wallOk;
        }

        private static bool ApplyLiquidMutation(int x, int y, TileMutation mutation)
        {
            int liquidType = Math.Clamp((int)mutation.Type, 0, 255);
            byte amount = (byte)Math.Clamp(mutation.Style, 0, byte.MaxValue);
            if (amount == 0)
            {
                if (Main.tile[x, y].LiquidAmount == 0)
                    return true;

                SuppressTileDrop.EmptyLiquid(x, y);
                return Main.tile[x, y].LiquidAmount == 0;
            }

            return TryWriteLiquid(x, y, liquidType, amount);
        }

        private static bool ApplyFrameMutation(int x, int y, TileMutation mutation)
        {
            Tile tile = Main.tile[x, y];
            if (!tile.HasTile)
                return false;

            TileWallWireStateData state = tile.Get<TileWallWireStateData>();
            state.TileFrameX = (short)Math.Clamp(mutation.Style, short.MinValue, short.MaxValue);
            state.TileFrameY = (short)Math.Clamp(mutation.Alternate, short.MinValue, short.MaxValue);
            tile.Get<TileWallWireStateData>() = state;
            TileLoaderSupport.RunPostPlacementHooks(x, y);
            return tile.HasTile
                && tile.TileFrameX == state.TileFrameX
                && tile.TileFrameY == state.TileFrameY;
        }

        private static bool ApplyPayloadBlockMutation(int x, int y, TileMutation mutation)
        {
            PayloadCell payload = mutation.Payload;
            if (payload.IsEmpty || !payload.Detail.HasTile || payload.Block.Type >= TileLoader.TileCount)
                return false;

            Tile dest = Main.tile[x, y];
            dest.HasTile = true;
            dest.TileType = (ushort)payload.Block.Type;

            TileWallWireStateData state = dest.Get<TileWallWireStateData>();
            state.TileFrameX = (short)payload.Block.FrameX;
            state.TileFrameY = (short)payload.Block.FrameY;
            state.BlockType = payload.Detail.BlockType;
            dest.Get<TileWallWireStateData>() = state;

            return dest.HasTile
                && dest.TileType == payload.Block.Type
                && dest.TileFrameX == state.TileFrameX
                && dest.TileFrameY == state.TileFrameY;
        }

        private static bool ApplySweepMutation(TileMutation mutation, int x, int y)
        {
            mutation.Payload.CopyToWorldTile(x, y);
            return WorldGen.InWorld(x, y);
        }

        private static bool ShouldUsePlaceProjectile(in PasteContext ctx, bool consumeItem) =>
            ShouldConsumeOnPlace(in ctx, consumeItem);

        private static bool ShouldConsumeOnPlace(in PasteContext ctx, bool consumeItem) =>
            consumeItem && ctx.RequireItems && ctx.InventoryLookup is not null && !ctx.IsSweepCheat;

        private static PasteProjectileMode? ResolveClearProjectileMode(int itemType, ClearItemDropStyle style, in PasteContext ctx)
        {
            if (style != ClearItemDropStyle.Store || itemType <= 0)
                return null;

            return ctx.InventoryLookup is not null ? PasteProjectileMode.Clear : null;
        }

        private static int PackDetailFlags(bool isActuated, bool isHalfBlock)
        {
            int flags = 0;
            if (isActuated)
                flags |= DetailActuatedFlag;
            if (isHalfBlock)
                flags |= DetailHalfBlockFlag;
            return flags;
        }

        private static int PackPaintFlags(bool tileInvisible, bool wallInvisible, bool tileFullbright, bool wallFullbright)
        {
            int flags = 0;
            if (tileInvisible)
                flags |= PaintTileInvisibleFlag;
            if (wallInvisible)
                flags |= PaintWallInvisibleFlag;
            if (tileFullbright)
                flags |= PaintTileFullbrightFlag;
            if (wallFullbright)
                flags |= PaintWallFullbrightFlag;
            return flags;
        }

        private static bool HasPaintFlag(int flags, int flag) => (flags & flag) != 0;

        private static bool ApplyBlockCoating(int x, int y, bool invisible, bool fullBright)
        {
            Tile tile = Main.tile[x, y];
            TileColorCache cache = tile.BlockColorAndCoating();
            if (cache.Invisible == invisible && cache.FullBright == fullBright)
                return false;

            cache.Invisible = invisible;
            cache.FullBright = fullBright;
            cache.ApplyToBlock(Main.tile[x, y]);
            return true;
        }

        private static bool ApplyWallCoating(int x, int y, bool invisible, bool fullBright)
        {
            Tile tile = Main.tile[x, y];
            TileColorCache cache = tile.WallColorAndCoating();
            if (cache.Invisible == invisible && cache.FullBright == fullBright)
                return false;

            cache.Invisible = invisible;
            cache.FullBright = fullBright;
            cache.ApplyToWall(Main.tile[x, y]);
            return true;
        }

        private static bool TryWriteLiquid(int x, int y, int liquidType, byte amount)
        {
            if (!WorldGen.InWorld(x, y))
                return false;

            if (WorldGen.PlaceLiquid(x, y, (byte)liquidType, amount))
                return true;

            Tile dest = Main.tile[x, y];
            ref LiquidData liquid = ref dest.Get<LiquidData>();
            liquid.LiquidType = liquidType;
            liquid.Amount = amount;
            liquid.SkipLiquid = false;
            liquid.CheckingLiquid = false;
            return Main.tile[x, y].LiquidAmount == amount && Main.tile[x, y].LiquidType == liquidType;
        }

        private static int ResolveStoreItemForClearTile(int x, int y, ClearItemDropStyle style)
        {
            if (style != ClearItemDropStyle.Store || !Main.tile[x, y].HasTile)
                return 0;

            Tile tile = Main.tile[x, y];
            int styleValue = 0;
            int alternate = 0;
            TileObjectData.GetTileInfo(tile, ref styleValue, ref alternate);
            int itemId = TileLoader.GetItemDropFromTypeAndStyle(tile.TileType, Math.Max(0, styleValue));
            return itemId > 0 ? itemId : 0;
        }

        private static int ResolveStoreItemForClearWall(int x, int y, ClearItemDropStyle style)
        {
            if (style != ClearItemDropStyle.Store)
                return 0;

            int wallType = Main.tile[x, y].WallType;
            if (wallType <= 0)
                return 0;

            for (int i = 1; i < ItemLoader.ItemCount; i++)
            {
                Item item = new();
                item.SetDefaults(i);
                if (item.createWall == wallType)
                    return i;
            }

            return 0;
        }

        private static void SyncTileSquare(int x, int y)
        {
            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendTileSquare(-1, x, y, 1, 1);
        }

        private static void SyncLiquid(int x, int y)
        {
            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.LiquidUpdate, -1, -1, null, x, y);
        }

        private static Point WorldToTile(Vector2 worldPosition) =>
            new((int)(worldPosition.X / 16f), (int)(worldPosition.Y / 16f));

        internal static bool IsTrackedBoundArrowActive(in PasteArrowSlot slot, in TileMutation mutation)
        {
            int index = slot.ProjectileIndex;
            if (index < 0 || index >= Main.maxProjectiles)
                return false;

            Projectile projectile = Main.projectile[index];
            if (!projectile.active || projectile.type != ModContent.ProjectileType<PasteArrow>())
                return false;

            int operationId = TileEffectSession.ReadProjectileOperationId(projectile);
            int arrowId = TileEffectSession.ReadProjectileMutationId(projectile);
            return operationId == mutation.OperationId && arrowId == slot.ArrowId;
        }

        internal static ArrowFlightProfile ComputeArrowFlightProfile(in PasteContext ctx)
        {
            int timeoutTicks = Math.Clamp(ctx.ArrowSpeed, ArrowSpeedMinTicks, ArrowSpeedMaxTicks);
            int maneuverTicks = Math.Max(1, timeoutTicks - ArrowManeuverLeadTicks);
            float minSpeed = Math.Clamp(ctx.ArrowMinSpeed, 0.5f, 48f);
            float maxSpeed = Math.Clamp(ctx.ArrowMaxSpeed, minSpeed, 96f);
            return new ArrowFlightProfile
            {
                TimeoutTicks = timeoutTicks,
                ManeuverTicks = maneuverTicks,
                MinSpeed = minSpeed,
                MaxSpeed = maxSpeed,
            };
        }

        internal static ArrowFlightProfile ComputeArrowFlightProfileFromClientConfig()
        {
            var client = ModContent.GetInstance<ClientPasteConfig>();
            int timeoutTicks = Math.Clamp(client.Arrow.Speed, ArrowSpeedMinTicks, ArrowSpeedMaxTicks);
            int maneuverTicks = Math.Max(1, timeoutTicks - ArrowManeuverLeadTicks);
            float minSpeed = Math.Clamp(client.Arrow.MinSpeed, 0.5f, 48f);
            float maxSpeed = Math.Clamp(client.Arrow.MaxSpeed, minSpeed, 96f);
            return new ArrowFlightProfile
            {
                TimeoutTicks = timeoutTicks,
                ManeuverTicks = maneuverTicks,
                MinSpeed = minSpeed,
                MaxSpeed = maxSpeed,
            };
        }

        internal static int GetMaxConcurrentArrows()
        {
            int configured = ModContent.GetInstance<PastingConfig>().Limits.Server.MaxConcurrentArrows;
            return Math.Clamp(configured, 1, 512);
        }

        private static Point ResolveDestinationDropTile(in PasteContext ctx, TileMutation mutation, Point fallbackTile)
        {
            if (ctx.InventoryLookup is null)
                return fallbackTile;

            if (mutation.TargetInventoryIndex >= 0
                && mutation.TargetInventoryIndex < ctx.InventoryLookup.Inventories.Count)
            {
                Vector2 target = InventoryManager.GetInventoryWorldCenter(
                    ctx.InventoryLookup.Inventories[mutation.TargetInventoryIndex],
                    ctx.Player);
                return WorldToTile(target);
            }

            return ctx.InventoryLookup.TryResolveStoreDestinationForItem(
                mutation.ItemType,
                ctx.Player,
                out Vector2 destination,
                out _)
                ? WorldToTile(destination)
                : fallbackTile;
        }
    }
}
