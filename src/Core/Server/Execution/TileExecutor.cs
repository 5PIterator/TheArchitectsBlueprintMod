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
using TheArchitectsBlueprintMod.Core.Server;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.Debug;
using TheArchitectsBlueprintMod.Utils;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Whether a tile/wall can be supplied from tracked inventories and the interaction map.</summary>
    internal enum TileItemAvailability
    {
        Available,
        MissingFromInventory,
        UnresolvedInteraction,
    }

    /// <summary>
    /// Central entry point for queued paste tile/wall/wire execution.
    /// </summary>
    internal static class TileExecutor
    {
        private const int DetailActuatedFlag = 1 << 0;
        private const int DetailHalfBlockFlag = 1 << 1;
        private const int ArrowSpeedMinTicks = 10;
        private const int ArrowSpeedMaxTicks = 600;
        private const int ArrowManeuverLeadTicks = 6;

        public static bool EnqueuePlaceTile(
            int x,
            int y,
            ushort tileType,
            int style,
            in PasteContext ctx,
            bool forced,
            bool consumeItem,
            short postPlaceFrameX = TileMutation.NoPostPlaceFrame,
            short postPlaceFrameY = TileMutation.NoPostPlaceFrame)
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
                projectileMode: mode,
                postPlaceFrameX: postPlaceFrameX,
                postPlaceFrameY: postPlaceFrameY));
            return true;
        }

        public static bool EnqueuePlaceObject(
            int x,
            int y,
            ushort tileType,
            int style,
            int alternate,
            in PasteContext ctx,
            bool consumeItem,
            short postPlaceFrameX = TileMutation.NoPostPlaceFrame,
            short postPlaceFrameY = TileMutation.NoPostPlaceFrame,
            int direction = -1)
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
                direction: direction,
                consumeItem: consumeItem,
                projectileMode: mode,
                postPlaceFrameX: postPlaceFrameX,
                postPlaceFrameY: postPlaceFrameY));
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

        public static bool EnqueuePlaceWire(int x, int y, in PasteContext ctx, bool consumeItem = false) =>
            EnqueueSimpleMutation(TileMutationKind.PlaceWireRed, new Point(x, y), in ctx, consumeItem);
        public static bool EnqueuePlaceWire2(int x, int y, in PasteContext ctx, bool consumeItem = false) =>
            EnqueueSimpleMutation(TileMutationKind.PlaceWireBlue, new Point(x, y), in ctx, consumeItem);
        public static bool EnqueuePlaceWire3(int x, int y, in PasteContext ctx, bool consumeItem = false) =>
            EnqueueSimpleMutation(TileMutationKind.PlaceWireGreen, new Point(x, y), in ctx, consumeItem);
        public static bool EnqueuePlaceWire4(int x, int y, in PasteContext ctx, bool consumeItem = false) =>
            EnqueueSimpleMutation(TileMutationKind.PlaceWireYellow, new Point(x, y), in ctx, consumeItem);
        public static bool EnqueuePlaceActuator(int x, int y, in PasteContext ctx, bool consumeItem = false) =>
            EnqueueSimpleMutation(TileMutationKind.PlaceActuator, new Point(x, y), in ctx, consumeItem);

        public static bool EnqueueClearTile(int x, int y, in PasteContext ctx)
        {
            ResolveStoreDropForClearTile(x, y, ctx.ItemDropStyle, out int itemType, out int itemStack);
            return EnqueueClear(
                kind: TileMutationKind.ClearTile,
                world: new Point(x, y),
                itemType: itemType,
                itemStack: itemStack,
                in ctx);
        }

        public static bool EnqueueClearWall(int x, int y, in PasteContext ctx)
        {
            ResolveStoreDropForClearWall(x, y, ctx.ItemDropStyle, out int itemType, out int itemStack);
            return EnqueueClear(
                kind: TileMutationKind.ClearWall,
                world: new Point(x, y),
                itemType: itemType,
                itemStack: itemStack,
                in ctx);
        }

        public static bool EnqueueClearWire(int x, int y, in PasteContext ctx) =>
            EnqueueClear(TileMutationKind.ClearWireRed, new Point(x, y), ItemID.Wire, 1, in ctx);
        public static bool EnqueueClearWire2(int x, int y, in PasteContext ctx) =>
            EnqueueClear(TileMutationKind.ClearWireBlue, new Point(x, y), ItemID.Wire, 1, in ctx);
        public static bool EnqueueClearWire3(int x, int y, in PasteContext ctx) =>
            EnqueueClear(TileMutationKind.ClearWireGreen, new Point(x, y), ItemID.Wire, 1, in ctx);
        public static bool EnqueueClearWire4(int x, int y, in PasteContext ctx) =>
            EnqueueClear(TileMutationKind.ClearWireYellow, new Point(x, y), ItemID.Wire, 1, in ctx);
        public static bool EnqueueClearActuator(int x, int y, in PasteContext ctx) =>
            EnqueueClear(TileMutationKind.ClearActuator, new Point(x, y), ItemID.Actuator, 1, in ctx);

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

        /// <summary>Applies a sweep payload cell to the world immediately and returns whether application succeeded.</summary>
        public static bool EnqueueSweep(in PasteTile tile, in PasteContext ctx)
        {
            // --- Sweep never uses projectiles; apply now so callers can verify the live world tile.

            if (!TryGetSession(in ctx, out TileEffectSession session))
                return false;

            // Build the mutation, then process it inline instead of deferring to the session queue.
            TileMutation mutation = session.ApplyActiveSyncStamp(TileMutation.New(
                mutationId: session.NextMutationId(),
                operationId: session.OperationId,
                kind: TileMutationKind.Sweep,
                world: tile.World,
                payload: tile.PayloadCell));

            return ProcessQueuedMutation(session, mutation);
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

        /// <summary>Queues an atomic tile/wall paint and coating write at the world cell.</summary>
        public static bool EnqueueApplyPaintState(
            int x,
            int y,
            byte tileColor,
            byte wallColor,
            bool tileInvisible,
            bool wallInvisible,
            bool tileFullbright,
            bool wallFullbright,
            in PasteContext ctx,
            bool consumeItem = false)
        {
            // --- Pack paint facets into PaintMutationState; keep Style free for placement tools.

            if (!TryGetSession(in ctx, out TileEffectSession session))
                return false;

            PasteProjectileMode? mode = ShouldUsePlaceProjectile(in ctx, consumeItem) ? PasteProjectileMode.Place : null;
            session.Enqueue(TileMutation.New(
                mutationId: session.NextMutationId(),
                operationId: session.OperationId,
                kind: TileMutationKind.ApplyPaintState,
                world: new Point(x, y),
                consumeItem: consumeItem,
                projectileMode: mode,
                paint: new PaintMutationState(
                    tileColor,
                    wallColor,
                    tileInvisible,
                    wallInvisible,
                    tileFullbright,
                    wallFullbright)));
            return true;
        }

        public static bool EnqueueSetLiquid(int x, int y, int liquidType, byte amount, in PasteContext ctx, bool consumeItem = false)
        {
            if (!TryGetSession(in ctx, out TileEffectSession session))
                return false;

            PasteProjectileMode? mode = ShouldUsePlaceProjectile(in ctx, consumeItem) ? PasteProjectileMode.Place : null;
            session.Enqueue(TileMutation.New(
                mutationId: session.NextMutationId(),
                operationId: session.OperationId,
                kind: TileMutationKind.SetLiquid,
                world: new Point(x, y),
                type: (ushort)Math.Clamp(liquidType, 0, 255),
                style: amount,
                consumeItem: consumeItem,
                projectileMode: mode));
            return true;
        }

        /// <summary>Enqueues one liquid-pool mutation that writes every changed cell when its single arrow arrives.</summary>
        public static bool EnqueueSetLiquidPool(IReadOnlyList<LiquidPoolCell> cells, in PasteContext ctx, bool consumeItem = false)
        {
            // --- Attach pool payload to one SetLiquid mutation so FluidLayer fires a single place arrow.
            if (cells is null || cells.Count == 0)
                return true;

            if (!TryGetSession(in ctx, out TileEffectSession session))
                return false;

            LiquidPoolCell[] pool = new LiquidPoolCell[cells.Count];
            for (int i = 0; i < cells.Count; i++)
                pool[i] = cells[i];

            int mutationId = session.NextMutationId();
            session.AttachLiquidPool(mutationId, pool);

            PasteProjectileMode? mode = ShouldUsePlaceProjectile(in ctx, consumeItem) ? PasteProjectileMode.Place : null;
            LiquidPoolCell first = pool[0];
            session.Enqueue(TileMutation.New(
                mutationId: mutationId,
                operationId: session.OperationId,
                kind: TileMutationKind.SetLiquid,
                world: first.World,
                type: (ushort)Math.Clamp(first.LiquidType, 0, 255),
                style: first.Amount,
                consumeItem: consumeItem,
                projectileMode: mode));

            TABLog.Debug(
                $"EnqueueLiquidPool mutation={mutationId} cells={pool.Length} liquid={first.LiquidType} consume={consumeItem}",
                TABLog.TileExecutorCategory);
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

        public static bool HasItemForTile(ushort tileType, int style, in PasteContext ctx, int? baseId = null)
        {
            if (ctx.IsSweep || ctx.InventoryLookup is null)
                return true;
            return GetTileItemAvailability(tileType, style, in ctx, baseId) == TileItemAvailability.Available;
        }

        /// <summary>Whether required inventory contains an item for the given wall type.</summary>
        public static bool HasItemForWall(ushort wallType, in PasteContext ctx)
        {
            if (ctx.IsSweep || ctx.InventoryLookup is null)
                return true;
            return GetWallItemAvailability(wallType, in ctx) == TileItemAvailability.Available;
        }

        /// <summary>Whether inventory can supply any mapped tool for the paint or coating main.</summary>
        public static bool HasItemForPaint(int paintMainId, in PasteContext ctx)
        {
            if (ctx.IsSweep || ctx.InventoryLookup is null)
                return true;
            return ctx.InventoryLookup.GetPaintItemAvailability(
                    paintMainId,
                    ctx.InteractionBudget,
                    requireInventory: ctx.RequireItems)
                == TileItemAvailability.Available;
        }

        public static bool HasItemForInteraction(InteractionEntry entry, in PasteContext ctx)
        {
            if (ctx.IsSweep || ctx.InventoryLookup is null)
                return true;
            return ctx.InventoryLookup.GetInteractionAvailability(
                    entry,
                    ctx.InteractionBudget,
                    requireInventory: ctx.RequireItems)
                == TileItemAvailability.Available;
        }

        /// <summary>Returns true when inventory can supply any mapped tool for the liquid type.</summary>
        public static bool HasItemForLiquid(int liquidType, in PasteContext ctx)
        {
            if (ctx.IsSweep || ctx.InventoryLookup is null)
                return true;
            return ctx.InventoryLookup.GetLiquidItemAvailability(
                    liquidType,
                    ctx.InteractionBudget,
                    requireInventory: ctx.RequireItems)
                == TileItemAvailability.Available;
        }

        public static TileItemAvailability GetTileItemAvailability(ushort tileType, int style, in PasteContext ctx, int? baseId = null)
        {
            if (ctx.IsSweep || ctx.InventoryLookup is null)
                return TileItemAvailability.Available;
            return ctx.InventoryLookup.GetTileItemAvailability(
                tileType,
                style,
                ctx.InteractionBudget,
                baseId,
                requireInventory: ctx.RequireItems);
        }

        public static TileItemAvailability GetWallItemAvailability(ushort wallType, in PasteContext ctx)
        {
            if (ctx.IsSweep || ctx.InventoryLookup is null)
                return TileItemAvailability.Available;
            return ctx.InventoryLookup.GetWallItemAvailability(
                wallType,
                ctx.InteractionBudget,
                requireInventory: ctx.RequireItems);
        }

        /// <summary>Returns true when inventory can supply the interaction; otherwise sets <paramref name="failReason"/>.</summary>
        public static bool TryCheckInteraction(
            InteractionEntry entry,
            in PasteContext ctx,
            out LayerFailReason failReason)
        {
            failReason = LayerFailReason.None;
            if (ctx.IsSweep || ctx.InventoryLookup is null)
                return true;

            TileItemAvailability availability = ctx.InventoryLookup.GetInteractionAvailability(
                entry,
                ctx.InteractionBudget,
                requireInventory: ctx.RequireItems);
            if (availability == TileItemAvailability.Available)
                return true;

            failReason = ToFailReason(availability);
            return false;
        }

        /// <summary>Returns true when inventory can supply the tile; otherwise sets <paramref name="failReason"/>.</summary>
        public static bool TryCheckTileItem(
            ushort tileType,
            int style,
            in PasteContext ctx,
            out LayerFailReason failReason,
            int? baseId = null)
        {
            failReason = LayerFailReason.None;
            TileItemAvailability availability = GetTileItemAvailability(tileType, style, in ctx, baseId);
            if (availability == TileItemAvailability.Available)
                return true;

            failReason = ToFailReason(availability);
            return false;
        }

        /// <summary>Returns true when inventory can supply the wall; otherwise sets <paramref name="failReason"/>.</summary>
        public static bool TryCheckWallItem(ushort wallType, in PasteContext ctx, out LayerFailReason failReason)
        {
            failReason = LayerFailReason.None;
            TileItemAvailability availability = GetWallItemAvailability(wallType, in ctx);
            if (availability == TileItemAvailability.Available)
                return true;

            failReason = ToFailReason(availability);
            return false;
        }

        public static LayerFailReason ToFailReason(TileItemAvailability availability) =>
            availability switch
            {
                TileItemAvailability.UnresolvedInteraction => LayerFailReason.UnresolvedInteraction,
                TileItemAvailability.MissingFromInventory => LayerFailReason.MissingItems,
                _ => LayerFailReason.None,
            };

        internal static bool ProcessQueuedMutation(TileEffectSession session, TileMutation mutation)
        {
            // --- Apply or fire; surface paint reservation/apply failures so they are not silent.

            PasteContext ctx = session.ContextFactory();
            bool wantsProjectile = mutation.ProjectileMode is not null;
            int arrowsBefore = session.PendingImpactCount;

            bool result;
            if (mutation.ProjectileMode is null)
                result = ApplyMutation(mutation, in ctx);
            else
                result = PasteArrow.TryFire(session, mutation, in ctx);

            if (!result)
            {
                session.ClearLiquidPool(mutation.MutationId);
                session.ClearPlacementLedger(mutation.MutationId);
                if (mutation.Kind == TileMutationKind.ApplyPaintState)
                {
                    TABLog.Warn(
                        $"Paint mutation failed id={mutation.MutationId} at ({mutation.World.X},{mutation.World.Y}) "
                        + $"tileColor={mutation.Paint.TileColor} wallColor={mutation.Paint.WallColor} "
                        + $"echoT={mutation.Paint.TileInvisible} echoW={mutation.Paint.WallInvisible} "
                        + $"illumT={mutation.Paint.TileFullbright} illumW={mutation.Paint.WallFullbright}",
                        TABLog.TileExecutorCategory);
                }
            }

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
                    // Mute WorldGen; PasteWorldFx owns the place cue after a successful write.
                    applied = WorldGen.PlaceTile(x, y, mutation.Type, mute: true, forced: mutation.Forced, plr: ctx.Player.whoAmI, style: Math.Max(0, mutation.Style));
                    if (applied)
                        applied = TryApplyBundledPostPlaceFrame(x, y, in mutation) && applied;
                    break;
                case TileMutationKind.PlaceObject:
                    applied = TryPlaceObject(x, y, in mutation);
                    if (applied)
                        applied = TryApplyBundledPostPlaceFrame(x, y, in mutation, runPlacementHooks: false) && applied;
                    break;
                case TileMutationKind.PlaceWall:
                    // Mute WorldGen; PasteWorldFx owns the wall place cue after a successful write.
                    WorldGen.PlaceWall(x, y, mutation.Type, mute: true);
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
                    applied = ApplyLiquidMutation(mutation, in ctx);
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
                    applied = ApplySweepMutation(mutation, x, y, in ctx);
                    break;
                default:
                    applied = false;
                    break;
            }

            if (applied)
            {
                // Drop liquid pool payload after apply; MP sync uses the stamped batch square.
                if (mutation.Kind == TileMutationKind.SetLiquid
                    && ctx.EffectSession is not null)
                {
                    ctx.EffectSession.ClearLiquidPool(mutation.MutationId);
                }

                // One Dig cue for SP and all MP clients after a place/clear write succeeds.
                if (EmitsWorldFx(mutation.Kind))
                    PasteWorldFxNetworking.Notify(x, y);

                TileBatchNetSync.Send(mutation.SyncSquare, mutation.World);
            }

            TileExecutorLog.Applied(in mutation, applied);
            return applied;
        }

        /// <summary>True when a successful mutation should play paste Dig audio.</summary>
        private static bool EmitsWorldFx(TileMutationKind kind)
        {
            // --- Place/clear tile and wall writes emit Dig; other kinds stay silent.

            return kind is TileMutationKind.PlaceTile
                or TileMutationKind.PlaceWall
                or TileMutationKind.PlaceObject
                or TileMutationKind.ClearTile
                or TileMutationKind.ClearWall;
        }

        private static bool TryGetSession(in PasteContext ctx, out TileEffectSession session)
        {
            session = ctx.EffectSession!;
            if (session is not null)
                return true;

            TABLog.Warn("Enqueue rejected: no effect session on paste context", TABLog.TileExecutorCategory);
            return false;
        }

        private static bool EnqueueClear(TileMutationKind kind, Point world, int itemType, int itemStack, in PasteContext ctx)
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
                itemStack: Math.Max(1, itemStack)));
            return true;
        }

        private static bool EnqueueSimpleMutation(TileMutationKind kind, Point world, in PasteContext ctx, bool consumeItem = false)
        {
            if (!TryGetSession(in ctx, out TileEffectSession session))
                return false;

            PasteProjectileMode? mode = ShouldUsePlaceProjectile(in ctx, consumeItem) ? PasteProjectileMode.Place : null;
            session.Enqueue(TileMutation.New(
                mutationId: session.NextMutationId(),
                operationId: session.OperationId,
                kind: kind,
                world: world,
                consumeItem: consumeItem,
                projectileMode: mode));
            return true;
        }

        private static bool TryReserveForPlacementMutation(
            TileMutation mutation,
            in PasteContext ctx,
            out int placeStyle,
            out int itemType,
            out Vector2 origin,
            out bool fromChest,
            out Chest? sourceChest,
            out PlacementReservationLedger ledger)
        {
            // --- Always reserve through .itr; RequireItems only controls whether stacks are debited.

            placeStyle = mutation.Style;
            itemType = 0;
            origin = default;
            fromChest = false;
            sourceChest = null;
            ledger = new PlacementReservationLedger();
            bool requireInventory = ctx.RequireItems;

            // Paint uses PaintMutationState; reserve each needed facet via priority-ordered tools.
            if (mutation.Kind == TileMutationKind.ApplyPaintState)
                return TryReservePaintFacets(mutation, in ctx, out placeStyle, out itemType, out origin, out fromChest, out sourceChest, out ledger);

            int wireKind = WireKindFromMutationKind(mutation.Kind);
            if (wireKind >= 0)
            {
                return ctx.InventoryLookup!.TryReserveForWireMutation(
                    wireKind,
                    ctx.Player,
                    ctx.InteractionBudget,
                    out itemType,
                    out origin,
                    out fromChest,
                    out sourceChest,
                    out ledger,
                    requireInventory);
            }

            InteractionEntry? entry = ResolveInteractionForMutation(mutation, in ctx);
            if (entry is not null
                && ctx.InventoryLookup!.TryReserveForInteraction(
                    entry,
                    ctx.InteractionBudget,
                    ctx.Player,
                    out InteractionReservation reservation,
                    requireInventory))
            {
                placeStyle = reservation.PlaceStyle;
                itemType = reservation.ConsumableItemId;
                origin = reservation.Origin;
                fromChest = reservation.FromChest;
                sourceChest = reservation.SourceChest;
                ledger.Record(entry, in reservation);
                return true;
            }

            int? baseId = ResolveBaseTileIdForMutation(mutation);
            if (mutation.Kind == TileMutationKind.PlaceWall)
            {
                return ctx.InventoryLookup!.TryReserveForWallMutation(
                    mutation.Type,
                    ctx.Player,
                    ctx.InteractionBudget,
                    out itemType,
                    out origin,
                    out fromChest,
                    out sourceChest,
                    out ledger,
                    requireInventory);
            }

            if (mutation.Kind is TileMutationKind.PlaceTile or TileMutationKind.PlaceObject)
            {
                return ctx.InventoryLookup!.TryReserveForTileMutation(
                    mutation.Type,
                    mutation.Style,
                    ctx.Player,
                    ctx.InteractionBudget,
                    baseId,
                    out placeStyle,
                    out itemType,
                    out origin,
                    out fromChest,
                    out sourceChest,
                    out ledger,
                    requireInventory);
            }

            return false;
        }

        /// <summary>
        /// Reserves one priority-ordered tool per paint/coating facet, rolling back on partial failure.
        /// </summary>
        private static bool TryReservePaintFacets(
            TileMutation mutation,
            in PasteContext ctx,
            out int placeStyle,
            out int itemType,
            out Vector2 origin,
            out bool fromChest,
            out Chest? sourceChest,
            out PlacementReservationLedger ledger)
        {
            // --- Collect needed paint mains, then TryReserveForPaintKind each with undo on failure.

            placeStyle = 0;
            itemType = 0;
            origin = default;
            fromChest = false;
            sourceChest = null;
            ledger = new PlacementReservationLedger();

            List<int> paintMains = [];
            CollectPaintMainIds(mutation, paintMains);
            if (paintMains.Count == 0)
                return true;

            var completed = new List<(InteractionEntry Entry, InteractionReservation Reservation)>(paintMains.Count);
            for (int i = 0; i < paintMains.Count; i++)
            {
                if (!ctx.InventoryLookup!.TryReserveForPaintKind(
                        paintMains[i],
                        ctx.Player,
                        ctx.InteractionBudget,
                        out InteractionEntry? reservedEntry,
                        out InteractionReservation reservation,
                        requireInventory: ctx.RequireItems)
                    || reservedEntry is null)
                {
                    for (int j = completed.Count - 1; j >= 0; j--)
                    {
                        (InteractionEntry priorEntry, InteractionReservation priorReservation) = completed[j];
                        ctx.InventoryLookup.UndoInteractionReservation(priorEntry, ctx.InteractionBudget, in priorReservation);
                    }

                    return false;
                }

                completed.Add((reservedEntry, reservation));
                ledger.Record(reservedEntry, in reservation);
                placeStyle = reservation.PlaceStyle;
                itemType = reservation.ConsumableItemId;
                origin = reservation.Origin;
                fromChest = reservation.FromChest;
                sourceChest = reservation.SourceChest;
            }

            return true;
        }

        /// <summary>Collects paint-color and coating main ids needed to reach the mutation state.</summary>
        private static void CollectPaintMainIds(TileMutation mutation, List<int> paintMains)
        {
            // --- Compare live tile against PaintMutationState; one main id per differing facet.

            if (mutation.Kind != TileMutationKind.ApplyPaintState)
                return;

            int x = mutation.World.X;
            int y = mutation.World.Y;
            if (!WorldGen.InWorld(x, y))
                return;

            Tile live = Main.tile[x, y];
            PaintMutationState paint = mutation.Paint;

            if (paint.TileColor != PaintID.None && live.TileColor != paint.TileColor)
                TryAddPaintMainId(paintMains, paint.TileColor);

            if (paint.WallColor != PaintID.None && live.WallColor != paint.WallColor)
                TryAddPaintMainId(paintMains, paint.WallColor);

            if ((paint.TileInvisible && live.HasTile && !live.IsTileInvisible)
                || (paint.WallInvisible && live.WallType != WallID.None && !live.IsWallInvisible))
                TryAddPaintMainId(paintMains, ContentNameResolver.EchoCoatingId);

            if ((paint.TileFullbright && live.HasTile && !live.IsTileFullbright)
                || (paint.WallFullbright && live.WallType != WallID.None && !live.IsWallFullbright))
                TryAddPaintMainId(paintMains, ContentNameResolver.IlluminantCoatingId);
        }

        /// <summary>Appends a paint main id when it is not already queued.</summary>
        private static void TryAddPaintMainId(List<int> paintMains, int paintMainId)
        {
            if (paintMains.Contains(paintMainId))
                return;
            paintMains.Add(paintMainId);
        }

        private static InteractionEntry? ResolveInteractionForMutation(TileMutation mutation, in PasteContext ctx)
        {
            switch (mutation.Kind)
            {
                case TileMutationKind.SetLiquid:
                    return InteractionMapProvider.ResolveLiquidInteraction(mutation.Type);
                case TileMutationKind.PlaceWireRed:
                case TileMutationKind.PlaceWireGreen:
                case TileMutationKind.PlaceWireBlue:
                case TileMutationKind.PlaceWireYellow:
                case TileMutationKind.PlaceActuator:
                    return InteractionMapProvider.ResolveWireInteraction(WireKindFromMutationKind(mutation.Kind));
                default:
                    return null;
            }
        }

        private static int? ResolveBaseTileIdForMutation(TileMutation mutation)
        {
            if (mutation.Kind is not (TileMutationKind.PlaceTile or TileMutationKind.PlaceObject))
                return null;

            int x = mutation.World.X;
            int y = mutation.World.Y;
            if (!WorldGen.InWorld(x, y))
                return null;

            Tile target = Main.tile[x, y];
            return target.HasTile ? target.TileType : null;
        }

        private static int WireKindFromMutationKind(TileMutationKind kind) => kind switch
        {
            TileMutationKind.PlaceWireRed => 0,
            TileMutationKind.PlaceWireGreen => 1,
            TileMutationKind.PlaceWireBlue => 2,
            TileMutationKind.PlaceWireYellow => 3,
            TileMutationKind.PlaceActuator => 4,
            _ => -1,
        };

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
                // AbortArrow passes arrived=false explicitly; orphaned resolves pass null and still place.
                bool isAbort = arrivedAtDestination == false;
                if (isAbort)
                {
                    SettleFailedPlacement(mutation.MutationId, dropTile, in ctx);
                    actionSucceeded = false;
                }
                else
                {
                    actionSucceeded = ApplyMutation(mutation with { ProjectileMode = null }, in ctx);
                    if (actionSucceeded)
                        CommitPlacementReservation(mutation.MutationId, in ctx);
                    else
                        SettleFailedPlacement(mutation.MutationId, dropTile, in ctx);
                }

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

        /// <summary>Commits in-flight place ownership after a successful arrival apply.</summary>
        private static void CommitPlacementReservation(int mutationId, in PasteContext ctx)
        {
            // --- Keep budget spends; discard ownership metadata and any unused pool payload.
            if (ctx.EffectSession is null)
                return;

            ctx.EffectSession.ClearPlacementLedger(mutationId);
            ctx.EffectSession.ClearLiquidPool(mutationId);
        }

        /// <summary>
        /// Drops only stacks that were genuinely decremented and rolls back aborted yield spends.
        /// </summary>
        private static void SettleFailedPlacement(int mutationId, Point dropTile, in PasteContext ctx)
        {
            // --- Refund via world drops for real debits; restore free/stack budget slots in reverse order.
            if (ctx.EffectSession is null)
                return;

            ctx.EffectSession.ClearLiquidPool(mutationId);
            if (!ctx.EffectSession.TryTakePlacementLedger(mutationId, out PlacementReservationLedger ledger))
                return;

            if (ledger.DebitedStackCount > 0 && ledger.DebitedItemType > 0)
                InventoryManager.DropItemAtTile(ledger.DebitedItemType, ledger.DebitedStackCount, dropTile);

            ledger.RollbackBudget(ctx.InteractionBudget);
            TABLog.Debug(
                $"SettleFailedPlacement mutation={mutationId} debitedItem={ledger.DebitedItemType} "
                + $"debitedStack={ledger.DebitedStackCount} freeYield={ledger.FreeYieldSpendCount} "
                + $"drop=({dropTile.X},{dropTile.Y})",
                TABLog.TileExecutorCategory);
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
            // --- Always reserve through .itr (zero-count when !RequireItems); abort when unmapped.

            if (ctx.InventoryLookup is null)
                return false;

            int placeStyle = mutation.Style;
            int itemType;
            Vector2 origin;
            bool fromChest = false;
            Chest? sourceChest = null;
            PlacementReservationLedger ledger = default;
            bool debitStacks = ShouldConsumeOnPlace(in ctx, mutation.ConsumeItem);

            if (mutation.Kind == TileMutationKind.SetLiquid
                && session.TryGetLiquidPool(mutation.MutationId, out LiquidPoolCell[] poolCells)
                && poolCells.Length > 0)
            {
                // --- Reserve the selected liquid tool once per pool cell, then fire one shared arrow.
                if (!ctx.InventoryLookup.TryReserveForLiquidPool(
                        mutation.Type,
                        poolCells.Length,
                        ctx.Player,
                        ctx.InteractionBudget,
                        out ledger,
                        out origin,
                        out fromChest,
                        out sourceChest,
                        requireInventory: ctx.RequireItems))
                    return false;

                itemType = ledger.VisualItemType > 0 ? ledger.VisualItemType : 0;
                TABLog.Debug(
                    $"FireLiquidPool mutation={mutation.MutationId} cells={poolCells.Length} visual={itemType} "
                    + $"debited={ledger.DebitedStackCount} freeYield={ledger.FreeYieldSpendCount} "
                    + $"requireInventory={ctx.RequireItems}",
                    TABLog.TileExecutorCategory);
            }
            else if (!TryReserveForPlacementMutation(
                mutation,
                in ctx,
                out placeStyle,
                out itemType,
                out origin,
                out fromChest,
                out sourceChest,
                out ledger))
            {
                return false;
            }

            if (ledger.VisualItemType > 0)
                itemType = ledger.VisualItemType;

            // Unmapped or empty visual means .itr failed to resolve a place item.
            if (itemType <= 0)
                return false;

            var impactMutation = mutation with
            {
                // Keep ApplyPaintState.Paint intact; Style is only for tile/object placement.
                Style = mutation.Kind == TileMutationKind.ApplyPaintState ? mutation.Style : placeStyle,
                ItemType = itemType,
                ItemStack = debitStacks ? Math.Max(1, ledger.DebitedStackCount) : 0,
                ConsumeItem = debitStacks && mutation.ConsumeItem,
            };

            session.AttachPlacementLedger(impactMutation.MutationId, ledger);

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
                detail: debitStacks
                    ? $"eta={arriveBy - firedAt}"
                    : $"zeroCount eta={arriveBy - firedAt}");
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
            // Default to the cleared tile; only fly to chest/player when an adjacent source resolves.
            Vector2 destination = InventoryManager.GetTileWorldCenter(mutation.World);
            if (ctx.InventoryLookup.TryResolveStoreDestinationForItem(
                    mutation.ItemType,
                    ctx.Player,
                    out Vector2 resolvedDestination,
                    out inventoryIndex))
            {
                destination = resolvedDestination;
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

        /// <summary>Spawns a PasteArrow owned by the server in multiplayer so AI and impact resolve authoritatively.</summary>
        internal static bool SpawnProjectile(
            TileMutation mutation,
            Vector2 origin,
            Vector2 destination,
            int ownerPlayerId,
            out int projectileIndex)
        {
            // --- NewProjectile with server owner (255) on NetmodeID.Server; otherwise the requesting player.

            projectileIndex = -1;
            int projType = ModContent.ProjectileType<PasteArrow>();
            int itemType = Math.Max(0, mutation.ItemType);

            // Client-owned arrows run AI on the client; OnKill there never completes the place.
            int projectileOwner = Main.netMode == NetmodeID.Server ? 255 : ownerPlayerId;

            int projIndex = Projectile.NewProjectile(
                new EntitySource_Misc("TAB.TileMutation"),
                origin.X,
                origin.Y,
                0f,
                0f,
                projType,
                itemType,
                0f,
                projectileOwner,
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

            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.SyncProjectile, -1, -1, null, projIndex);

            projectileIndex = projIndex;
            return true;
        }

        private static bool ApplyClearTile(int x, int y, TileMutation mutation, in PasteContext ctx)
        {
            // --- Mute KillTile Dig; PasteWorldFx Notify owns the clear cue after apply.

            InventoryLookup? inventory = ctx.InventoryLookup;
            bool result = false;
            SuppressTileSound.Run(() =>
            {
                switch (mutation.ItemDropStyle)
                {
                    case ClearItemDropStyle.Void:
                        SuppressTileDrop.KillTile(x, y);
                        result = true;
                        break;
                    case ClearItemDropStyle.Drop:
                        WorldGen.KillTile(x, y, fail: false, effectOnly: false, noItem: false);
                        result = true;
                        break;
                    case ClearItemDropStyle.Store:
                        SuppressTileDrop.KillTile(x, y);
                        if (mutation.ItemType > 0)
                            inventory?.ApplyStoreOnArrival(mutation.ItemType, mutation.ItemStack, mutation.World);
                        result = true;
                        break;
                }
            });
            return result;
        }

        private static bool ApplyClearWall(int x, int y, TileMutation mutation, in PasteContext ctx)
        {
            // --- Mute KillWall Dig; PasteWorldFx Notify owns the clear cue after apply.

            InventoryLookup? inventory = ctx.InventoryLookup;
            bool result = false;
            SuppressTileSound.Run(() =>
            {
                switch (mutation.ItemDropStyle)
                {
                    case ClearItemDropStyle.Void:
                        SuppressTileDrop.KillWall(x, y);
                        result = true;
                        break;
                    case ClearItemDropStyle.Drop:
                        WorldGen.KillWall(x, y, fail: false);
                        result = true;
                        break;
                    case ClearItemDropStyle.Store:
                        SuppressTileDrop.KillWall(x, y);
                        if (mutation.ItemType > 0)
                            inventory?.ApplyStoreOnArrival(mutation.ItemType, mutation.ItemStack, mutation.World);
                        result = true;
                        break;
                }
            });
            return result;
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

        /// <summary>Applies tile/wall colors and coatings from the mutation paint state.</summary>
        private static bool ApplyPaintStateMutation(int x, int y, TileMutation mutation)
        {
            // --- Apply tile/wall colors and coatings; require a live wall when wall paint is requested.

            Tile live = Main.tile[x, y];
            PaintMutationState paint = mutation.Paint;
            byte tileColor = paint.TileColor;
            byte wallColor = paint.WallColor;
            bool tileInvisible = paint.TileInvisible;
            bool wallInvisible = paint.WallInvisible;
            bool tileFullbright = paint.TileFullbright;
            bool wallFullbright = paint.WallFullbright;

            bool wantsWallPaint = paint.WantsWallAppearance;
            if (wantsWallPaint && live.WallType == WallID.None)
            {
                TABLog.Debug(
                    $"ApplyPaint skipped wall missing at ({x},{y}) wallColor={wallColor}",
                    TABLog.TileExecutorCategory);
                return false;
            }

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
            bool wallOk = !wantsWallPaint
                || (result.WallType != WallID.None
                    && result.WallColor == wallColor
                    && wall.Invisible == wallInvisible
                    && wall.FullBright == wallFullbright);
            return tileOk && wallOk;
        }

        private static bool ApplyLiquidMutation(TileMutation mutation, in PasteContext ctx)
        {
            // --- Write every attached pool cell, or fall back to the single-cell mutation fields.
            if (ctx.EffectSession is not null
                && ctx.EffectSession.TryGetLiquidPool(mutation.MutationId, out LiquidPoolCell[] poolCells)
                && poolCells.Length > 0)
            {
                bool allOk = true;
                for (int i = 0; i < poolCells.Length; i++)
                {
                    LiquidPoolCell cell = poolCells[i];
                    if (!ApplyLiquidCell(cell.World.X, cell.World.Y, cell.LiquidType, cell.Amount))
                        allOk = false;
                }

                return allOk;
            }

            return ApplyLiquidCell(
                mutation.World.X,
                mutation.World.Y,
                Math.Clamp((int)mutation.Type, 0, 255),
                (byte)Math.Clamp(mutation.Style, 0, byte.MaxValue));
        }

        private static bool ApplyLiquidCell(int x, int y, int liquidType, byte amount)
        {
            if (amount == 0)
            {
                if (Main.tile[x, y].LiquidAmount == 0)
                    return true;

                SuppressTileDrop.EmptyLiquid(x, y);
                return Main.tile[x, y].LiquidAmount == 0;
            }

            return TryWriteLiquid(x, y, liquidType, amount);
        }

        /// <summary>Places a multitile and runs HookPostPlaceMyPlayer so chests and TileEntities are created.</summary>
        private static bool TryPlaceObject(int x, int y, in TileMutation mutation)
        {
            // --- Player PlaceThing sequence: CanPlace, Place tiles, then CallPostPlacementPlayerHook.

            int type = mutation.Type;
            int style = Math.Max(0, mutation.Style);
            int direction = mutation.Direction;

            // CanPlace keeps PlaceRight when dir=1 and PlaceLeft when dir=-1; it does not take Alternate.
            if (!TileObject.CanPlace(x, y, type, style, direction, out TileObject objectData))
                return false;

            objectData.random = -1;
            objectData.alternate = mutation.Alternate;
            if (!TileObject.Place(objectData))
                return false;

            // TileObject.Place writes tiles only. This hook is what creates Chest / Sign / TE.
            // Do not SquareTileFrame here: framing can destroy the just-created host without re-running the hook.
            TileObjectData.CallPostPlacementPlayerHook(
                x,
                y,
                type,
                style,
                direction,
                mutation.Alternate,
                objectData);

            Tile tile = Main.tile[x, y];
            return tile.HasTile && tile.TileType == type;
        }

        /// <summary>Writes captured origin frames after PlaceTile/PlaceObject, optionally replaying TileFrame hooks.</summary>
        private static bool TryApplyBundledPostPlaceFrame(
            int x,
            int y,
            in TileMutation mutation,
            bool runPlacementHooks = true)
        {
            // --- Skip when the mutation has no bundled frames; otherwise write X/Y and optionally TileFrame.

            if (!mutation.HasPostPlaceFrame)
                return true;

            return ApplyTileFrame(x, y, mutation.PostPlaceFrameX, mutation.PostPlaceFrameY, runPlacementHooks);
        }

        private static bool ApplyFrameMutation(int x, int y, TileMutation mutation) =>
            ApplyTileFrame(
                x,
                y,
                (short)Math.Clamp(mutation.Style, short.MinValue, short.MaxValue),
                (short)Math.Clamp(mutation.Alternate, short.MinValue, short.MaxValue));

        /// <summary>Writes TileFrameX/Y. Optionally runs TileLoader.TileFrame, which can recreate PlaceObject hosts.</summary>
        private static bool ApplyTileFrame(int x, int y, short frameX, short frameY, bool runPlacementHooks = true)
        {
            // --- Assign captured frames, then TileFrame only when PlaceTile still needs the post-place hook.

            Tile tile = Main.tile[x, y];
            if (!tile.HasTile)
                return false;

            // Overwrite the origin cell frames with the payload snapshot.
            TileWallWireStateData state = tile.Get<TileWallWireStateData>();
            state.TileFrameX = frameX;
            state.TileFrameY = frameY;
            tile.Get<TileWallWireStateData>() = state;

            // PlaceObject writes tiles only; TileFrame would not create the missing Chest/TE host.
            if (runPlacementHooks)
                TileLoaderSupport.RunPostPlacementHooks(x, y);

            return tile.HasTile
                && tile.TileFrameX == frameX
                && tile.TileFrameY == frameY;
        }

        /// <summary>Writes payload tile type and frames only; skips TileFrame hooks so tree branch geometry stays exact.</summary>
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

        private static bool ApplySweepMutation(TileMutation mutation, int x, int y, in PasteContext ctx)
        {
            PayloadCell payload = mutation.Payload;
            if (!payload.IsEmpty
                && payload.Detail.HasTile
                && TreeLayer.IsTreeType((ushort)payload.Block.Type))
            {
                if (!ctx.IsSweep
                    && ctx.Lookup is PasteTileLookup lookup
                    && lookup.TryGetPasteTileByWorld(mutation.World, out PasteTile pasteTile)
                    && !TreeLayer.CanPlaceSegment(in pasteTile, in ctx))
                {
                    return false;
                }

                return ApplyPayloadBlockMutation(x, y, mutation) && WorldGen.InWorld(x, y);
            }

            // Remap unknown tile/wall ids to tML Unloaded placeholders before the world write.
            RemapUnknownTypesForSweep(payload).CopyToWorldTile(x, y);
            return WorldGen.InWorld(x, y);
        }

        /// <summary>Clones the payload cell when unknown tile/wall ids must become Unloaded placeholders.</summary>
        private static PayloadCell RemapUnknownTypesForSweep(PayloadCell payload)
        {
            // --- Leave known and already-unloaded content alone; clone only when a remap is required.
            if (payload.IsEmpty || payload.Data is null)
                return payload;

            ushort tileType = payload.Block.Type;
            ushort wallType = payload.Wall.Type;
            bool remapTile = payload.Detail.HasTile
                && TileLoaderSupport.IsUnknownTileType(tileType)
                && TileLoaderSupport.ResolveUnloadableTileType(tileType) != tileType;
            bool remapWall = TileLoaderSupport.IsUnknownWallType(wallType)
                && TileLoaderSupport.ResolveUnloadableWallType(wallType) != wallType;
            if (!remapTile && !remapWall)
                return payload;

            // Clone TileData so the projection payload snapshot stays unchanged.
            PayloadCell.TileData source = payload.Data;
            var data = new PayloadCell.TileData
            {
                TileType = source.TileType,
                WallType = source.WallType,
                LiquidData = source.LiquidData,
                TWBIData = source.TWBIData,
                TWWStateData = source.TWWStateData,
                TileStyle = source.TileStyle,
                TileAlternate = source.TileAlternate,
            };
            if (remapTile)
                data.TileType.Type = TileLoaderSupport.ResolveUnloadableTileType(tileType);
            if (remapWall)
                data.WallType.Type = TileLoaderSupport.ResolveUnloadableWallType(wallType);
            return PayloadCell.FromData(data);
        }

        private static bool ShouldUsePlaceProjectile(in PasteContext ctx, bool consumeItem) =>
            !ctx.IsSweep;

        private static bool ShouldConsumeOnPlace(in PasteContext ctx, bool consumeItem) =>
            consumeItem && ctx.RequireItems && ctx.InventoryLookup is not null && !ctx.IsSweep;

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

        /// <summary>Places liquid via WorldGen; fails closed when PlaceLiquid rejects the cell.</summary>
        private static bool TryWriteLiquid(int x, int y, int liquidType, byte amount)
        {
            // --- Trust PlaceLiquid for solid checks, merges, framing, and sendWater; no LiquidData fallback.

            if (!WorldGen.InWorld(x, y))
                return false;

            // PlaceLiquid owns placement failure, merge tiles, SquareTileFrame, and MP water sync.
            return WorldGen.PlaceLiquid(x, y, (byte)liquidType, amount);
        }

        private static void ResolveStoreDropForClearTile(int x, int y, ClearItemDropStyle style, out int itemType, out int itemStack)
        {
            // --- Resolve break loot for Store clears; warn when a live tile yields nothing to credit.

            itemType = 0;
            itemStack = 1;
            if (style != ClearItemDropStyle.Store || !Main.tile[x, y].HasTile)
                return;

            Tile tile = Main.tile[x, y];
            int tileType = tile.TileType;
            TileBreakDropResolver.TryResolveTileStoreDrop(x, y, out itemType, out itemStack);
            if (itemType <= 0)
            {
                TABLog.Debug(
                    $"StoreClear tile=({x},{y}) type={tileType} item=0 mode=void-only",
                    TABLog.TileExecutorCategory);
                return;
            }

            TABLog.Debug(
                $"StoreClear tile=({x},{y}) type={tileType} item={itemType} stack={itemStack}",
                TABLog.TileExecutorCategory);
        }

        private static void ResolveStoreDropForClearWall(int x, int y, ClearItemDropStyle style, out int itemType, out int itemStack)
        {
            // --- Resolve wall break loot for Store clears; warn when a live wall yields nothing to credit.

            itemType = 0;
            itemStack = 1;
            if (style != ClearItemDropStyle.Store)
                return;

            int wallType = Main.tile[x, y].WallType;
            TileBreakDropResolver.TryResolveWallStoreDrop(x, y, out itemType, out itemStack);
            if (wallType <= 0)
                return;

            if (itemType <= 0)
            {
                TABLog.Debug(
                    $"StoreClear wall=({x},{y}) type={wallType} item=0 mode=void-only",
                    TABLog.TileExecutorCategory);
                return;
            }

            TABLog.Debug(
                $"StoreClear wall=({x},{y}) type={wallType} item={itemType} stack={itemStack}",
                TABLog.TileExecutorCategory);
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
