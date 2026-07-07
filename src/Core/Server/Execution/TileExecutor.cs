using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;
using TheArchitectsBlueprintMod.Content.Projectiles;
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
                sweepPayload: tile.PayloadCell));
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
            int impactsBefore = session.PendingImpacts.Count;

            bool result;
            if (mutation.ProjectileMode is null)
                result = ApplyMutation(mutation, in ctx);
            else if (mutation.ProjectileMode == PasteProjectileMode.Place)
                result = SpawnPlaceProjectile(session, mutation, in ctx);
            else
                result = SpawnClearProjectile(session, mutation, in ctx);

            if (wantsProjectile && session.PendingImpacts.Count == impactsBefore)
                TileExecutorLog.Projectile(in mutation, fired: false);

            return result;
        }

        internal static bool CompleteProjectileImpact(
            TileEffectSession session,
            int mutationId,
            bool arrivedAtDestination,
            Point dropTile,
            out bool actionSucceeded)
        {
            actionSucceeded = false;
            PasteContext ctx = session.ContextFactory();
            if (!session.TryTakeImpact(mutationId, out TileMutation mutation))
                return false;
            if (dropTile == default)
                dropTile = mutation.World;

            if (mutation.ProjectileMode == PasteProjectileMode.Place)
            {
                actionSucceeded = arrivedAtDestination && ApplyMutation(mutation, in ctx);
                if (!actionSucceeded)
                    InventoryManager.DropItemAtTile(mutation.ItemType, mutation.ItemStack, dropTile);
                return true;
            }

            if (!arrivedAtDestination || ctx.InventoryLookup is null)
            {
                InventoryManager.DropItemAtTile(mutation.ItemType, mutation.ItemStack, dropTile);
                return true;
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

            return true;
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
                SyncTileSquare(x, y);

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

        private static bool SpawnPlaceProjectile(TileEffectSession session, TileMutation mutation, in PasteContext ctx)
        {
            if (!ShouldConsumeOnPlace(in ctx, mutation.ConsumeItem))
            {
                return ApplyMutation(mutation with { ProjectileMode = null }, in ctx);
            }

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
            session.TrackImpact(impactMutation);
            if (!SpawnProjectile(impactMutation, origin, InventoryManager.GetTileWorldCenter(mutation.World)))
            {
                if (!session.TryTakeImpact(impactMutation.MutationId, out _))
                    return false;
                InventoryManager.DropItemAtTile(itemType, 1, WorldToTile(origin));
                return false;
            }
            if (fromChest && sourceChest is not null)
                InventoryManager.PlayChestEatAnimation(sourceChest);
            return true;
        }

        private static bool SpawnClearProjectile(TileEffectSession session, TileMutation mutation, in PasteContext ctx)
        {
            if (mutation.ItemType <= 0 || ctx.InventoryLookup is null)
            {
                return ApplyMutation(mutation with { ProjectileMode = null }, in ctx);
            }

            if (!ApplyMutation(mutation with { ItemDropStyle = ClearItemDropStyle.Void, ProjectileMode = null }, in ctx))
                return false;

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
            session.TrackImpact(trackedMutation);
            if (!SpawnProjectile(trackedMutation, InventoryManager.GetTileWorldCenter(mutation.World), destination))
            {
                if (!session.TryTakeImpact(trackedMutation.MutationId, out _))
                    return false;
                InventoryManager.DropItemAtTile(trackedMutation.ItemType, trackedMutation.ItemStack, trackedMutation.World);
                return false;
            }
            return true;
        }

        private static bool SpawnProjectile(TileMutation mutation, Vector2 origin, Vector2 destination)
        {
            int projType = ModContent.ProjectileType<PasteTileProjectile>();
            int projIndex = Projectile.NewProjectile(
                new EntitySource_Misc("TAB.TileMutation"),
                origin.X,
                origin.Y,
                0f,
                0f,
                projType,
                0,
                0f,
                Main.myPlayer,
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
            p.netUpdate = true;
            if (p.ModProjectile is PasteTileProjectile tabProj)
                tabProj.AttachHeldItem(mutation.ItemType, mutation.ItemStack);

            TileExecutorLog.Projectile(in mutation, fired: true);
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

        private static bool ApplySweepMutation(TileMutation mutation, int x, int y)
        {
            mutation.SweepPayload.CopyToWorldTile(x, y);
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

        private static Point WorldToTile(Vector2 worldPosition) =>
            new((int)(worldPosition.X / 16f), (int)(worldPosition.Y / 16f));

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
