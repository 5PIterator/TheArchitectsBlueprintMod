using System;
using System.Collections.Generic;
using ArchitectsStencilMod.Core;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
using Microsoft.Xna.Framework.Graphics;
using ArchitectsStencilMod.Utils;

namespace ArchitectsStencilMod.UI
{
    /// <summary>
    /// Channel-based overlay API. Call DrawPoint, DrawTile, DrawTileBounds to register content;
    /// call EraseChannel to clear. Content persists for the given duration (frames).
    /// </summary>
    public static class Overlay
    {
        /// <summary>
        /// Draws point handles at the given tile coordinates. Hover state is computed at draw time.
        /// </summary>
        public static void DrawPoint(IReadOnlyList<Point> points, Color color, string channel, int durationFrames = 1)
        {
            if (points is null || points.Count == 0 || string.IsNullOrEmpty(channel)) return;
            var list = GetOrCreateChannel(channel);
            var copy = new Point[points.Count];
            for (int i = 0; i < points.Count; i++) copy[i] = points[i];
            list.Add(new OverlayEntry(OverlayKind.Point, copy, color, durationFrames));
        }

        /// <summary>
        /// Draws filled tiles at the given world coordinates.
        /// </summary>
        public static void DrawTile(IReadOnlyList<Point> tiles, Color fill, string channel, int durationFrames = 1)
        {
            if (tiles is null || tiles.Count == 0 || string.IsNullOrEmpty(channel)) return;
            var list = GetOrCreateChannel(channel);
            var copy = new Point[tiles.Count];
            for (int i = 0; i < tiles.Count; i++) copy[i] = tiles[i];
            list.Add(new OverlayEntry(OverlayKind.TileFill, copy, fill, durationFrames));
        }

        /// <summary>
        /// Draws a bordered rectangle in tile-space.
        /// </summary>
        public static void DrawTileBounds(Rectangle tileBounds, Color color, int borderWidth, string channel, int durationFrames = 1)
        {
            if (tileBounds.IsEmpty || string.IsNullOrEmpty(channel)) return;
            var list = GetOrCreateChannel(channel);
            list.Add(new OverlayEntry(OverlayKind.TileBounds, tileBounds, color, durationFrames, borderWidth));
        }

        /// <summary>
        /// Draws custom screen-space content (e.g. inventory label).
        /// </summary>
        public static void DrawScreen(Action draw, string channel, int durationFrames = 1)
        {
            if (draw is null || string.IsNullOrEmpty(channel)) return;
            var list = GetOrCreateChannel(channel);
            list.Add(new OverlayEntry(OverlayKind.Screen, draw, default, durationFrames));
        }

        /// <summary>
        /// Clears all content in the given channel.
        /// </summary>
        public static void EraseChannel(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return;
            _channels.Remove(channel);
        }

        /// <summary>
        /// Clears all channels.
        /// </summary>
        public static void EraseAll()
        {
            _channels.Clear();
        }

        private static readonly Dictionary<string, List<OverlayEntry>> _channels = new();
        private static readonly string[] _channelOrder = ["blueprint", "projection", "selection", "pasting"];

        private static List<OverlayEntry> GetOrCreateChannel(string channel)
        {
            if (!_channels.TryGetValue(channel, out var list))
            {
                list = [];
                _channels[channel] = list;
            }
            return list;
        }

        internal static void DrawAll()
        {
            for (int c = 0; c < _channelOrder.Length; c++)
            {
                if (!_channels.TryGetValue(_channelOrder[c], out var list)) continue;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var e = list[i];
                    e.Draw();
                    e.RemainingFrames--;
                    if (e.RemainingFrames <= 0)
                        list.RemoveAt(i);
                }
                if (list.Count == 0)
                    _channels.Remove(_channelOrder[c]);
            }
        }

        private enum OverlayKind { Point, TileFill, TileBounds, Screen }

        private class OverlayEntry
        {
            public int RemainingFrames;
            private readonly OverlayKind _kind;
            private readonly object _data;
            private readonly Color _color;
            private readonly int _borderWidth;

            public OverlayEntry(OverlayKind kind, object data, Color color, int duration, int borderWidth = 0)
            {
                _kind = kind;
                _data = data;
                _color = color;
                RemainingFrames = duration;
                _borderWidth = borderWidth;
            }

            public void Draw()
            {
                switch (_kind)
                {
                    case OverlayKind.Point:
                        OverlaySystem.DrawPoints((Point[])_data, _color);
                        break;
                    case OverlayKind.TileFill:
                        OverlaySystem.DrawTileFill((Point[])_data, _color);
                        break;
                    case OverlayKind.TileBounds:
                        OverlaySystem.DrawTileBounds((Rectangle)_data, _color, _borderWidth);
                        break;
                    case OverlayKind.Screen:
                        ((Action)_data)();
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Context computed at draw time. OverlaySystem uses this to adapt (e.g. skip when map is open).
    /// </summary>
    public readonly struct OverlayContext
    {
        public bool IsGameMenu { get; init; }
        public bool IsMapFullscreen { get; init; }
        public bool ShouldDrawWorldOverlay => !IsGameMenu && !IsMapFullscreen;
    }

    /// <summary>
    /// Draws overlay content for the mod. Pushes overlay data each frame, then draws during the interface layer phase.
    /// </summary>
    public sealed class OverlaySystem : ModSystem
    {
        private const int TilePixelSize = 16;
        private const int PointHandleSizePx = 12;
        private const int PointHandleHoverSizePx = 16;
        private static readonly Rectangle PixelSource = new(0, 0, 1, 1);

        /// <summary>
        /// Size in pixels used for point handle hit-testing (hover detection).
        /// </summary>
        public const int PointHandleHitTestSizePx = PointHandleSizePx;

        public override void PostUpdatePlayers()
        {
            if (Main.gameMenu) return;
            PushAllOverlays(Main.LocalPlayer);
        }

        private static void PushAllOverlays(Player player)
        {
            Overlay.EraseAll();

            var blueprintSystem = ModContent.GetInstance<BlueprintSystem>();
            Blueprint blueprint = blueprintSystem.GetActiveBlueprint(player);
            if (blueprint.IsDefined)
            {
                Overlay.DrawTileBounds(blueprint.Bounds, Color.White, 2, "blueprint");
                PushUnselectedTiles(blueprint.Tiles, blueprint.Bounds.Location, Color.Black * 0.35f, "blueprint");
                if (blueprintSystem.TryGetPoints(player, out var a, out var b))
                {
                    Overlay.DrawPoint([a], Color.Cyan, "blueprint");
                    Overlay.DrawPoint([b], Color.Orange, "blueprint");
                }
            }

            var projectionSystem = ModContent.GetInstance<ProjectionSystem>();
            var projections = projectionSystem.GetProjections(player);
            for (int i = 0; i < projections.Count; i++)
            {
                var p = projections[i];
                if (!p.Bounds.IsEmpty)
                {
                    Color c = p.IsLocked ? Color.Gold : (p.Kind == ProjectionKind.Copy ? Color.LimeGreen : Color.Red);
                    Overlay.DrawTileBounds(p.Bounds, c, 2, "projection");
                }
                PushUnselectedTiles(p.Tiles, p.Bounds.Location, Color.Black * 0.25f, "projection");
            }

            var pastingSystem = ModContent.GetInstance<PastingSystem>();
            pastingSystem.PushOverlay(player);

            var selectionSystem = ModContent.GetInstance<SelectionSystem>();
            if (selectionSystem.TryGetPreview(player, out var action, out var rect, out var targetBounds))
            {
                Color c = action == ToolActionManager.ToolAction.ADD ? Color.LimeGreen : Color.Red;
                Rectangle clamped = Rectangle.Intersect(targetBounds, rect);
                Overlay.DrawTileBounds(clamped, c, 2, "selection");
            }
        }

        private static void PushUnselectedTiles(IReadOnlyList<Blueprint.BoundedTile> tiles, Point boundsOrigin, Color fill, string channel)
        {
            if (tiles is null || tiles.Count == 0) return;
            var points = new List<Point>(tiles.Count);
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                if (t.Selected) continue;
                points.Add(new Point(boundsOrigin.X + t.Relative.X, boundsOrigin.Y + t.Relative.Y));
            }
            if (points.Count > 0)
                Overlay.DrawTile(points, fill, channel);
        }

        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            int insertIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Interface Logic 1"));
            if (insertIndex == -1) insertIndex = 0;

            layers.Insert(insertIndex, new LegacyGameInterfaceLayer(
                "ArchitectsStencil: Overlay",
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
            {
                Overlay.EraseAll();
                return;
            }

            Overlay.DrawAll();
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
