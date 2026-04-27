using System;
using ArchitectsStencilMod.UI;
using ArchitectsStencilMod.ModUtils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// COPY-tool gestures for projections: create from selection, pick up and move, cancel restores start-of-drag position.
    /// Hit-testing uses <see cref="BlueprintSystem"/> (same topmost order as INCLUDE/EXCLUDE).
    /// Mirrors <see cref="SelectionControl"/> (gesture state here; list data in <see cref="ProjectionSystem"/>).
    /// Future: handles, resize, and on-projection buttons follow the same Control vs System split.
    /// </summary>
    public sealed class ProjectionControl : ModSystem
    {
        private const int ButtonWidth = 58;
        private const int ButtonHeight = 24;
        private const int ButtonGap = 6;
        private const int ButtonOffset = 8;
        private const int TilePixelSize = 16;
        private static readonly Rectangle PixelSource = new(0, 0, 1, 1);

        private enum Mode
        {
            None,
            Drag,
        }

        private enum DragKind
        {
            None,
            Move,
        }

        private sealed class PlayerState
        {
            public Mode Mode;
            public DragKind DragKind;
            public Projection DraggingProjection;
            public Point DragOffsetFromCursor;
            public Rectangle DragStartBounds;
        }

        private readonly PlayerState[] _states = new PlayerState[Main.maxPlayers];
        private bool _previousLeftDown;

        public static ProjectionSystem ProjectionSystem => ModContent.GetInstance<ProjectionSystem>();

        private static BlueprintSystem BlueprintSystem => ModContent.GetInstance<BlueprintSystem>();

        public override void UpdateUI(GameTime gameTime)
        {
            // --- Treat projection import/export buttons as UI clicks before tool gestures can consume them.

            bool leftClicked = Main.mouseLeft && !_previousLeftDown;
            _previousLeftDown = Main.mouseLeft;
            if (!leftClicked || Main.gameMenu || PayloadFileDialogs.IsActive)
                return;

            Player player = Main.LocalPlayer;
            if (player is null || player.whoAmI != Main.myPlayer)
                return;

            if (!ProjectionSystem.TryGetFrontmostProjection(player, out Projection projection) || projection.IsLocked)
                return;

            Rectangle importButton = GetImportButton(projection);
            Rectangle exportButton = GetExportButton(projection);
            Point mouse = new(Main.mouseX, Main.mouseY);

            if (importButton.Contains(mouse))
            {
                player.mouseInterface = true;
                PayloadSerialization.BeginImport(projection);
            }
            else if (exportButton.Contains(mouse))
            {
                player.mouseInterface = true;
                PayloadSerialization.BeginExport(projection);
            }
        }

        /// <summary>
        /// Begins a COPY gesture (left click): pick up the topmost <see cref="Projection"/> under the cursor, or
        /// create one from the topmost <see cref="Selection"/> when the cursor is on the selection and not covered by a projection.
        /// </summary>
        public void BeginCopy(Player player, Point cursorTile)
        {
            var state = GetState(player);
            if (state.Mode != Mode.None)
            {
                return;
            }

            if (!BlueprintSystem.TryGetTopmostBlueprintAt(player, cursorTile, out Blueprint? top) || top is null)
            {
                return;
            }

            var sys = ProjectionSystem;

            switch (top)
            {
                case Projection topmost:
                    if (topmost.IsLocked)
                    {
                        return;
                    }

                    sys.SetActiveProjection(player, topmost);
                    sys.BringProjectionToFront(player, topmost);
                    state.Mode = Mode.Drag;
                    state.DragKind = DragKind.Move;
                    state.DraggingProjection = topmost;
                    state.DragStartBounds = topmost.Area;
                    state.DragOffsetFromCursor = new Point(cursorTile.X - topmost.Area.X, cursorTile.Y - topmost.Area.Y);
                    break;

                case Selection:
                {
                    Projection projection = sys.NewProjection(player, cursorTile);
                    if (projection is null)
                    {
                        return;
                    }

                    state.Mode = Mode.Drag;
                    state.DragKind = DragKind.Move;
                    state.DraggingProjection = projection;
                    state.DragStartBounds = projection.Area;
                    state.DragOffsetFromCursor = new Point(cursorTile.X - projection.Area.X, cursorTile.Y - projection.Area.Y);
                    break;
                }

                default:
                    // COPY only interacts with live projections and the world selection, not other registered blueprints.
                    return;
            }

            if (state.DraggingProjection is not null && state.DraggingProjection.IsDefined)
            {
                Point origin = new(cursorTile.X - state.DragOffsetFromCursor.X, cursorTile.Y - state.DragOffsetFromCursor.Y);
                state.DraggingProjection.MoveTo(origin);
            }
        }

        /// <summary>Updates an in-progress COPY drag while the left button is held.</summary>
        public void UpdateGesture(Player player, Point cursorTile)
        {
            var state = GetState(player);
            if (state.Mode != Mode.Drag || state.DraggingProjection is null || !state.DraggingProjection.IsDefined)
            {
                return;
            }

            Point origin = new(cursorTile.X - state.DragOffsetFromCursor.X, cursorTile.Y - state.DragOffsetFromCursor.Y);
            state.DraggingProjection.MoveTo(origin);
        }

        /// <summary>Ends the current left-button COPY gesture (release).</summary>
        public void EndGesture(Player player)
        {
            var state = GetState(player);
            state.Mode = Mode.None;
            state.DragKind = DragKind.None;
            state.DraggingProjection = null;
        }

        /// <summary>
        /// Aborts an in-progress COPY drag and restores the projection to its position when the drag began.
        /// </summary>
        public bool CancelGesture(Player player)
        {
            var state = GetState(player);
            if (state.Mode != Mode.Drag || state.DraggingProjection is null || !state.DraggingProjection.IsDefined)
            {
                return false;
            }

            state.DraggingProjection.MoveTo(state.DragStartBounds.Location);
            EndGesture(player);
            return true;
        }

        /// <summary>True while the local player is mid-COPY drag.</summary>
        public bool IsActivelyDragging(Player player)
        {
            if (player?.whoAmI is not int i || i < 0 || i >= _states.Length)
            {
                return false;
            }

            return _states[i] is { Mode: Mode.Drag, DraggingProjection: not null };
        }

        /// <summary>
        /// Called when a <see cref="Projection"/> is removed from the world list; clears COPY gesture if it referenced that instance.
        /// </summary>
        internal void InvalidateGestureIfDragging(Player player, Projection removed)
        {
            ArgumentNullException.ThrowIfNull(player);
            ArgumentNullException.ThrowIfNull(removed);

            int i = player.whoAmI;
            if ((uint)i >= (uint)_states.Length)
            {
                return;
            }

            if (_states[i] is not { } gs)
            {
                return;
            }

            if (gs.Mode != Mode.Drag || !ReferenceEquals(gs.DraggingProjection, removed))
            {
                return;
            }

            gs.Mode = Mode.None;
            gs.DragKind = DragKind.None;
            gs.DraggingProjection = null;
        }

        /// <summary>Clears COPY gesture state for every player slot (world unload).</summary>
        internal void ClearAllGestureSlots()
        {
            for (int i = 0; i < _states.Length; i++)
            {
                if (_states[i] is not { } gs)
                {
                    continue;
                }

                gs.Mode = Mode.None;
                gs.DragKind = DragKind.None;
                gs.DraggingProjection = null;
            }
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

        /// <summary>
        /// Draws import/export buttons for the front-most local projection on the overlay layer
        /// (<see cref="OverlaySystem"/>).
        /// </summary>
        public static void DrawPayloadButtonsInOverlay()
        {
            // --- Draw nothing while another payload modal is active or no usable projection is present.

            if (Main.gameMenu || PayloadFileDialogs.IsActive)
                return;

            Player player = Main.LocalPlayer;
            if (player is null || player.whoAmI != Main.myPlayer)
                return;

            if (!ProjectionSystem.TryGetFrontmostProjection(player, out Projection projection) || projection.IsLocked)
                return;

            DrawButton(GetImportButton(projection), "Import");
            DrawButton(GetExportButton(projection), "Export");
        }

        /// <summary>Returns the screen rectangle for the import button.</summary>
        private static Rectangle GetImportButton(Projection projection)
        {
            // --- Anchor the button group to the projection's top-right screen edge.

            Point origin = GetButtonGroupOrigin(projection);
            return new Rectangle(origin.X, origin.Y, ButtonWidth, ButtonHeight);
        }

        /// <summary>Returns the screen rectangle for the export button.</summary>
        private static Rectangle GetExportButton(Projection projection)
        {
            // --- Place export to the right of import.

            Point origin = GetButtonGroupOrigin(projection);
            return new Rectangle(origin.X + ButtonWidth + ButtonGap, origin.Y, ButtonWidth, ButtonHeight);
        }

        /// <summary>Returns the clamped screen origin for the projection button group.</summary>
        private static Point GetButtonGroupOrigin(Projection projection)
        {
            // --- Convert projection tile bounds to screen pixels and clamp the group on screen.

            int groupWidth = ButtonWidth * 2 + ButtonGap;
            int x = (int)(projection.Area.Right * TilePixelSize - Main.screenPosition.X) + ButtonOffset;
            int y = (int)(projection.Area.Y * TilePixelSize - Main.screenPosition.Y);

            x = Math.Clamp(x, ButtonOffset, Math.Max(ButtonOffset, Main.screenWidth - groupWidth - ButtonOffset));
            y = Math.Clamp(y, ButtonOffset, Math.Max(ButtonOffset, Main.screenHeight - ButtonHeight - ButtonOffset));
            return new Point(x, y);
        }

        /// <summary>Draws one projection payload button.</summary>
        private static void DrawButton(Rectangle bounds, string label)
        {
            // --- Draw a compact filled button with hover feedback.

            bool hovered = bounds.Contains(new Point(Main.mouseX, Main.mouseY));
            Texture2D pixel = ModUtils.ModUtils.DummyTexture;
            Color fill = hovered ? new Color(90, 86, 126, 245) : new Color(44, 42, 64, 235);
            Color border = hovered ? Color.White : new Color(155, 150, 190, 255);
            Main.spriteBatch.Draw(pixel, bounds, PixelSource, fill);
            Main.spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), PixelSource, border);
            Main.spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), PixelSource, border);
            Main.spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), PixelSource, border);
            Main.spriteBatch.Draw(pixel, new Rectangle(bounds.Right - 1, bounds.Y, 1, bounds.Height), PixelSource, border);

            DynamicSpriteFont font = FontAssets.MouseText.Value;
            Vector2 size = font.MeasureString(label) * 0.72f;
            Vector2 pos = new(bounds.Center.X - size.X * 0.5f, bounds.Center.Y - size.Y * 0.5f);
            Main.spriteBatch.DrawString(font, label, pos, Color.White, 0f, Vector2.Zero, 0.72f, SpriteEffects.None, 0f);
        }
    }
}
