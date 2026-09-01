using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Graphics.Capture;
using Terraria.Map;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Tile ↔ screen conversion for the fullscreen map, mirroring vanilla DrawMap and MapOverlayDrawContext.
    /// </summary>
    internal static class ArchitectMapTransform
    {
        internal const float FullscreenEdgePaddingTiles = 10f;

        private static bool _hasCachedContext;
        private static uint _cachedGameUpdateCount;
        private static Vector2 _mapPosition;
        private static Vector2 _mapOffset;
        private static float _mapScale;

        /// <summary>True when <see cref="SetCurrent"/> ran this game update with a live map context.</summary>
        public static bool IsContextValid =>
            _hasCachedContext && _cachedGameUpdateCount == Main.GameUpdateCount;

        /// <summary>Screen pixels per map tile on the current map view.</summary>
        public static float PixelsPerTile =>
            IsContextValid ? _mapScale : Main.mapFullscreenScale;

        /// <summary>Fullscreen map or Camera Mode framing from the map.</summary>
        public static bool IsMapViewActive =>
            Main.mapFullscreen
            || (CaptureManager.Instance is { Active: true, UsingMap: true });

        /// <summary>Caches transform parameters from the active map overlay draw context.</summary>
        public static void SetCurrent(in MapOverlayDrawContext context)
        {
            _mapPosition = context.MapPosition;
            _mapOffset = context.MapOffset;
            _mapScale = context.MapScale;
            _hasCachedContext = _mapScale > 0f;
            _cachedGameUpdateCount = Main.GameUpdateCount;
        }

        public static void ClearCurrent() => _hasCachedContext = false;

        public static Vector2 TileToScreen(Point tile) => TileToScreen(new Vector2(tile.X, tile.Y));

        public static Vector2 TileToScreen(Vector2 tile)
        {
            if (IsContextValid)
                return (tile - _mapPosition) * _mapScale + _mapOffset;

            GetFallbackTransform(out Vector2 mapPosition, out Vector2 mapOffset, out float mapScale);
            return (tile - mapPosition) * mapScale + mapOffset;
        }

        public static Vector2 ScreenToTile(Vector2 screen)
        {
            if (IsContextValid)
                return (screen - _mapOffset) / _mapScale + _mapPosition;

            GetFallbackTransform(out Vector2 mapPosition, out Vector2 mapOffset, out float mapScale);
            if (mapScale <= 0f)
                return Vector2.Zero;

            return (screen - mapOffset) / mapScale + mapPosition;
        }

        /// <summary>Converts screen coordinates to a tile using vanilla fullscreen-map integer casting.</summary>
        public static bool TryScreenToTile(Vector2 screen, out Point tile)
        {
            if (!IsMapViewActive)
            {
                tile = default;
                return false;
            }

            if (IsContextValid)
            {
                Vector2 mapTile = ScreenToTile(screen);
                tile = new Point((int)mapTile.X, (int)mapTile.Y);
                return true;
            }

            GetFallbackTransform(out _, out Vector2 mapOffset, out float mapScale);
            if (mapScale <= 0f)
            {
                tile = default;
                return false;
            }

            tile = new Point(
                (int)((-mapOffset.X + screen.X) / mapScale + FullscreenEdgePaddingTiles),
                (int)((-mapOffset.Y + screen.Y) / mapScale + FullscreenEdgePaddingTiles));
            return true;
        }

        /// <summary>Replicates Main.DrawMap fullscreen offset math when no cached context is available.</summary>
        private static void GetFallbackTransform(out Vector2 mapPosition, out Vector2 mapOffset, out float mapScale)
        {
            mapPosition = new Vector2(FullscreenEdgePaddingTiles, FullscreenEdgePaddingTiles);
            mapScale = Main.mapFullscreenScale;
            if (mapScale <= 0f)
            {
                mapOffset = Vector2.Zero;
                return;
            }

            float centerX = Main.mapFullscreenPos.X * mapScale;
            float centerY = Main.mapFullscreenPos.Y * mapScale;
            mapOffset = new Vector2(
                -centerX + Main.screenWidth / 2f + FullscreenEdgePaddingTiles * mapScale,
                -centerY + Main.screenHeight / 2f + FullscreenEdgePaddingTiles * mapScale);
        }
    }
}
