using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core.Architect;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Tracks overlay scopes for the locally held item, mirroring how vanilla updates wire visibility from the selected tool.
    /// Also drives periodic and budgeted guard-mask refresh for player reach and map exploration.
    /// </summary>
    public sealed class ArchitectOverlayPlayer : ModPlayer
    {
        private const int GuardRefreshIntervalTicks = 60;

        private Point _lastGuardAnchorTile = new(int.MinValue, int.MinValue);
        private int _guardRefreshTickCounter;

        public ArchitectOverlayScope HeldOverlayScope { get; private set; }

        public override void PostUpdate()
        {
            // --- Prefer Architect Mode / map render-all, else fall back to held-item scopes.

            if (Player.whoAmI != Main.myPlayer)
                return;

            if (ModContent.GetInstance<ArchitectSystem>().Active)
                HeldOverlayScope = ArchitectOverlayScope.All;
            else if (ArchitectMapTransform.IsMapViewActive)
                HeldOverlayScope = ArchitectOverlayScope.All;
            else
                HeldOverlayScope = ArchitectOverlayVisibility.GetItemScope(Player.HeldItem.type);

            TickGuardMaskRefresh();
            BlueprintGuard.TickClientGuardScan(Player);
        }

        /// <summary>Queues a new guard scan on tile move or once per second (cancels any unfinished scan).</summary>
        private void TickGuardMaskRefresh()
        {
            // --- Skip while pasting; move-refresh small reach areas, interval-refresh reveal and large reach.

            if (BlueprintGuard.HasActivePasteFor(Player))
            {
                BlueprintGuard.CancelClientGuardScan();
                return;
            }

            if (!BlueprintGuard.NeedsDynamicGuardRefresh())
            {
                BlueprintGuard.CancelClientGuardScan();
                return;
            }

            Point anchorTile = Player.Center.ToTileCoordinates();
            bool moved = anchorTile != _lastGuardAnchorTile;
            _guardRefreshTickCounter++;
            bool intervalElapsed = _guardRefreshTickCounter >= GuardRefreshIntervalTicks;
            bool large = BlueprintGuard.HasLargeOwnedBlueprint(Player);
            bool reach = BlueprintGuard.NeedsMoveDrivenGuardRefresh();
            bool reveal = BlueprintGuard.NeedsRevealDrivenGuardRefresh();

            // Small reach areas update immediately on move; large reach and reveal use the 1s tick.
            bool refreshOnMove = moved && reach && !large;
            bool refreshOnInterval = intervalElapsed && (reveal || (reach && large));

            if (!refreshOnMove && !refreshOnInterval)
                return;

            if (moved)
                _lastGuardAnchorTile = anchorTile;

            if (intervalElapsed)
                _guardRefreshTickCounter = 0;

            // New request cancels any mid-scan work and starts a fresh batch.
            BlueprintGuard.RefreshClientGuardMasks(Player);
        }
    }
}
