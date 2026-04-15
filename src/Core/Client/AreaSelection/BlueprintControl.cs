using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Camera-mode-like interactive selection for a player's Blueprint bounds.
    /// Driven by <see cref="ToolActionManager"/> for PIN/SET left-click/hold/release; right-click cancel is handled there.
    /// <para/>
    /// In-progress gestures can be canceled via <see cref="CancelGesture"/> (e.g. tool right-click).
    /// </summary>
    public sealed class BlueprintControl : ModSystem
    {
        private const int TilePixelSize = 16;
        private const int HandleThicknessPx = 8;
        private const int CornerHandleSizePx = 12;
        private const int PointHandleSizePx = 12;

        private enum Mode
        {
            None,
            EdgeSelection,
            Drag,
        }

        private enum DragKind
        {
            None,
            Move,
            ResizeLeft,
            ResizeRight,
            ResizeTop,
            ResizeBottom,
            ResizeTopLeft,
            ResizeTopRight,
            ResizeBottomLeft,
            ResizeBottomRight,
            PointA,
            PointB,
        }

        private enum HitRegion
        {
            None,
            PointA,
            PointB,
            CornerTopLeft,
            CornerTopRight,
            CornerBottomLeft,
            CornerBottomRight,
            EdgeLeft,
            EdgeRight,
            EdgeTop,
            EdgeBottom,
            Inside,
        }

        private sealed class PlayerState
        {
            public Mode Mode;
            public DragKind DragKind;

            public Point FixedPoint; // Used for EdgeSelection (fixed B).

            public Point DragStartCursorTile;
            public Rectangle DragStartBounds;
            public Point DragStartPointA;
            public Point DragStartPointB;
        }

        private readonly PlayerState[] _states = new PlayerState[Main.maxPlayers];

        public static BlueprintSystem BlueprintSystem => ModContent.GetInstance<BlueprintSystem>();

        /// <summary>Begins a PIN gesture (left click).</summary>
        public void BeginPin(Player player, Point cursorTile)
        {
            var state = GetState(player);

            Blueprint blueprint = BlueprintSystem.GetBlueprint(player);

            // Default gesture: place Edge B immediately and drag Edge A while holding.
            // PIN always starts fresh. SET only starts fresh if no blueprint exists.
            if (!blueprint.IsDefined)
            {
                BlueprintSystem.NewBlueprint(player);
            }

            BlueprintSystem.SetPoints(player, cursorTile, cursorTile);
            state.Mode = Mode.EdgeSelection;
            state.FixedPoint = cursorTile; // fixed B
        }

        /// <summary>Begins a SET gesture (left click).</summary>
        public void BeginSet(Player player, Point cursorTile)
        {
            var state = GetState(player);

            Blueprint blueprint = BlueprintSystem.GetBlueprint(player);

            if (blueprint.IsDefined)
            {
                Rectangle screenBounds = blueprint.GetScreenPixelBounds(Main.screenPosition, TilePixelSize);
                Point mouse = new(Main.mouseX, Main.mouseY);

                HitRegion region = HitTest(player, screenBounds, mouse);
                DragKind dragKind = DragKindFromHit(region);
                if (dragKind != DragKind.None)
                {
                    BeginDrag(state, dragKind, player, blueprint, cursorTile);
                    return;
                }
            }
        }

        /// <summary>Updates an in-progress PIN or SET gesture while the left button is held.</summary>
        public void UpdateGesture(Player player, Point cursorTile)
        {
            var state = GetState(player);

            Blueprint blueprint = BlueprintSystem.GetBlueprint(player);
            UpdateDrag(state, blueprint, player, cursorTile);
        }

        /// <summary>Ends the current left-button gesture (release).</summary>
        public void EndGesture(Player player)
        {
            var state = GetState(player);

            state.Mode = Mode.None;
            state.DragKind = DragKind.None;
        }

        /// <summary>
        /// Aborts an in-progress PIN edge-define or SET drag/resize and restores a sensible prior state.
        /// </summary>
        public void CancelGesture(Player player)
        {
            var state = GetState(player);
            if (state.Mode == Mode.None)
            {
                return;
            }

            var blueprint = BlueprintSystem.GetBlueprint(player);

            if (state.Mode == Mode.EdgeSelection)
            {
                BlueprintSystem.SetPoints(player, state.FixedPoint, state.FixedPoint);
                state.Mode = Mode.None;
                state.DragKind = DragKind.None;
                return;
            }

            if (state.Mode == Mode.Drag && blueprint.IsDefined)
            {
                BlueprintSystem.SetBounds(player, state.DragStartBounds);
                BlueprintSystem.SetPoints(player, state.DragStartPointA, state.DragStartPointB);
                state.Mode = Mode.None;
                state.DragKind = DragKind.None;
            }

            state.Mode = Mode.None;
            state.DragKind = DragKind.None;
        }

        private static void BeginDrag(PlayerState state, DragKind kind, Player player, Blueprint blueprint, Point cursorTile)
        {
            state.Mode = Mode.Drag;
            state.DragKind = kind;
            state.DragStartCursorTile = cursorTile;
            state.DragStartBounds = blueprint.Bounds;

            if (BlueprintSystem.TryGetPoints(player, out var a, out var b))
            {
                state.DragStartPointA = a;
                state.DragStartPointB = b;
            }
            else
            {
                // Shouldn't happen for a defined blueprint, but keep invariants safe.
                state.DragStartPointA = new Point(blueprint.Bounds.Left, blueprint.Bounds.Top);
                state.DragStartPointB = new Point(blueprint.Bounds.Right - 1, blueprint.Bounds.Bottom - 1);
            }
        }

        private static void UpdateDrag(PlayerState state, Blueprint blueprint, Player player, Point cursorTile)
        {

            if (state.Mode == Mode.EdgeSelection)
            {
                BlueprintSystem.SetPoints(player, cursorTile, state.FixedPoint);
                return;
            }

            if (state.Mode != Mode.Drag || state.DragKind == DragKind.None || !blueprint.IsDefined)
            {
                return;
            }

            switch (state.DragKind)
            {
                case DragKind.PointA:
                    BlueprintSystem.SetPoints(player, cursorTile, state.DragStartPointB);
                    return;

                case DragKind.PointB:
                    BlueprintSystem.SetPoints(player, state.DragStartPointA, cursorTile);
                    return;

                case DragKind.Move:
                {
                    Point delta = new(cursorTile.X - state.DragStartCursorTile.X, cursorTile.Y - state.DragStartCursorTile.Y);
                    Rectangle b = state.DragStartBounds;
                    var moved = new Rectangle(b.X + delta.X, b.Y + delta.Y, b.Width, b.Height);
                    BlueprintSystem.SetBounds(player, moved);
                    return;
                }

                default:
                {
                    Rectangle r = ResizeFromCursor(state.DragStartBounds, state.DragKind, cursorTile);
                    BlueprintSystem.SetBounds(player, r);
                    return;
                }
            }
        }

        private static DragKind DragKindFromHit(HitRegion region)
        {
            return region switch
            {
                HitRegion.PointA => DragKind.PointA,
                HitRegion.PointB => DragKind.PointB,
                HitRegion.Inside => DragKind.Move,

                HitRegion.EdgeLeft => DragKind.ResizeLeft,
                HitRegion.EdgeRight => DragKind.ResizeRight,
                HitRegion.EdgeTop => DragKind.ResizeTop,
                HitRegion.EdgeBottom => DragKind.ResizeBottom,

                HitRegion.CornerTopLeft => DragKind.ResizeTopLeft,
                HitRegion.CornerTopRight => DragKind.ResizeTopRight,
                HitRegion.CornerBottomLeft => DragKind.ResizeBottomLeft,
                HitRegion.CornerBottomRight => DragKind.ResizeBottomRight,

                _ => DragKind.None,
            };
        }

        private static HitRegion HitTest(Player player, Rectangle screenBounds, Point mouse)
        {
            // Point A/B handles (prefer these over corner handles so dragging endpoints feels "sticky").
            if (BlueprintSystem.TryGetPoints(player, out var a, out var b))
            {
                if (GetPointHandleRect(a).Contains(mouse))
                {
                    return HitRegion.PointA;
                }

                if (GetPointHandleRect(b).Contains(mouse))
                {
                    return HitRegion.PointB;
                }
            }

            // Corner handles.
            Rectangle topLeft = new(screenBounds.Left - CornerHandleSizePx / 2, screenBounds.Top - CornerHandleSizePx / 2, CornerHandleSizePx, CornerHandleSizePx);
            Rectangle topRight = new(screenBounds.Right - CornerHandleSizePx / 2, screenBounds.Top - CornerHandleSizePx / 2, CornerHandleSizePx, CornerHandleSizePx);
            Rectangle bottomLeft = new(screenBounds.Left - CornerHandleSizePx / 2, screenBounds.Bottom - CornerHandleSizePx / 2, CornerHandleSizePx, CornerHandleSizePx);
            Rectangle bottomRight = new(screenBounds.Right - CornerHandleSizePx / 2, screenBounds.Bottom - CornerHandleSizePx / 2, CornerHandleSizePx, CornerHandleSizePx);

            if (topLeft.Contains(mouse)) return HitRegion.CornerTopLeft;
            if (topRight.Contains(mouse)) return HitRegion.CornerTopRight;
            if (bottomLeft.Contains(mouse)) return HitRegion.CornerBottomLeft;
            if (bottomRight.Contains(mouse)) return HitRegion.CornerBottomRight;

            // Edge handles.
            Rectangle edgeTop = new(screenBounds.Left + CornerHandleSizePx, screenBounds.Top - HandleThicknessPx / 2, Math.Max(0, screenBounds.Width - 2 * CornerHandleSizePx), HandleThicknessPx);
            Rectangle edgeBottom = new(screenBounds.Left + CornerHandleSizePx, screenBounds.Bottom - HandleThicknessPx / 2, Math.Max(0, screenBounds.Width - 2 * CornerHandleSizePx), HandleThicknessPx);
            Rectangle edgeLeft = new(screenBounds.Left - HandleThicknessPx / 2, screenBounds.Top + CornerHandleSizePx, HandleThicknessPx, Math.Max(0, screenBounds.Height - 2 * CornerHandleSizePx));
            Rectangle edgeRight = new(screenBounds.Right - HandleThicknessPx / 2, screenBounds.Top + CornerHandleSizePx, HandleThicknessPx, Math.Max(0, screenBounds.Height - 2 * CornerHandleSizePx));

            if (edgeLeft.Contains(mouse)) return HitRegion.EdgeLeft;
            if (edgeRight.Contains(mouse)) return HitRegion.EdgeRight;
            if (edgeTop.Contains(mouse)) return HitRegion.EdgeTop;
            if (edgeBottom.Contains(mouse)) return HitRegion.EdgeBottom;

            // Inside: move.
            Rectangle inside = screenBounds;
            inside.Inflate(-HandleThicknessPx, -HandleThicknessPx);
            if (inside.Contains(mouse))
            {
                return HitRegion.Inside;
            }

            return HitRegion.None;
        }

        private static Rectangle GetPointHandleRect(Point tile)
        {
            int centerX = tile.X * TilePixelSize + TilePixelSize / 2 - (int)Main.screenPosition.X;
            int centerY = tile.Y * TilePixelSize + TilePixelSize / 2 - (int)Main.screenPosition.Y;
            return new Rectangle(centerX - PointHandleSizePx / 2, centerY - PointHandleSizePx / 2, PointHandleSizePx, PointHandleSizePx);
        }

        private static Rectangle ResizeFromCursor(Rectangle start, DragKind kind, Point cursorTile)
        {
            int left = start.Left;
            int top = start.Top;
            int right = start.Right - 1;   // inclusive
            int bottom = start.Bottom - 1; // inclusive

            switch (kind)
            {
                case DragKind.ResizeLeft:
                    left = Math.Min(cursorTile.X, right);
                    break;

                case DragKind.ResizeRight:
                    right = Math.Max(cursorTile.X, left);
                    break;

                case DragKind.ResizeTop:
                    top = Math.Min(cursorTile.Y, bottom);
                    break;

                case DragKind.ResizeBottom:
                    bottom = Math.Max(cursorTile.Y, top);
                    break;

                case DragKind.ResizeTopLeft:
                    left = Math.Min(cursorTile.X, right);
                    top = Math.Min(cursorTile.Y, bottom);
                    break;

                case DragKind.ResizeTopRight:
                    right = Math.Max(cursorTile.X, left);
                    top = Math.Min(cursorTile.Y, bottom);
                    break;

                case DragKind.ResizeBottomLeft:
                    left = Math.Min(cursorTile.X, right);
                    bottom = Math.Max(cursorTile.Y, top);
                    break;

                case DragKind.ResizeBottomRight:
                    right = Math.Max(cursorTile.X, left);
                    bottom = Math.Max(cursorTile.Y, top);
                    break;
            }

            return new Rectangle(left, top, right - left + 1, bottom - top + 1);
        }

        private PlayerState GetState(Player player)
        {
            ArgumentNullException.ThrowIfNull(player);

            if (player.whoAmI != Main.myPlayer || Main.gameMenu)
            {
                throw new InvalidOperationException("Player is not valid");
            }

            int index = player.whoAmI;
            if (index < 0 || index >= _states.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(player), $"Invalid player index: {index}");
            }

            return _states[index] ??= new PlayerState();
        }
    }
}
