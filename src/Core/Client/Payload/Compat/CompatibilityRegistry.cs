using System;
using System.Collections.Generic;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using TheArchitectsBlueprintMod.Core.Compat.Adapters;

namespace TheArchitectsBlueprintMod.Core.Compat
{
    /// <summary>Priority-ordered registry that dispatches payload entity compat hooks.</summary>
    internal static class CompatibilityRegistry
    {
        private static readonly List<ICompatibilityAdapter> Adapters = new();
        private static bool _sorted;

        /// <summary>Registers or replaces an adapter by <see cref="ICompatibilityAdapter.Id"/>.</summary>
        public static void Register(ICompatibilityAdapter adapter)
        {
            // --- Upsert by Id, then mark the priority list dirty.

            ArgumentNullException.ThrowIfNull(adapter);

            for (int i = 0; i < Adapters.Count; i++)
            {
                if (string.Equals(Adapters[i].Id, adapter.Id, StringComparison.Ordinal))
                {
                    Adapters[i] = adapter;
                    _sorted = false;
                    return;
                }
            }

            Adapters.Add(adapter);
            _sorted = false;
        }

        /// <summary>Builds a context for a phase, preferring document mods when supplied.</summary>
        public static CompatibilityContext CreateSessionContext(
            CompatibilityPhase phase,
            IReadOnlyList<string>? modsLoaded = null)
        {
            // --- Use document modsLoaded when present; otherwise snapshot the live mod list.
            IReadOnlyList<string> mods = modsLoaded is { Count: > 0 }
                ? modsLoaded
                : CollectCurrentModsLoaded();
            return new CompatibilityContext(phase, mods);
        }

        /// <summary>Collects currently loaded mod internal names.</summary>
        public static IReadOnlyList<string> CollectCurrentModsLoaded()
        {
            // --- Mirror export metadata: walk ModLoader.Mods and keep non-empty names.

            var mods = new List<string>();
            try
            {
                for (int i = 0; i < ModLoader.Mods.Length; i++)
                {
                    Mod? m = ModLoader.Mods[i];
                    if (m is not null && !string.IsNullOrEmpty(m.Name))
                        mods.Add(m.Name);
                }
            }
            catch
            {
                // Early load can throw; treat as empty session list.
            }

            return mods;
        }

        /// <summary>Returns true when an available adapter claims <paramref name="fullName"/>.</summary>
        public static bool Claims(string? fullName)
        {
            // --- Probe Handles without SaveData so copy/import can skip unclaimed mods.

            if (string.IsNullOrEmpty(fullName))
                return false;

            EnsureReady();
            CompatibilityContext ctx = CreateSessionContext(CompatibilityPhase.Sanitize);
            ctx.FullName = fullName;

            foreach (ICompatibilityAdapter adapter in Enumerate(ctx))
                return true;

            return false;
        }

        /// <summary>Dispatches copy through available adapters; Vanilla is the final fallback.</summary>
        public static TagCompound? DispatchCopy(CompatibilityContext ctx, TileEntity te)
        {
            // --- Walk priority order; first TryCopy that returns true owns the result.

            EnsureReady();
            ctx.Phase = CompatibilityPhase.Copy;
            ctx.LiveTileEntity = te;

            foreach (ICompatibilityAdapter adapter in Enumerate(ctx))
            {
                if (adapter.TryCopy(ctx, te, out TagCompound? data))
                    return data;
            }

            return null;
        }

        /// <summary>Dispatches sanitize through available adapters; Vanilla is the final fallback.</summary>
        public static TagCompound? DispatchSanitize(CompatibilityContext ctx, TagCompound data)
        {
            // --- Walk priority order; first TrySanitize that returns true owns the cleaned tag.

            EnsureReady();
            ctx.Phase = CompatibilityPhase.Sanitize;

            foreach (ICompatibilityAdapter adapter in Enumerate(ctx))
            {
                if (adapter.TrySanitize(ctx, data, out TagCompound? cleaned))
                    return cleaned;
            }

            return null;
        }

        /// <summary>Dispatches paste/apply through available adapters; Vanilla is the final fallback.</summary>
        public static bool DispatchPaste(CompatibilityContext ctx, int worldX, int worldY, TagCompound data)
        {
            // --- Walk priority order; first handler that claims the call reports applied.

            EnsureReady();
            ctx.Phase = CompatibilityPhase.Paste;
            ctx.WorldX = worldX;
            ctx.WorldY = worldY;

            foreach (ICompatibilityAdapter adapter in Enumerate(ctx))
            {
                if (adapter.TryPaste(ctx, worldX, worldY, data, out bool applied))
                    return applied;
            }

            return false;
        }

        /// <summary>Ensures built-in adapters are registered and sorted by priority descending.</summary>
        private static void EnsureReady()
        {
            // --- Lazy-register Terraria when Load has not run yet, then sort once.

            if (Adapters.Count == 0)
                Register(new TerrariaCopyPasteAdapter());

            if (_sorted)
                return;

            Adapters.Sort(static (a, b) => b.Priority.CompareTo(a.Priority));
            _sorted = true;
        }

        /// <summary>Yields available adapters that claim the current context.</summary>
        private static IEnumerable<ICompatibilityAdapter> Enumerate(CompatibilityContext ctx)
        {
            // --- Priority order already applied; filter on availability and Handles.

            for (int i = 0; i < Adapters.Count; i++)
            {
                ICompatibilityAdapter adapter = Adapters[i];
                if (!adapter.IsAvailable(ctx))
                    continue;
                if (!adapter.Handles(ctx))
                    continue;
                yield return adapter;
            }
        }
    }
}
