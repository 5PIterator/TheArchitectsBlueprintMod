#if DEBUG
#nullable enable

using System.Collections;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core.Phases;

namespace TheArchitectsBlueprintMod.Core.TestCases
{
    /// <summary>
    /// Test Fixture node that pins every paste-related ModConfig field for Case runs, then restores.
    /// Covers ClientPasteConfig and PastingConfig (including client safeguard prefs and server gates).
    /// </summary>
    internal static class PasteConfigFixture
    {
        /// <summary>
        /// Applies <paramref name="profile"/> for the duration of <paramref name="body"/>, then restores.
        /// </summary>
        public static IEnumerator RunWith(PasteConfigProfile profile, IEnumerator body)
        {
            // --- Snapshot all paste ModConfigs, apply a complete profile, pump body, always restore.

            ClientPasteConfig client = ModContent.GetInstance<ClientPasteConfig>();
            PastingConfig server = ModContent.GetInstance<PastingConfig>();

            ConfigSnapshot previous = Snapshot(client, server);
            Apply(profile, client, server);

            while (true)
            {
                bool moved;
                try
                {
                    moved = body.MoveNext();
                }
                catch
                {
                    Restore(client, server, previous);
                    throw;
                }

                if (!moved)
                    break;

                yield return body.Current;
            }

            Restore(client, server, previous);
        }

        private static ConfigSnapshot Snapshot(
            ClientPasteConfig client,
            PastingConfig server) =>
            new(
                SnapshotClient(client),
                SnapshotServer(server));

        private static void Apply(
            PasteConfigProfile profile,
            ClientPasteConfig client,
            PastingConfig server)
        {
            // --- Every field is assigned; no ModConfig value is left at the player's prior setting.

            ApplyServerDefaults(server, retryOnMissingItems: profile == PasteConfigProfile.RetryOnMissing);
            ApplyClientDefaults(client, retryOnMissingItems: profile == PasteConfigProfile.RetryOnMissing);
        }

        private static void ApplyServerDefaults(PastingConfig server, bool retryOnMissingItems)
        {
            ClientPasteLimitsConfig clientLimits = server.Limits.Client;
            clientLimits.MaxPasteWidth = 200;
            clientLimits.MaxPasteHeight = 200;
            clientLimits.VolumeLimit = 5000;
            clientLimits.PlayerOperationLimit = 1;
            clientLimits.AllowClientSafeguardPreferences = true;
            clientLimits.RestrictCopyAndPasteToRevealedTiles = false;
            clientLimits.LimitCopyAndPasteToPlayerReach = false;
            clientLimits.MaxReachDistanceTiles = 5000;
            clientLimits.MaxReachDistanceTilesCap = 5000;

            ServerPasteLimitsConfig serverLimits = server.Limits.Server;
            serverLimits.ServerOperationLimit = 8;
            serverLimits.MaxPhaseTickSize = 512;
            serverLimits.MaxTilesPerCategory = 4096;
            serverLimits.MaxConcurrentArrows = 64;

            PlanPhaseConfig plan = server.Phases.Plan;
            plan.Active = true;
            plan.TickSize = 50;
            plan.TickSpeed = 0;
            plan.Strategy.Style = ClearStyle.Transparent;
            plan.AllowClientPreferences = true;

            ClearPhaseConfig clear = server.Phases.Clear;
            clear.Active = true;
            clear.TickSize = 100;
            clear.TickSpeed = 0;
            clear.Strategy.ItemDrop = ClearItemDropStyle.Void;
            clear.AllowClientPreferences = true;

            CutPhaseConfig cut = server.Phases.Cut;
            cut.Active = true;
            cut.TickSize = 100;
            cut.TickSpeed = 0;
            cut.Strategy.EnableCutPasteProcess = false;

            PlacePhaseConfig place = server.Phases.Place;
            place.Active = true;
            place.TickSize = 100;
            place.TickSpeed = 0;
            place.Strategy.RequireItems = true;
            place.Strategy.RetryOnMissingItems = retryOnMissingItems;
            place.Strategy.RetryOnInvalidPlacement = false;
            place.Strategy.Style = PlaceStyle.Place;
            place.AllowClientPreferences = true;

            SweepPhaseConfig sweep = server.Phases.Sweep;
            sweep.Active = false;
            sweep.TickSize = 512;
            sweep.TickSpeed = 0;

            LayerPolicySettings main = server.LayerPolicy.MainLayers;
            main.Wall = true;
            main.Block = true;
            main.Falling = true;
            main.Frame = true;
            main.Entity = true;
            main.Custom = true;
            main.Paint = true;
            main.Wiring = true;
            main.Detail = true;
            main.Fluid = true;
            main.Unknown = true;

            CustomLayerPolicySettings custom = server.LayerPolicy.CustomLayers;
            custom.Plant = true;
            custom.Vine = true;
            custom.Tree = true;
        }

        private static void ApplyClientDefaults(ClientPasteConfig client, bool retryOnMissingItems)
        {
            // --- Chamber Cases must not depend on map reveal or a tight copy/paste reach radius.

            client.Plan.Strategy.Style = ClearStyle.Transparent;
            client.Clear.Strategy.ItemDrop = ClearItemDropStyle.Void;
            client.Place.Strategy.RetryOnMissingItems = retryOnMissingItems;
            client.Place.Strategy.RetryOnInvalidPlacement = false;
            client.Safeguards.RestrictCopyAndPasteToRevealedTiles = false;
            client.Safeguards.LimitCopyAndPasteToPlayerReach = false;
            client.Safeguards.MaxReachDistanceTiles = 5000;
            client.Arrow.Speed = 48;
            client.Arrow.MinSpeed = 2.5f;
            client.Arrow.MaxSpeed = 24f;
            client.Overlay.RectBorders = false;
        }

        private static void Restore(
            ClientPasteConfig client,
            PastingConfig server,
            ConfigSnapshot previous)
        {
            RestoreClient(client, previous.Client);
            RestoreServer(server, previous.Server);
        }

        private static ClientSnapshot SnapshotClient(ClientPasteConfig client) =>
            new(
                client.Plan.Strategy.Style,
                client.Clear.Strategy.ItemDrop,
                client.Place.Strategy.RetryOnMissingItems,
                client.Place.Strategy.RetryOnInvalidPlacement,
                client.Safeguards.RestrictCopyAndPasteToRevealedTiles,
                client.Safeguards.LimitCopyAndPasteToPlayerReach,
                client.Safeguards.MaxReachDistanceTiles,
                client.Arrow.Speed,
                client.Arrow.MinSpeed,
                client.Arrow.MaxSpeed,
                client.Overlay.RectBorders);

        private static void RestoreClient(ClientPasteConfig client, ClientSnapshot s)
        {
            client.Plan.Strategy.Style = s.PlanStyle;
            client.Clear.Strategy.ItemDrop = s.ClearItemDrop;
            client.Place.Strategy.RetryOnMissingItems = s.RetryOnMissingItems;
            client.Place.Strategy.RetryOnInvalidPlacement = s.RetryOnInvalidPlacement;
            client.Safeguards.RestrictCopyAndPasteToRevealedTiles = s.RestrictToRevealedTiles;
            client.Safeguards.LimitCopyAndPasteToPlayerReach = s.LimitToPlayerReach;
            client.Safeguards.MaxReachDistanceTiles = s.MaxReachDistanceTiles;
            client.Arrow.Speed = s.ArrowSpeed;
            client.Arrow.MinSpeed = s.ArrowMinSpeed;
            client.Arrow.MaxSpeed = s.ArrowMaxSpeed;
            client.Overlay.RectBorders = s.OverlayRectBorders;
        }

        private static ServerSnapshot SnapshotServer(PastingConfig server) =>
            new(
                server.Limits.Client.MaxPasteWidth,
                server.Limits.Client.MaxPasteHeight,
                server.Limits.Client.VolumeLimit,
                server.Limits.Client.PlayerOperationLimit,
                server.Limits.Client.AllowClientSafeguardPreferences,
                server.Limits.Client.RestrictCopyAndPasteToRevealedTiles,
                server.Limits.Client.LimitCopyAndPasteToPlayerReach,
                server.Limits.Client.MaxReachDistanceTiles,
                server.Limits.Client.MaxReachDistanceTilesCap,
                server.Limits.Server.ServerOperationLimit,
                server.Limits.Server.MaxPhaseTickSize,
                server.Limits.Server.MaxTilesPerCategory,
                server.Limits.Server.MaxConcurrentArrows,
                server.Phases.Plan.Active,
                server.Phases.Plan.TickSize,
                server.Phases.Plan.TickSpeed,
                server.Phases.Plan.Strategy.Style,
                server.Phases.Plan.AllowClientPreferences,
                server.Phases.Clear.Active,
                server.Phases.Clear.TickSize,
                server.Phases.Clear.TickSpeed,
                server.Phases.Clear.Strategy.ItemDrop,
                server.Phases.Clear.AllowClientPreferences,
                server.Phases.Cut.Active,
                server.Phases.Cut.TickSize,
                server.Phases.Cut.TickSpeed,
                server.Phases.Cut.Strategy.EnableCutPasteProcess,
                server.Phases.Place.Active,
                server.Phases.Place.TickSize,
                server.Phases.Place.TickSpeed,
                server.Phases.Place.Strategy.RequireItems,
                server.Phases.Place.Strategy.RetryOnMissingItems,
                server.Phases.Place.Strategy.RetryOnInvalidPlacement,
                server.Phases.Place.Strategy.Style,
                server.Phases.Place.AllowClientPreferences,
                server.Phases.Sweep.Active,
                server.Phases.Sweep.TickSize,
                server.Phases.Sweep.TickSpeed,
                server.LayerPolicy.MainLayers.Wall,
                server.LayerPolicy.MainLayers.Block,
                server.LayerPolicy.MainLayers.Falling,
                server.LayerPolicy.MainLayers.Frame,
                server.LayerPolicy.MainLayers.Entity,
                server.LayerPolicy.MainLayers.Custom,
                server.LayerPolicy.MainLayers.Paint,
                server.LayerPolicy.MainLayers.Wiring,
                server.LayerPolicy.MainLayers.Detail,
                server.LayerPolicy.MainLayers.Fluid,
                server.LayerPolicy.MainLayers.Unknown,
                server.LayerPolicy.CustomLayers.Plant,
                server.LayerPolicy.CustomLayers.Vine,
                server.LayerPolicy.CustomLayers.Tree);

        private static void RestoreServer(PastingConfig server, ServerSnapshot s)
        {
            server.Limits.Client.MaxPasteWidth = s.MaxPasteWidth;
            server.Limits.Client.MaxPasteHeight = s.MaxPasteHeight;
            server.Limits.Client.VolumeLimit = s.VolumeLimit;
            server.Limits.Client.PlayerOperationLimit = s.PlayerOperationLimit;
            server.Limits.Client.AllowClientSafeguardPreferences = s.AllowClientSafeguardPreferences;
            server.Limits.Client.RestrictCopyAndPasteToRevealedTiles = s.RestrictToRevealedTiles;
            server.Limits.Client.LimitCopyAndPasteToPlayerReach = s.LimitToPlayerReach;
            server.Limits.Client.MaxReachDistanceTiles = s.MaxReachDistanceTiles;
            server.Limits.Client.MaxReachDistanceTilesCap = s.MaxReachDistanceTilesCap;
            server.Limits.Server.ServerOperationLimit = s.ServerOperationLimit;
            server.Limits.Server.MaxPhaseTickSize = s.MaxPhaseTickSize;
            server.Limits.Server.MaxTilesPerCategory = s.MaxTilesPerCategory;
            server.Limits.Server.MaxConcurrentArrows = s.MaxConcurrentArrows;

            server.Phases.Plan.Active = s.PlanActive;
            server.Phases.Plan.TickSize = s.PlanTickSize;
            server.Phases.Plan.TickSpeed = s.PlanTickSpeed;
            server.Phases.Plan.Strategy.Style = s.PlanStyle;
            server.Phases.Plan.AllowClientPreferences = s.PlanAllowClientPreferences;

            server.Phases.Clear.Active = s.ClearActive;
            server.Phases.Clear.TickSize = s.ClearTickSize;
            server.Phases.Clear.TickSpeed = s.ClearTickSpeed;
            server.Phases.Clear.Strategy.ItemDrop = s.ClearItemDrop;
            server.Phases.Clear.AllowClientPreferences = s.ClearAllowClientPreferences;

            server.Phases.Cut.Active = s.CutActive;
            server.Phases.Cut.TickSize = s.CutTickSize;
            server.Phases.Cut.TickSpeed = s.CutTickSpeed;
            server.Phases.Cut.Strategy.EnableCutPasteProcess = s.CutEnableCutPasteProcess;

            server.Phases.Place.Active = s.PlaceActive;
            server.Phases.Place.TickSize = s.PlaceTickSize;
            server.Phases.Place.TickSpeed = s.PlaceTickSpeed;
            server.Phases.Place.Strategy.RequireItems = s.PlaceRequireItems;
            server.Phases.Place.Strategy.RetryOnMissingItems = s.PlaceRetryOnMissingItems;
            server.Phases.Place.Strategy.RetryOnInvalidPlacement = s.PlaceRetryOnInvalidPlacement;
            server.Phases.Place.Strategy.Style = s.PlaceStyle;
            server.Phases.Place.AllowClientPreferences = s.PlaceAllowClientPreferences;

            server.Phases.Sweep.Active = s.SweepActive;
            server.Phases.Sweep.TickSize = s.SweepTickSize;
            server.Phases.Sweep.TickSpeed = s.SweepTickSpeed;

            server.LayerPolicy.MainLayers.Wall = s.LayerWall;
            server.LayerPolicy.MainLayers.Block = s.LayerBlock;
            server.LayerPolicy.MainLayers.Falling = s.LayerFalling;
            server.LayerPolicy.MainLayers.Frame = s.LayerFrame;
            server.LayerPolicy.MainLayers.Entity = s.LayerEntity;
            server.LayerPolicy.MainLayers.Custom = s.LayerCustom;
            server.LayerPolicy.MainLayers.Paint = s.LayerPaint;
            server.LayerPolicy.MainLayers.Wiring = s.LayerWiring;
            server.LayerPolicy.MainLayers.Detail = s.LayerDetail;
            server.LayerPolicy.MainLayers.Fluid = s.LayerFluid;
            server.LayerPolicy.MainLayers.Unknown = s.LayerUnknown;
            server.LayerPolicy.CustomLayers.Plant = s.LayerPlant;
            server.LayerPolicy.CustomLayers.Vine = s.LayerVine;
            server.LayerPolicy.CustomLayers.Tree = s.LayerTree;
        }

        private readonly record struct ConfigSnapshot(
            ClientSnapshot Client,
            ServerSnapshot Server);

        private readonly record struct ClientSnapshot(
            ClearStyle PlanStyle,
            ClearItemDropStyle ClearItemDrop,
            bool RetryOnMissingItems,
            bool RetryOnInvalidPlacement,
            bool RestrictToRevealedTiles,
            bool LimitToPlayerReach,
            int MaxReachDistanceTiles,
            int ArrowSpeed,
            float ArrowMinSpeed,
            float ArrowMaxSpeed,
            bool OverlayRectBorders);

        private readonly record struct ServerSnapshot(
            int MaxPasteWidth,
            int MaxPasteHeight,
            int VolumeLimit,
            int PlayerOperationLimit,
            bool AllowClientSafeguardPreferences,
            bool RestrictToRevealedTiles,
            bool LimitToPlayerReach,
            int MaxReachDistanceTiles,
            int MaxReachDistanceTilesCap,
            int ServerOperationLimit,
            int MaxPhaseTickSize,
            int MaxTilesPerCategory,
            int MaxConcurrentArrows,
            bool PlanActive,
            int PlanTickSize,
            int PlanTickSpeed,
            ClearStyle PlanStyle,
            bool PlanAllowClientPreferences,
            bool ClearActive,
            int ClearTickSize,
            int ClearTickSpeed,
            ClearItemDropStyle ClearItemDrop,
            bool ClearAllowClientPreferences,
            bool CutActive,
            int CutTickSize,
            int CutTickSpeed,
            bool CutEnableCutPasteProcess,
            bool PlaceActive,
            int PlaceTickSize,
            int PlaceTickSpeed,
            bool PlaceRequireItems,
            bool PlaceRetryOnMissingItems,
            bool PlaceRetryOnInvalidPlacement,
            PlaceStyle PlaceStyle,
            bool PlaceAllowClientPreferences,
            bool SweepActive,
            int SweepTickSize,
            int SweepTickSpeed,
            bool LayerWall,
            bool LayerBlock,
            bool LayerFalling,
            bool LayerFrame,
            bool LayerEntity,
            bool LayerCustom,
            bool LayerPaint,
            bool LayerWiring,
            bool LayerDetail,
            bool LayerFluid,
            bool LayerUnknown,
            bool LayerPlant,
            bool LayerVine,
            bool LayerTree);
    }

    /// <summary>Named paste ModConfig join points for paste / paste-mp Cases.</summary>
    internal enum PasteConfigProfile
    {
        /// <summary>
        /// Full deterministic paste config: map/reach limits off, all layers on, RequireItems,
        /// transparent/void strategies, no place retries, PlayerOperationLimit=1.
        /// </summary>
        DefaultPaste,

        /// <summary>
        /// Same as DefaultPaste, but RetryOnMissingItems=true (client + server place strategy)
        /// so the first paste stays alive for lifecycle / op-limit Cases.
        /// </summary>
        RetryOnMissing,
    }
}
#endif
