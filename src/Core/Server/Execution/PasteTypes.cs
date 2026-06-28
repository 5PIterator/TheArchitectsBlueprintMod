using System.Collections.Generic;
using TheArchitectsBlueprintMod.Config;
using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Optional client preference hints attached to a paste request.</summary>
    internal readonly struct PasteClientHints(
        bool retryOnMissingItems,
        bool retryOnInvalidPlacement,
        ClearPhaseStrategy clearStrategy,
        SweepPhaseStrategy sweepStrategy)
    {
        public bool RetryOnMissingItems { get; } = retryOnMissingItems;
        public bool RetryOnInvalidPlacement { get; } = retryOnInvalidPlacement;
        public ClearPhaseStrategy ClearStrategy { get; } = clearStrategy;
        public SweepPhaseStrategy SweepStrategy { get; } = sweepStrategy;

        /// <summary>Reads local client ModConfig values for outbound paste requests.</summary>
        public static PasteClientHints FromClientConfig()
        {
            var client = ModContent.GetInstance<ClientPasteConfig>();
            return new PasteClientHints(
                client.Place.RetryOnMissingItems,
                client.Place.RetryOnInvalidPlacement,
                client.Clear.Strategy,
                client.Sweep.Strategy);
        }
    }

    /// <summary>Resolved paste policy after applying authoritative server config and optional client hints.</summary>
    internal readonly struct EffectivePastePolicy
    {
        public ClearPhaseStrategy ClearStrategy { get; init; }
        public bool RetryOnMissingItems { get; init; }
        public bool RetryOnInvalidPlacement { get; init; }
        public SweepPhaseStrategy SweepStrategy { get; init; }
        public PlacePhaseStrategy PlaceStrategy { get; init; }
        public bool RequireItems { get; init; }
        public PastePhaseSettings PlanPhase { get; init; }
        public PastePhaseSettings ClearPhase { get; init; }
        public PastePhaseSettings CutPhase { get; init; }
        public PastePhaseSettings PlacePhase { get; init; }
        public PastePhaseSettings SweepPhase { get; init; }

        public bool IsSweepCheat => SweepStrategy == SweepPhaseStrategy.Cheat;
        public bool UseReplacePlace => PlaceStrategy == PlacePhaseStrategy.Replace;

        /// <summary>
        /// Merges server policy with optional client hints.
        /// Retry flags are permissive: server may force them on, or clients may opt in when allowed.
        /// Strategy enums use client choice only when <see cref="PlacePhaseConfig.AllowClientPreferences"/> applies.
        /// </summary>
        public static EffectivePastePolicy Resolve(PastingConfig server, in PasteClientHints hints)
        {
            bool allowPlacePrefs = server.Phases.Place.AllowClientPreferences;
            return new EffectivePastePolicy
            {
                ClearStrategy = server.Phases.Clear.AllowClientPreferences ? hints.ClearStrategy : server.Phases.Clear.Strategy,
                RetryOnMissingItems = server.Phases.Place.RetryOnMissingItems
                    || (allowPlacePrefs && hints.RetryOnMissingItems),
                RetryOnInvalidPlacement = server.Phases.Place.RetryOnInvalidPlacement
                    || (allowPlacePrefs && hints.RetryOnInvalidPlacement),
                SweepStrategy = server.Phases.Sweep.AllowClientPreferences ? hints.SweepStrategy : server.Phases.Sweep.Strategy,
                PlaceStrategy = server.Phases.Place.Strategy,
                RequireItems = server.Phases.Place.RequireItems,
                PlanPhase = server.Phases.Plan,
                ClearPhase = server.Phases.Clear.ToPhaseSettings(),
                CutPhase = server.Phases.Cut,
                PlacePhase = server.Phases.Place.ToPhaseSettings(),
                SweepPhase = server.Phases.Sweep.ToPhaseSettings(),
            };
        }
    }

    /// <summary>
    /// Arguments for layer execution; <see cref="TheArchitectsBlueprintMod.Core.Layers.Layer.Place"/> performs placement and calls <see cref="TheArchitectsBlueprintMod.Core.Layers.Layer.Consume"/> when appropriate.
    /// </summary>
    internal readonly struct PasteContext(int playerIndex, Item[]? items, IReadOnlyList<Inventory>? inventories, in EffectivePastePolicy policy)
    {
        public readonly Player Player = Main.player[playerIndex];
        public readonly SweepPhaseStrategy SweepStrategy = policy.SweepStrategy;
        public readonly bool IsSweepCheat = policy.IsSweepCheat;
        public readonly bool RetryOnMissingItems = policy.RetryOnMissingItems;
        public readonly bool RetryOnInvalidPlacement = policy.RetryOnInvalidPlacement;
        public readonly bool RequireItems = policy.RequireItems;
        public readonly Item[]? Items = items;
        public readonly IReadOnlyList<Inventory>? Inventories = inventories;
        public readonly ClearPhaseStrategy ClearStrategy = policy.ClearStrategy;
        public readonly PlacePhaseStrategy PlaceStrategy = policy.PlaceStrategy;
        public readonly bool UseReplacePlace = policy.UseReplacePlace;
        public readonly PastePhaseSettings PlanPhase = policy.PlanPhase;
        public readonly PastePhaseSettings ClearPhase = policy.ClearPhase;
        public readonly PastePhaseSettings CutPhase = policy.CutPhase;
        public readonly PastePhaseSettings PlacePhase = policy.PlacePhase;
        public readonly PastePhaseSettings SweepPhase = policy.SweepPhase;
    }
}
