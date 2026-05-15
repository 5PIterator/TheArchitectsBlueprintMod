using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
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

        public void Draw()
        {
            switch (_kind)
            {
                case OverlayDrawableKind.Point:
                    OverlaySystem.DrawPoints((Point[])_data, _color);
                    break;
                case OverlayDrawableKind.TileFill:
                    OverlaySystem.DrawTileFill((Point[])_data, _color);
                    break;
                case OverlayDrawableKind.TileBounds:
                    OverlaySystem.DrawTileBounds((Rectangle)_data, _color, _borderWidth);
                    break;
                case OverlayDrawableKind.TileBorder:
                    OverlaySystem.DrawTileBorder((Rectangle)_data, _color, _borderWidth);
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
        TileBounds,
        TileBorder,
        Screen,
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

        public void Draw()
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
    /// Hooks the overlay <see cref="OverlayCatalog"/> into the game interface layer. Feature code registers drawables via the static catalog API.
    /// </summary>
    public sealed class OverlaySystem : ModSystem
    {
        private const int TilePixelSize = 16;
        private const int PointHandleSizePx = 12;
        private const int PointHandleHoverSizePx = 16;
        private static readonly Rectangle PixelSource = new(0, 0, 1, 1);

        private readonly OverlayCatalog _catalog = new();

        private static OverlaySystem Instance => ModContent.GetInstance<OverlaySystem>();

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
        public static void RegScreen(string id, Action draw, string? family = null, int? ttlMs = null)
        {
            ValidateRegistration(id, draw);
            Instance._catalog.Register(family, id, new OverlayDrawable(OverlayDrawableKind.Screen, draw, default, 0), ttlMs);
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

        public override void OnWorldUnload()
        {
            _catalog.Clear();
        }

        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            int insertIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Interface Logic 1"));
            if (insertIndex == -1) insertIndex = 0;

            layers.Insert(insertIndex, new LegacyGameInterfaceLayer(
                "TheArchitectsBlueprint: Overlay",
                () => { DrawOverlay(); return true; },
                InterfaceScaleType.Game)
            );
        }

        private static void DrawOverlay()
        {
            var ctx = new OverlayContext
            {
                IsGameMenu = Main.gameMenu,
                IsMapFullscreen = Main.mapFullscreen
            };
            if (!ctx.ShouldDrawWorldOverlay)
                return;

            Instance._catalog.Draw();
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
            int centerX = tile.X * TilePixelSize + TilePixelSize / 2 - (int)Main.screenPosition.X;
            int centerY = tile.Y * TilePixelSize + TilePixelSize / 2 - (int)Main.screenPosition.Y;
            return new Rectangle(centerX - sizePx / 2, centerY - sizePx / 2, sizePx, sizePx);
        }
    }
}
