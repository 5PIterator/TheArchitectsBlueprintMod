using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Tracks overlay scopes for the locally held item, mirroring how vanilla updates wire visibility from the selected tool.
    /// Also drives periodic guard-mask refresh for player reach and map exploration.
    /// </summary>
    public sealed class ArchitectOverlayPlayer : ModPlayer
    {
        private const int GuardRefreshIntervalTicks = 60;

        private Point _lastGuardAnchorTile = new(int.MinValue, int.MinValue);
        private int _guardRefreshTickCounter;

        public ArchitectOverlayScope HeldOverlayScope { get; private set; }

        public override void PostUpdate()
        {
            if (Player.whoAmI != Main.myPlayer)
                return;

            HeldOverlayScope = ArchitectOverlayVisibility.GetItemScope(Player.HeldItem.type);
            TickGuardMaskRefresh();
        }

        /// <summary>Refreshes guard masks when the player moves a tile or once per second (skipped during active pastes).</summary>
        private void TickGuardMaskRefresh()
        {
            if (BlueprintGuard.HasActivePasteFor(Player))
                return;

            Point anchorTile = Player.Center.ToTileCoordinates();
            bool moved = anchorTile != _lastGuardAnchorTile;
            _guardRefreshTickCounter++;
            bool intervalElapsed = _guardRefreshTickCounter >= GuardRefreshIntervalTicks;

            if (!moved && !intervalElapsed)
                return;

            if (moved)
                _lastGuardAnchorTile = anchorTile;

            if (intervalElapsed)
                _guardRefreshTickCounter = 0;

            BlueprintGuard.RefreshClientGuardMasks(Player);
        }
    }
}
