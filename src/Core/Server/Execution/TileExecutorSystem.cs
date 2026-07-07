using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Content.Projectiles;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Processes queued tile mutations and projectile completions for active paste operations.</summary>
    public sealed class TileExecutorSystem : ModSystem
    {
        private static readonly Dictionary<int, TileEffectSession> Sessions = new();

        internal static void RegisterSession(TileEffectSession session)
        {
            if (session is null)
                return;

            Sessions[session.OperationId] = session;
        }

        internal static void UnregisterSession(int operationId)
        {
            if (Sessions.TryGetValue(operationId, out TileEffectSession? session))
                session.Clear();
            Sessions.Remove(operationId);
        }

        internal static bool TryCompleteProjectileImpact(
            int operationId,
            int mutationId,
            bool arrivedAtDestination,
            Point dropTile,
            out bool actionSucceeded)
        {
            actionSucceeded = false;
            if (!Sessions.TryGetValue(operationId, out TileEffectSession? session))
                return false;

            return TileExecutor.CompleteProjectileImpact(
                session,
                mutationId,
                arrivedAtDestination,
                dropTile,
                out actionSucceeded);
        }

        internal static bool TryGetProjectileDestination(int operationId, int mutationId, out Vector2 destination)
        {
            destination = default;
            if (!Sessions.TryGetValue(operationId, out TileEffectSession? session))
                return false;

            if (!session.PendingImpacts.TryGetValue(mutationId, out TileMutation mutation))
                return false;

            PasteContext ctx = session.ContextFactory();
            if (mutation.ProjectileMode == PasteProjectileMode.Place)
            {
                destination = InventoryManager.GetTileWorldCenter(mutation.World);
                return true;
            }

            if (ctx.InventoryLookup is not null
                && mutation.TargetInventoryIndex >= 0
                && ctx.InventoryLookup.TryGetInventory(mutation.TargetInventoryIndex, out Inventory inventory))
            {
                destination = InventoryManager.GetInventoryWorldCenter(
                    inventory,
                    ctx.Player);
                return true;
            }

            return ctx.InventoryLookup is not null
                && ctx.InventoryLookup.TryResolveStoreDestination(ctx.Player, out destination);
        }

        internal static void ProcessSessions()
        {
            if (Main.gameMenu || Sessions.Count == 0)
                return;

            var keys = new List<int>(Sessions.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                if (!Sessions.TryGetValue(keys[i], out TileEffectSession? session))
                    continue;

                while (session.TryDequeue(out TileMutation mutation))
                    TileExecutor.ProcessQueuedMutation(session, mutation);

                session.CompleteOrphanedImpacts();
            }
        }

        public override void OnWorldUnload() => Sessions.Clear();
    }
}
