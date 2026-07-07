using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Content.Projectiles;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Per-operation queue and in-flight projectile tracking for tile effects.</summary>
    internal sealed class TileEffectSession
    {
        private int _nextMutationId = 1;

        public TileEffectSession(int operationId, Func<PasteContext> contextFactory)
        {
            OperationId = operationId;
            ContextFactory = contextFactory;
        }

        public int OperationId { get; }

        public Queue<TileMutation> Pending { get; } = new();

        public Dictionary<int, TileMutation> PendingImpacts { get; } = new();

        public Func<PasteContext> ContextFactory { get; }

        public int InFlightProjectiles { get; private set; }

        public bool IsIdle => Pending.Count == 0 && InFlightProjectiles == 0;

        public int NextMutationId() => _nextMutationId++;

        public void Enqueue(TileMutation mutation)
        {
            Pending.Enqueue(mutation);
            TileExecutorLog.Registered(in mutation);
        }

        public bool TryDequeue(out TileMutation mutation)
        {
            if (Pending.Count == 0)
            {
                mutation = default;
                return false;
            }

            mutation = Pending.Dequeue();
            return true;
        }

        public void TrackImpact(TileMutation mutation)
        {
            PendingImpacts[mutation.MutationId] = mutation;
            InFlightProjectiles++;
        }

        public bool TryTakeImpact(int mutationId, out TileMutation mutation)
        {
            if (!PendingImpacts.TryGetValue(mutationId, out mutation))
                return false;

            PendingImpacts.Remove(mutationId);
            InFlightProjectiles = Math.Max(0, InFlightProjectiles - 1);
            return true;
        }

        public void Clear()
        {
            Pending.Clear();
            PendingImpacts.Clear();
            InFlightProjectiles = 0;
        }

        /// <summary>Completes tracked impacts whose projectile was killed before arrival (slot reuse).</summary>
        internal void CompleteOrphanedImpacts()
        {
            if (PendingImpacts.Count == 0)
                return;

            int projType = ModContent.ProjectileType<PasteTileProjectile>();
            var orphaned = new List<int>();

            foreach (KeyValuePair<int, TileMutation> entry in PendingImpacts)
            {
                if (!HasActiveProjectile(projType, OperationId, entry.Key))
                    orphaned.Add(entry.Key);
            }

            for (int i = 0; i < orphaned.Count; i++)
                TileExecutor.CompleteProjectileImpact(this, orphaned[i]);
        }

        private static bool HasActiveProjectile(int projType, int operationId, int mutationId)
        {
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile projectile = Main.projectile[i];
                if (!projectile.active || projectile.type != projType)
                    continue;

                if ((int)projectile.ai[1] == operationId && (int)projectile.ai[2] == mutationId)
                    return true;
            }

            return false;
        }
    }
}
