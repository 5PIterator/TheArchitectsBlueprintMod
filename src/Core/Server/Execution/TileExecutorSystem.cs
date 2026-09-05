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
        private static readonly List<int> SessionKeyScratch = [];

        internal static void RegisterSession(TileEffectSession session)
        {
            if (session is null)
                return;

            Sessions[session.OperationId] = session;
        }

        internal static void UnregisterSession(int operationId)
        {
            if (Sessions.TryGetValue(operationId, out TileEffectSession? session))
            {
                TileExecutor.TerminateSession(session);
                session.Clear();
            }

            Sessions.Remove(operationId);
        }

        internal static bool TryCompleteProjectileImpact(
            int operationId,
            int arrowId,
            bool arrivedAtDestination,
            Point dropTile,
            out bool actionSucceeded)
        {
            actionSucceeded = false;
            if (!Sessions.TryGetValue(operationId, out TileEffectSession? session))
                return false;

            bool marked = session.TryMarkArrowArrived(arrowId, arrivedAtDestination, dropTile);
            actionSucceeded = marked;
            return marked;
        }

        internal static bool TryGetProjectileDestination(int operationId, int arrowId, out Vector2 destination)
        {
            destination = default;
            if (!Sessions.TryGetValue(operationId, out TileEffectSession? session))
                return false;

            return session.TryGetArrowDestination(arrowId, out destination);
        }

        internal static bool TryGetProjectileItemType(int operationId, int arrowId, out int itemType)
        {
            itemType = 0;
            if (!Sessions.TryGetValue(operationId, out TileEffectSession? session))
                return false;

            return session.TryGetArrowItemType(arrowId, out itemType);
        }

        internal static bool TryGetArrowFlightProfile(int operationId, int arrowId, out ArrowFlightProfile profile)
        {
            profile = default;
            if (!Sessions.TryGetValue(operationId, out TileEffectSession? session))
                return false;

            return session.TryGetArrowFlightProfile(arrowId, out profile);
        }

        internal static void ProcessSessions()
        {
            if (Main.gameMenu || Sessions.Count == 0)
                return;

            SessionKeyScratch.Clear();
            foreach (int key in Sessions.Keys)
                SessionKeyScratch.Add(key);

            for (int i = 0; i < SessionKeyScratch.Count; i++)
            {
                if (!Sessions.TryGetValue(SessionKeyScratch[i], out TileEffectSession? session))
                    continue;

                while (session.Pending.Count > 0)
                {
                    TileMutation next = session.Pending.Peek();
                    if (next.ProjectileMode is not null && !session.CanLaunchArrow())
                        break;

                    if (!session.TryDequeue(out TileMutation mutation))
                        break;

                    TileExecutor.ProcessQueuedMutation(session, mutation);
                }

                PasteArrow.ProcessActive(session);
            }
        }

        public override void OnWorldUnload()
        {
            foreach (TileEffectSession session in Sessions.Values)
                TileExecutor.TerminateSession(session);

            Sessions.Clear();
            SessionKeyScratch.Clear();
            TileEffectSession.ResetOperationIdSequence();
        }
    }
}
