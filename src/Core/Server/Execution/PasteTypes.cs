using System.Collections.Generic;
using TheArchitectsBlueprintMod.Config;
using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Optional client preference hints attached to a paste request.</summary>
    internal readonly struct PasteClientHints(
        bool ignoreMissingItems,
        bool ignoreInvalidPlacement,
        ClearPhaseStrategy clearStrategy,
        SweepPhaseStrategy sweepStrategy)
    {
        public bool IgnoreMissingItems { get; } = ignoreMissingItems;
        public bool IgnoreInvalidPlacement { get; } = ignoreInvalidPlacement;
        public ClearPhaseStrategy ClearStrategy { get; } = clearStrategy;
        public SweepPhaseStrategy SweepStrategy { get; } = sweepStrategy;

        /// <summary>Reads local client ModConfig values for outbound paste requests.</summary>
        public static PasteClientHints FromClientConfig()
        {
            var client = ModContent.GetInstance<ClientPasteConfig>();
            return new PasteClientHints(
                client.Place.IgnoreMissingItems,
                client.Place.IgnoreInvalidPlacement,
                client.Clear.Strategy,
                client.Sweep.Strategy);
        }
    }

    /// <summary>Resolved paste policy after applying authoritative server config and optional client hints.</summary>
    internal readonly struct EffectivePastePolicy
    {
        public ClearPhaseStrategy ClearStrategy { get; init; }
        public bool IgnoreMissingItems { get; init; }
        public bool IgnoreInvalidPlacement { get; init; }
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

        /// <summary>Merges server policy with optional client hints; only honored fields allowed by server policy are applied.</summary>
        public static EffectivePastePolicy Resolve(PastingConfig server, in PasteClientHints hints)
        {
            return new EffectivePastePolicy
            {
                ClearStrategy = server.Clear.AllowClientPreferences ? hints.ClearStrategy : server.Clear.Strategy,
                IgnoreMissingItems = server.Place.AllowClientPreferences ? hints.IgnoreMissingItems : server.Place.IgnoreMissingItems,
                IgnoreInvalidPlacement = server.Place.AllowClientPreferences ? hints.IgnoreInvalidPlacement : server.Place.IgnoreInvalidPlacement,
                SweepStrategy = server.Sweep.AllowClientPreferences ? hints.SweepStrategy : server.Sweep.Strategy,
                PlaceStrategy = server.Place.Strategy,
                RequireItems = server.Place.RequireItems,
                PlanPhase = server.Plan,
                ClearPhase = server.Clear.ToPhaseSettings(),
                CutPhase = server.Cut,
                PlacePhase = server.Place.ToPhaseSettings(),
                SweepPhase = server.Sweep.ToPhaseSettings(),
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
        public readonly bool IgnoreMissingItems = policy.IgnoreMissingItems;
        public readonly bool IgnoreInvalidPlacement = policy.IgnoreInvalidPlacement;
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
