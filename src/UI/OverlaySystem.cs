using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.UI;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.Architect;
using TheArchitectsBlueprintMod.Core.Preview;
using TheArchitectsBlueprintMod.Utils;

namespace TheArchitectsBlueprintMod.UI
{
    /// <summary>
    /// Context computed at draw time. <see cref="OverlaySystem"/> uses this to adapt (e.g. skip when map is open).
    /// </summary>
    public readonly struct OverlayContext
    {
        public bool IsGameMenu { get; init; }
        public bool IsMapFullscreen { get; init; }
        public bool ShouldDrawWorldOverlay => !IsGameMenu && !IsMapFullscreen;
    }

    /// <summary>
    /// Payload stored in the <see cref="OverlayCatalog"/> for one registered overlay primitive.
    /// </summary>
    internal readonly struct OverlayDrawable(OverlayDrawableKind kind, object data, Color color, int borderWidth)
    {
        private readonly OverlayDrawableKind _kind = kind;
        private readonly object _data = data;
        private readonly Color _color = color;
        private readonly int _borderWidth = borderWidth;

        public OverlayDrawableKind Kind => _kind;

        public void Draw()
        {
            if (OverlaySystem.IsMapDrawMode)
            {
                if (_kind == OverlayDrawableKind.Screen)
                    return;

                if (_kind == OverlayDrawableKind.MapPayloadPreview)
                {
                    OverlaySystem.DrawMapPayloadPreview((Projection)_data);
                    return;
                }

                OverlaySystem.DrawOnMap(_kind, _data, _color, _borderWidth);
                return;
            }

            switch (_kind)
            {
                case OverlayDrawableKind.Point:
                    OverlaySystem.DrawPoints((Point[])_data, _color);
                    break;
                case OverlayDrawableKind.TileFill:
                    OverlaySystem.DrawTileFill((Point[])_data, _color);
                    break;
                case OverlayDrawableKind.TileRectFill:
                    OverlaySystem.DrawTileRectFill((Rectangle[])_data, _color);
                    break;
                case OverlayDrawableKind.TileBounds:
                    OverlaySystem.DrawTileBounds((Rectangle)_data, _color, _borderWidth);
                    break;
                case OverlayDrawableKind.TileBorder:
                    OverlaySystem.DrawTileBorder((Rectangle)_data, _color, _borderWidth);
                    break;
                case OverlayDrawableKind.GuardedTileMask:
                    OverlaySystem.DrawTileFill((Point[])_data, _color);
                    break;
                case OverlayDrawableKind.Screen:
                    ((Action)_data)();
                    break;
            }
        }
    }

    internal enum OverlayDrawableKind
    {
        Point,
        TileFill,
        TileRectFill,
        TileBounds,
        TileBorder,
        Screen,
        MapPayloadPreview,
        GuardedTileMask,
    }

    /// <summary>
    /// Client-side registry of world overlays. Items are keyed by <c>(family, id)</c>; draw order follows first registration per key.
    /// </summary>
    internal sealed class OverlayCatalog
    {
        private const char FamilyKeySeparator = '\u001f';

        private readonly Dictionary<string, CatalogItem> _items = new(StringComparer.Ordinal);
        private readonly List<string> _drawOrder = [];

        private readonly struct CatalogItem(string? family, OverlayDrawable drawable, long expiresAt)
        {
            public string? Family { get; } = family;
            public OverlayDrawable Drawable { get; } = drawable;
            /// <summary>Absolute tick count after which this item is removed. 0 means it never expires.</summary>
            public long ExpiresAt { get; } = expiresAt;
            public bool IsExpired => ExpiresAt != 0 && Environment.TickCount64 >= ExpiresAt;
        }

        private static string MakeKey(string? family, string id)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Catalog id cannot be null or empty.", nameof(id));
            return string.IsNullOrEmpty(family) ? id : family + FamilyKeySeparator + id;
        }

        /// <summary>
        /// <paramref name="family"/> null or empty: <paramref name="id"/> is globally unique. Otherwise ids are unique per family.
        /// Re-registering an existing key resets its TTL.
        /// </summary>
        /// <param name="ttlMs">If provided, the overlay is automatically removed after this many milliseconds.</param>
        public void Register(string? family, string id, OverlayDrawable drawable, int? ttlMs = null)
        {
            string key = MakeKey(family, id);
            long expiresAt = ttlMs.HasValue ? Environment.TickCount64 + ttlMs.Value : 0;
            if (!_items.ContainsKey(key))
                _drawOrder.Add(key);
            _items[key] = new CatalogItem(family, drawable, expiresAt);
        }

        public void Unregister(string? family, string id)
        {
            string key = MakeKey(family, id);
            if (!_items.Remove(key))
                return;
            _drawOrder.Remove(key);
        }

        /// <summary>Removes every catalog item whose family matches <paramref name="family"/>.</summary>
        public void UnregisterFamily(string family)
        {
            if (string.IsNullOrEmpty(family))
                return;

            for (int i = _drawOrder.Count - 1; i >= 0; i--)
            {
                string key = _drawOrder[i];
                if (_items.TryGetValue(key, out var item) &&
                    string.Equals(item.Family, family, StringComparison.Ordinal))
                {
                    _items.Remove(key);
                    _drawOrder.RemoveAt(i);
                }
            }
        }

        public void Clear()
        {
            _items.Clear();
            _drawOrder.Clear();
        }

        public void Draw(
            OverlayDrawableKind? onlyKind = null,
            OverlayDrawableKind? skipKind = null,
            bool lateFillsOnly = false,
            bool excludeLateFills = false,
            Func<string?, bool>? familyVisible = null)
        {
            List<string>? expired = null;
            for (int i = 0; i < _drawOrder.Count; i++)
            {
                string key = _drawOrder[i];
                var item = _items[key];
                if (item.IsExpired)
                {
                    (expired ??= []).Add(key);
                    continue;
                }

                if (familyVisible is not null && !familyVisible(item.Family))
                    continue;

                OverlayDrawableKind kind = item.Drawable.Kind;
                if (onlyKind.HasValue && kind != onlyKind.Value)
                    continue;
                if (skipKind.HasValue && kind == skipKind.Value)
                    continue;
                if (lateFillsOnly && !OverlaySystem.IsLateFillKind(kind))
                    continue;
                if (excludeLateFills && OverlaySystem.IsLateFillKind(kind))
                    continue;

                item.Drawable.Draw();
            }

            if (expired != null)
            {
                foreach (string key in expired)
                {
                    _items.Remove(key);
                    _drawOrder.Remove(key);
                }
            }
        }
    }

    /// <summary>
    /// Client-side overlay catalog and draw implementation. Feature code registers drawables via the static catalog API;
    /// <see cref="ArchitectSystem"/> owns the interface-layer hooks.
    /// </summary>
    public sealed class OverlaySystem
    {
        private const int TilePixelSize = 16;
        private const int PointHandleSizePx = 12;
        private const int PointHandleHoverSizePx = 16;
        private static readonly Rectangle PixelSource = new(0, 0, 1, 1);

        private readonly OverlayCatalog _catalog = new();

        internal static bool IsMapDrawMode { get; private set; }

        private static MapOverlayDrawContext _mapDrawContext;

        private static OverlaySystem? _instance;
        private static OverlaySystem Instance => _instance ??= new OverlaySystem();

        /// <summary>Clears every registered overlay when leaving a world.</summary>
        public static void ClearCatalog()
        {
            // --- Drop catalog entries so nothing leaks across world loads.

            Instance._catalog.Clear();
        }

        /// <summary>Throws if new registrations are not allowed in the current context or <paramref name="id"/> is invalid.</summary>
        private static void ValidateRegistration(string id)
        {
            if (Main.dedServ)
                throw new InvalidOperationException("Overlay registration is not allowed on dedicated server.");
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Id cannot be null or empty.", nameof(id));
        }

        /// <summary>Throws on dedicated server or empty key; unregister is allowed while the menu or map is open.</summary>
        private static void ValidateUnregisterKey(string key)
        {
            if (Main.dedServ)
                throw new InvalidOperationException("Overlay catalog cannot be modified on dedicated server.");
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty.", nameof(key));
        }

        /// <inheritdoc cref="ValidateRegistration(string)"/>
        private static void ValidateRegistration(string id, Point[]? points)
        {
            ValidateRegistration(id);
            if (points is null)
                throw new ArgumentNullException(nameof(points));
            if (points.Length == 0)
                throw new ArgumentException("At least one point is required.", nameof(points));
        }

        /// <inheritdoc cref="ValidateRegistration(string)"/>
        private static void ValidateRegistration(string id, Rectangle tileBounds)
        {
            ValidateRegistration(id);
            if (tileBounds.IsEmpty)
                throw new ArgumentException("Tile bounds cannot be empty.", nameof(tileBounds));
        }

        /// <inheritdoc cref="ValidateRegistration(string)"/>
        private static void ValidateRegistration(string id, Action? draw)
        {
            ValidateRegistration(id);
            if (draw is null)
                throw new ArgumentNullException(nameof(draw));
        }

        /// <param name="family">Optional; when set, <paramref name="id"/> only needs to be unique within this family.</param>
        /// <param name="ttlMs">If provided, the overlay is automatically removed after this many milliseconds.</param>
        public static void RegPoints(string id, Point[] points, Color color, string? family = null, int? ttlMs = null)
        {
            ValidateRegistration(id, points);
            var copy = new Point[points.Length];
            Array.Copy(points, copy, points.Length);
            Instance._catalog.Register(family, id, new OverlayDrawable(OverlayDrawableKind.Point, copy, color, 0), ttlMs);
        }

        /// <inheritdoc cref="RegPoints"/>
        public static void RegTileFill(string id, Point[] tiles, Color fill, string? family = null, int? ttlMs = null)
        {
            ValidateRegistration(id, tiles);
            var copy = new Point[tiles.Length];
            Array.Copy(tiles, copy, tiles.Length);
            Instance._catalog.Register(family, id, new OverlayDrawable(OverlayDrawableKind.TileFill, copy, fill, 0), ttlMs);
        }

        /// <summary>
        /// Registers paste overlay tiles as batched rectangle fills with exact tile coverage.
        /// </summary>
        /// <inheritdoc cref="RegPoints"/>
        public static void RegPasteTileFill(string id, Point[] tiles, Color fill, string? family = null, int? ttlMs = null)
        {
            ValidateRegistration(id, tiles);
            Rectangle[] rects = TileOverlayRects.Decompose(tiles);
            var copy = new Rectangle[rects.Length];
            Array.Copy(rects, copy, rects.Length);
            Instance._catalog.Register(family, id, new OverlayDrawable(OverlayDrawableKind.TileRectFill, copy, fill, 0), ttlMs);
        }

        /// <inheritdoc cref="RegPoints"/>
        public static void RegTileRectFill(string id, Rectangle[] rects, Color fill, string? family = null, int? ttlMs = null)
        {
            ValidateRegistration(id);
            ArgumentNullException.ThrowIfNull(rects);
            var copy = new Rectangle[rects.Length];
            Array.Copy(rects, copy, rects.Length);
            Instance._catalog.Register(family, id, new OverlayDrawable(OverlayDrawableKind.TileRectFill, copy, fill, 0), ttlMs);
        }

        /// <inheritdoc cref="RegPoints"/>
        public static void RegTileBounds(string id, Rectangle tileBounds, Color color, int borderWidth, string? family = null, int? ttlMs = null)
        {
            ValidateRegistration(id, tileBounds);
            Instance._catalog.Register(family, id, new OverlayDrawable(OverlayDrawableKind.TileBounds, tileBounds, color, borderWidth), ttlMs);
        }

        /// <summary>Registers a tile rectangle outline only (no interior fill).</summary>
        /// <inheritdoc cref="RegPoints"/>
        public static void RegTileBorder(string id, Rectangle tileBounds, Color color, int borderWidth, string? family = null, int? ttlMs = null)
        {
            ValidateRegistration(id, tileBounds);
            Instance._catalog.Register(family, id, new OverlayDrawable(OverlayDrawableKind.TileBorder, tileBounds, color, borderWidth), ttlMs);
        }

        /// <inheritdoc cref="RegPoints"/>
        public static void RegGuardedTileMask(string id, Point[] tiles, string? family = null, int? ttlMs = null)
        {
            ValidateRegistration(id, tiles);
            var copy = new Point[tiles.Length];
            Array.Copy(tiles, copy, tiles.Length);
            Instance._catalog.Register(
                family,
                id,
                new OverlayDrawable(OverlayDrawableKind.GuardedTileMask, copy, new Color(0, 0, 0, 220), 0),
                ttlMs);
        }

        /// <inheritdoc cref="RegPoints"/>
        public static void RegScreen(string id, Action draw, string? family = null, int? ttlMs = null)
        {
            ValidateRegistration(id, draw);
            Instance._catalog.Register(family, id, new OverlayDrawable(OverlayDrawableKind.Screen, draw, default, 0), ttlMs);
        }

        /// <summary>Registers a projection map-color preview drawn only on the fullscreen map.</summary>
        /// <inheritdoc cref="RegPoints"/>
        public static void RegMapPayloadPreview(string id, Projection projection, string? family = null, int? ttlMs = null)
        {
            ValidateRegistration(id);
            ArgumentNullException.ThrowIfNull(projection);
            Instance._catalog.Register(
                family,
                id,
                new OverlayDrawable(OverlayDrawableKind.MapPayloadPreview, projection, default, 0),
                ttlMs);
        }

        public static void Unregister(string id, string? family = null)
        {
            ValidateUnregisterKey(id);
            Instance._catalog.Unregister(family, id);
        }

        public static void UnregisterFamily(string family)
        {
            ValidateUnregisterKey(family);
            Instance._catalog.UnregisterFamily(family);
        }

        /// <summary>
        /// Size in pixels used for point handle hit-testing (hover detection).
        /// </summary>
        public const int PointHandleHitTestSizePx = PointHandleSizePx;

        /// <summary>Draws non-late-fill overlays in the early interface layer.</summary>
        internal static void DrawOverlayEarly()
        {
            if (!ShouldDrawWorldOverlay())
                return;

            Instance._catalog.Draw(excludeLateFills: true, familyVisible: IsOverlayFamilyVisible);
        }

        /// <summary>Draws late-fill overlays near Mouse Text.</summary>
        internal static void DrawOverlayLate()
        {
            if (!ShouldDrawWorldOverlay())
                return;

            Instance._catalog.Draw(lateFillsOnly: true, familyVisible: IsOverlayFamilyVisible);
        }

        /// <summary>Draws catalog overlays on the fullscreen map (called from <see cref="ArchitectOverlayMapLayer"/>).</summary>
        public static void DrawMapOverlays(ref MapOverlayDrawContext context)
        {
            if (Main.gameMenu || !ArchitectMapTransform.IsMapViewActive)
                return;

            IsMapDrawMode = true;
            _mapDrawContext = context;
            try
            {
                Instance._catalog.Draw(
                    excludeLateFills: true,
                    familyVisible: IsOverlayFamilyVisible,
                    skipKind: OverlayDrawableKind.Screen);
                Instance._catalog.Draw(
                    lateFillsOnly: true,
                    familyVisible: IsOverlayFamilyVisible,
                    skipKind: OverlayDrawableKind.Screen);
            }
            finally
            {
                IsMapDrawMode = false;
                _mapDrawContext = default;
            }
        }

        internal static void DrawMapPayloadPreview(Projection projection)
        {
            PayloadPreview.DrawOnMap(ref _mapDrawContext, projection);
        }

        internal static void DrawOnMap(OverlayDrawableKind kind, object data, Color color, int borderWidth)
        {
            switch (kind)
            {
                case OverlayDrawableKind.Point:
                    DrawPointsOnMap((Point[])data, color);
                    break;
                case OverlayDrawableKind.TileFill:
                    DrawTileFillOnMap((Point[])data, color);
                    break;
                case OverlayDrawableKind.TileRectFill:
                    DrawTileRectFillOnMap((Rectangle[])data, color);
                    break;
                case OverlayDrawableKind.TileBounds:
                    DrawTileBoundsOnMap((Rectangle)data, color, borderWidth);
                    break;
                case OverlayDrawableKind.TileBorder:
                    DrawTileBorderOnMap((Rectangle)data, color, borderWidth);
                    break;
                case OverlayDrawableKind.GuardedTileMask:
                    DrawTileFillOnMap((Point[])data, color);
                    break;
            }
        }

        /// <summary>Tile-space rectangle converted to screen pixels (world view or fullscreen map).</summary>
        public static Rectangle GetTileRectangleScreenPixels(Rectangle tileBounds)
        {
            if (ArchitectMapTransform.IsMapViewActive)
            {
                float pxPerTile = ArchitectMapTransform.PixelsPerTile;
                Vector2 topLeft = ArchitectMapTransform.TileToScreen(new Point(tileBounds.X, tileBounds.Y));
                return new Rectangle(
                    (int)topLeft.X,
                    (int)topLeft.Y,
                    (int)(tileBounds.Width * pxPerTile),
                    (int)(tileBounds.Height * pxPerTile));
            }

            Vector2 pos = new Vector2(tileBounds.X, tileBounds.Y) * TilePixelSize - Main.screenPosition;
            return new Rectangle(
                (int)pos.X,
                (int)pos.Y,
                tileBounds.Width * TilePixelSize,
                tileBounds.Height * TilePixelSize);
        }

        private static float MapTileDrawScale()
        {
            float drawScale = _mapDrawContext.DrawScale;
            if (drawScale <= 0f)
                return _mapDrawContext.MapScale;

            return _mapDrawContext.MapScale / drawScale;
        }

        private static void DrawPointsOnMap(Point[] points, Color color)
        {
            var pixel = ModUtils.DummyTexture;
            var frame = new SpriteFrame(1, 1, 0, 0);
            Point mouse = new(Main.mouseX, Main.mouseY);

            for (int i = 0; i < points.Length; i++)
            {
                Point tile = points[i];
                bool hovered = GetPointHandleRect(tile, PointHandleSizePx).Contains(mouse);
                float scale = hovered ? PointHandleHoverSizePx : PointHandleSizePx;
                _mapDrawContext.Draw(
                    pixel,
                    new Vector2(tile.X + 0.5f, tile.Y + 0.5f),
                    color * 0.9f,
                    frame,
                    scale,
                    scale,
                    Alignment.Center);
            }
        }

        private static void DrawTileFillOnMap(Point[] tiles, Color fill)
        {
            var pixel = ModUtils.DummyTexture;
            var frame = new SpriteFrame(1, 1, 0, 0);
            float scale = MapTileDrawScale();

            for (int i = 0; i < tiles.Length; i++)
            {
                _mapDrawContext.Draw(
                    pixel,
                    new Vector2(tiles[i].X, tiles[i].Y),
                    fill,
                    frame,
                    scale,
                    scale,
                    Alignment.TopLeft);
            }
        }

        private static void DrawTileRectFillOnMap(Rectangle[] rects, Color fill)
        {
            var pixel = ModUtils.DummyTexture;
            var frame = new SpriteFrame(1, 1, 0, 0);
            float scale = MapTileDrawScale();

            for (int i = 0; i < rects.Length; i++)
            {
                Rectangle r = rects[i];
                for (int y = r.Top; y < r.Bottom; y++)
                {
                    for (int x = r.Left; x < r.Right; x++)
                    {
                        _mapDrawContext.Draw(
                            pixel,
                            new Vector2(x, y),
                            fill,
                            frame,
                            scale,
                            scale,
                            Alignment.TopLeft);
                    }
                }
            }

            if (ModContent.GetInstance<ClientPasteConfig>().Overlay.RectBorders)
            {
                Color borderColor = new(fill.R, fill.G, fill.B, (byte)255);
                for (int i = 0; i < rects.Length; i++)
                    DrawTileBorderOnMap(rects[i], borderColor, 2);
            }
        }

        private static void DrawTileBoundsOnMap(Rectangle tileBounds, Color color, int borderWidth)
        {
            DrawBorderedRectOnMap(tileBounds, color * 0.3f, color, borderWidth);
        }

        private static void DrawTileBorderOnMap(Rectangle tileBounds, Color color, int borderWidth)
        {
            DrawBorderedRectOnMap(tileBounds, Color.Transparent, color, borderWidth);
        }

        private static void DrawBorderedRectOnMap(Rectangle tileBounds, Color fill, Color outline, int borderWidth)
        {
            float pxPerTile = ArchitectMapTransform.PixelsPerTile;
            Vector2 topLeft = ArchitectMapTransform.TileToScreen(new Point(tileBounds.X, tileBounds.Y));
            var screenRect = new Rectangle(
                (int)topLeft.X,
                (int)topLeft.Y,
                (int)(tileBounds.Width * pxPerTile),
                (int)(tileBounds.Height * pxPerTile));

            var pixel = ModUtils.DummyTexture;
            Main.spriteBatch.Draw(pixel, screenRect, PixelSource, fill);

            if (borderWidth <= 0)
                return;

            int bw = Math.Max(1, (int)Math.Round(borderWidth * pxPerTile / TilePixelSize));
            Main.spriteBatch.Draw(pixel, new Rectangle(screenRect.Left, screenRect.Top, screenRect.Width, bw), PixelSource, outline);
            Main.spriteBatch.Draw(pixel, new Rectangle(screenRect.Left, screenRect.Bottom - bw, screenRect.Width, bw), PixelSource, outline);
            Main.spriteBatch.Draw(pixel, new Rectangle(screenRect.Left, screenRect.Top, bw, screenRect.Height), PixelSource, outline);
            Main.spriteBatch.Draw(pixel, new Rectangle(screenRect.Right - bw, screenRect.Top, bw, screenRect.Height), PixelSource, outline);
        }

        internal static bool IsLateFillKind(OverlayDrawableKind kind) =>
            kind is OverlayDrawableKind.TileFill
                or OverlayDrawableKind.TileRectFill
                or OverlayDrawableKind.GuardedTileMask;

        private static bool ShouldDrawWorldOverlay()
        {
            var ctx = new OverlayContext
            {
                IsGameMenu = Main.gameMenu,
                IsMapFullscreen = Main.mapFullscreen
            };
            return ctx.ShouldDrawWorldOverlay;
        }

        private static bool IsOverlayFamilyVisible(string? family)
        {
            if (Main.myPlayer < 0)
                return false;

            ArchitectOverlayScope heldScope = Main.LocalPlayer.GetModPlayer<ArchitectOverlayPlayer>().HeldOverlayScope;
            return ArchitectOverlayVisibility.IsFamilyVisible(family, heldScope);
        }

        internal static void DrawPoints(Point[] points, Color color)
        {
            var pixel = ModUtils.DummyTexture;
            for (int i = 0; i < points.Length; i++)
            {
                Point tile = points[i];
                Point mouse = new(Main.mouseX, Main.mouseY);
                bool hovered = GetPointHandleRect(tile, PointHandleSizePx).Contains(mouse);
                int sizePx = hovered ? PointHandleHoverSizePx : PointHandleSizePx;
                int centerX = tile.X * TilePixelSize + TilePixelSize / 2 - (int)Main.screenPosition.X;
                int centerY = tile.Y * TilePixelSize + TilePixelSize / 2 - (int)Main.screenPosition.Y;
                var r = new Rectangle(centerX - sizePx / 2, centerY - sizePx / 2, sizePx, sizePx);
                Main.spriteBatch.Draw(pixel, r, PixelSource, color * 0.9f);
            }
        }

        public static void DrawTileFill(Point[] tiles, Color fill)
        {
            var pixel = ModUtils.DummyTexture;
            var scale = new Vector2(TilePixelSize, TilePixelSize);
            for (int i = 0; i < tiles.Length; i++)
            {
                var t = tiles[i];
                var pos = new Vector2(t.X * TilePixelSize, t.Y * TilePixelSize) - Main.screenPosition;
                Main.spriteBatch.Draw(pixel, pos, PixelSource, fill, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }
        }

        /// <summary>
        /// Draws each tile-space rectangle as one scaled pixel quad.
        /// </summary>
        public static void DrawTileRectFill(Rectangle[] rects, Color fill)
        {
            // --- Render every rectangle with width/height scaled to tile pixels.

            var pixel = ModUtils.DummyTexture;
            for (int i = 0; i < rects.Length; i++)
            {
                Rectangle r = rects[i];
                var pos = new Vector2(r.X * TilePixelSize, r.Y * TilePixelSize) - Main.screenPosition;
                var scale = new Vector2(r.Width * TilePixelSize, r.Height * TilePixelSize);
                Main.spriteBatch.Draw(pixel, pos, PixelSource, fill, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }

            if (ModContent.GetInstance<ClientPasteConfig>().Overlay.RectBorders)
            {
                // Draw each batch rectangle outline on top of the fill pass.
                Color borderColor = new(fill.R, fill.G, fill.B, (byte)255);
                for (int i = 0; i < rects.Length; i++)
                    DrawTileBorder(rects[i], borderColor, 2);
            }
        }

        public static void DrawTileBounds(Rectangle tileBounds, Color color, int borderWidth)
        {
            var pos = new Vector2(tileBounds.X, tileBounds.Y);
            var size = new Vector2(tileBounds.Width, tileBounds.Height);
            ModUtils.DrawBorderedRect(Main.spriteBatch, color, pos, size, borderWidth);
        }

        public static void DrawTileBorder(Rectangle tileBounds, Color color, int borderWidth)
        {
            var pos = new Vector2(tileBounds.X, tileBounds.Y);
            var size = new Vector2(tileBounds.Width, tileBounds.Height);
            ModUtils.DrawBorderedRect(Main.spriteBatch, Color.Transparent, color, pos, size, borderWidth);
        }

        /// <summary>
        /// Returns the screen rectangle for a point handle at the given tile coordinate.
        /// </summary>
        public static Rectangle GetPointHandleRect(Point tile, int sizePx)
        {
            if (ArchitectMapTransform.IsMapViewActive)
            {
                float pxPerTile = ArchitectMapTransform.PixelsPerTile;
                Vector2 center = ArchitectMapTransform.TileToScreen(tile) + new Vector2(pxPerTile / 2f, pxPerTile / 2f);
                return new Rectangle(
                    (int)center.X - sizePx / 2,
                    (int)center.Y - sizePx / 2,
                    sizePx,
                    sizePx);
            }

            int centerX = tile.X * TilePixelSize + TilePixelSize / 2 - (int)Main.screenPosition.X;
            int centerY = tile.Y * TilePixelSize + TilePixelSize / 2 - (int)Main.screenPosition.Y;
            return new Rectangle(centerX - sizePx / 2, centerY - sizePx / 2, sizePx, sizePx);
        }
    }
}
