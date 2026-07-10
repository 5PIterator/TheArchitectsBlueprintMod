using System.Collections.Generic;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core.Layers;
using TheArchitectsBlueprintMod.Core.Phases;
using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Optional client preference hints attached to a paste request.</summary>
    internal readonly struct PasteClientHints(
        bool retryOnMissingItems,
        bool retryOnInvalidPlacement,
        ClearStyle clearStyle,
        ClearItemDropStyle clearItemDropStyle,
        SweepStyle sweepStyle,
        int arrowSpeed,
        float arrowMinSpeed,
        float arrowMaxSpeed)
    {
        public bool RetryOnMissingItems { get; } = retryOnMissingItems;
        public bool RetryOnInvalidPlacement { get; } = retryOnInvalidPlacement;
        public ClearStyle ClearStyle { get; } = clearStyle;
        public ClearItemDropStyle ClearItemDropStyle { get; } = clearItemDropStyle;
        public SweepStyle SweepStyle { get; } = sweepStyle;
        public int ArrowSpeed { get; } = arrowSpeed;
        public float ArrowMinSpeed { get; } = arrowMinSpeed;
        public float ArrowMaxSpeed { get; } = arrowMaxSpeed;

        /// <summary>Reads local client ModConfig values for outbound paste requests.</summary>
        public static PasteClientHints FromClientConfig()
        {
            var client = ModContent.GetInstance<ClientPasteConfig>();
            return new PasteClientHints(
                client.Place.Strategy.RetryOnMissingItems,
                client.Place.Strategy.RetryOnInvalidPlacement,
                client.Plan.Strategy.Style,
                client.Clear.Strategy.ItemDrop,
                client.Sweep.Strategy.Style,
                client.Arrow.Speed,
                client.Arrow.MinSpeed,
                client.Arrow.MaxSpeed);
        }
    }

    /// <summary>Resolved paste policy after applying authoritative server config and optional client hints.</summary>
    internal readonly struct EffectivePastePolicy
    {
        public PastePhasePolicy<PlanPhaseStrategy> PlanPhase { get; init; }
        public PastePhasePolicy<ClearPhaseStrategy> ClearPhase { get; init; }
        public PastePhasePolicy<CutPhaseStrategy> CutPhase { get; init; }
        public PastePhasePolicy<PlacePhaseStrategy> PlacePhase { get; init; }
        public PastePhasePolicy<SweepPhaseStrategy> SweepPhase { get; init; }
        public int ArrowSpeed { get; init; }
        public float ArrowMinSpeed { get; init; }
        public float ArrowMaxSpeed { get; init; }

        /// <summary>
        /// Merges server policy with optional client hints.
        /// Retry flags are permissive: server may force them on, or clients may opt in when allowed.
        /// Strategy values use client choice only when the phase's <c>AllowClientPreferences</c> applies.
        /// </summary>
        public static EffectivePastePolicy Resolve(PastingConfig server, in PasteClientHints hints)
        {
            var placeServer = server.Phases.Place.Strategy;
            bool allowPlacePrefs = server.Phases.Place.AllowClientPreferences;

            var planStrategy = new PlanPhaseStrategy
            {
                Style = server.Phases.Plan.AllowClientPreferences
                    ? hints.ClearStyle
                    : server.Phases.Plan.Strategy.Style,
            };

            var clearStrategy = new ClearPhaseStrategy
            {
                ItemDrop = server.Phases.Clear.AllowClientPreferences
                    ? hints.ClearItemDropStyle
                    : server.Phases.Clear.Strategy.ItemDrop,
            };

            var cutStrategy = new CutPhaseStrategy
            {
                EnableCutPasteProcess = server.Phases.Cut.Strategy.EnableCutPasteProcess,
            };

            var placeStrategy = new PlacePhaseStrategy
            {
                RequireItems = placeServer.RequireItems,
                RetryOnMissingItems = placeServer.RetryOnMissingItems
                    || (allowPlacePrefs && hints.RetryOnMissingItems),
                RetryOnInvalidPlacement = placeServer.RetryOnInvalidPlacement
                    || (allowPlacePrefs && hints.RetryOnInvalidPlacement),
                Style = placeServer.Style,
            };

            var sweepStrategy = new SweepPhaseStrategy
            {
                Style = server.Phases.Sweep.AllowClientPreferences
                    ? hints.SweepStyle
                    : server.Phases.Sweep.Strategy.Style,
            };

            return new EffectivePastePolicy
            {
                PlanPhase = server.Phases.Plan.ToPolicy(planStrategy),
                ClearPhase = server.Phases.Clear.ToPolicy(clearStrategy),
                CutPhase = server.Phases.Cut.ToPolicy(cutStrategy),
                PlacePhase = server.Phases.Place.ToPolicy(placeStrategy),
                SweepPhase = server.Phases.Sweep.ToPolicy(sweepStrategy),
                ArrowSpeed = hints.ArrowSpeed,
                ArrowMinSpeed = hints.ArrowMinSpeed,
                ArrowMaxSpeed = hints.ArrowMaxSpeed,
            };
        }
    }

    /// <summary>
    /// Arguments for layer execution; <see cref="TheArchitectsBlueprintMod.Core.Layers.Layer.Place"/> performs placement and calls <see cref="TheArchitectsBlueprintMod.Core.Layers.Layer.Consume"/> when appropriate.
    /// </summary>
    internal readonly struct PasteContext(
        int playerIndex,
        InventoryLookup? inventoryLookup,
        in EffectivePastePolicy policy,
        PasteTileLookup? lookup = null,
        TileEffectSession? effectSession = null,
        IReadOnlyList<FrameOperations.FrameCandidate>? pendingFramePlaceCandidates = null)
    {
        public readonly Player Player = Main.player[playerIndex];
        public readonly SweepStyle SweepStrategy = policy.SweepPhase.Strategy.Style;
        public readonly bool IsSweepCheat = policy.SweepPhase.Strategy.Style == SweepStyle.Cheat;
        public readonly bool RetryOnMissingItems = policy.PlacePhase.Strategy.RetryOnMissingItems;
        public readonly bool RetryOnInvalidPlacement = policy.PlacePhase.Strategy.RetryOnInvalidPlacement;
        public readonly bool RequireItems = policy.PlacePhase.Strategy.RequireItems;
        public readonly InventoryLookup? InventoryLookup = inventoryLookup;
        public readonly Item[]? Items = inventoryLookup?.GetContents();
        public readonly IReadOnlyList<Inventory>? Inventories = inventoryLookup?.Inventories;
        public readonly ClearStyle ClearStrategy = policy.PlanPhase.Strategy.Style;
        public readonly ClearItemDropStyle ItemDropStyle = policy.ClearPhase.Strategy.ItemDrop;
        public readonly PlaceStyle PlacementStyle = policy.PlacePhase.Strategy.Style;
        public readonly bool UseReplacePlace = policy.PlacePhase.Strategy.Style == PlaceStyle.Replace;
        public readonly PastePhasePolicy<PlanPhaseStrategy> PlanPhase = policy.PlanPhase;
        public readonly PastePhasePolicy<ClearPhaseStrategy> ClearPhase = policy.ClearPhase;
        public readonly PastePhasePolicy<CutPhaseStrategy> CutPhase = policy.CutPhase;
        public readonly PastePhasePolicy<PlacePhaseStrategy> PlacePhase = policy.PlacePhase;
        public readonly PastePhasePolicy<SweepPhaseStrategy> SweepPhase = policy.SweepPhase;
        public readonly int ArrowSpeed = policy.ArrowSpeed;
        public readonly float ArrowMinSpeed = policy.ArrowMinSpeed;
        public readonly float ArrowMaxSpeed = policy.ArrowMaxSpeed;
        public readonly PasteTileLookup? Lookup = lookup;
        public readonly TileEffectSession? EffectSession = effectSession;
        public readonly IReadOnlyList<FrameOperations.FrameCandidate>? PendingFramePlaceCandidates = pendingFramePlaceCandidates;
    }
}
