using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;
using Terraria.ModLoader;
using Terraria.GameContent.UI;
using Terraria.GameInput;
using ArchitectsStencilMod.Core;

namespace ArchitectsStencilMod.ModUtils
{
    /// <summary>
    /// Lightweight "action wheel" UI.
    /// Based on Terraria's `WiresRadial` ("flower" wheel) behavior: center position,
    /// slice hover selection, gamepad thumbstick cursor, and click-outside to close.
    /// </summary>
    public sealed class ActionWheel
    {
        private static ActionWheel _active;
        private static int _ownerPlayer = -1;
        private static int _ownerHeldItemType;

        private static PendingOpenRequest _pendingOpen;

        private sealed class PendingOpenRequest
        {
            public int OwnerPlayer;
            public int OwnerHeldItemType;
            public List<ActionWheelEntry> Entries;
            public ActionWheelEntry CenterEntry;
            public ActionWheelStyle Style;
        }

        private readonly List<ActionWheelEntry> _entries;
        private readonly List<ActionWheelEntry> _visibleEntries = [];
        private readonly ActionWheelEntry _centerEntry;
        private readonly ActionWheelStyle _style;
        private ulong _openedAtUpdateCount;

        public bool Active { get; private set; }
        public bool OnWheelMenu { get; private set; }
        public Vector2 Position { get; private set; }
        public int HoveredIndex { get; private set; } = -1;
        public bool CenterHovered { get; private set; }

        public IReadOnlyList<ActionWheelEntry> Entries => _entries;
        public ActionWheelStyle Style => _style;

        public static bool IsActive => _active != null && _active.Active;
        public static bool IsPendingOpen => _pendingOpen != null;
        internal static ActionWheel ActiveInstance => _active;
        internal static int OwnerPlayer => _ownerPlayer;
        internal static int OwnerHeldItemType => _ownerHeldItemType;

        public static void OpenToolActionWheel(
            int ownerPlayer,
            int ownerHeldItemType,
            IReadOnlyList<ToolAction> actions,
            Func<ToolAction> getCurrent,
            Action<ToolAction> setCurrent,
            Func<ToolAction, bool> isVisibleForAction = null,
            ActionWheelStyle style = null)
        {
            ArgumentNullException.ThrowIfNull(actions);
            if (actions.Count == 0) throw new ArgumentException("Action wheel must have at least one action.", nameof(actions));
            ArgumentNullException.ThrowIfNull(getCurrent);
            ArgumentNullException.ThrowIfNull(setCurrent);

            var entries = new List<ActionWheelEntry>(actions.Count);
            for (int i = 0; i < actions.Count; i++)
            {
                ToolAction a = actions[i];
                entries.Add(new ActionWheelEntry(
                    ToolActionDefinitions.GetActionId(a),
                    ToolActionDefinitions.GetActionLabel(a),
                    onSelect: () => setCurrent(a),
                    isSelected: () => getCurrent() == a,
                    icon: null,
                    isVisible: isVisibleForAction != null ? () => isVisibleForAction(a) : null
                ));
            }

            RequestOpen(ownerPlayer, ownerHeldItemType, entries, centerEntry: null, style);
        }

        /// <summary>
        /// Creates and opens a new active wheel.
        /// If the same owner requests the wheel again while it is already open, the request is ignored.
        /// If a different owner requests a wheel while one is open, the current wheel is replaced.
        /// </summary>
        public static void OpenNew(int ownerPlayer, int ownerHeldItemType, IEnumerable<ActionWheelEntry> entries, ActionWheelStyle style = null)
        {
            ArgumentNullException.ThrowIfNull(entries);

            // OpenNew is now equivalent to requesting an open on the next UI tick.
            RequestOpen(ownerPlayer, ownerHeldItemType, entries, centerEntry: null, style);
        }

        public static void OpenNew(
            int ownerPlayer,
            int ownerHeldItemType,
            IEnumerable<ActionWheelEntry> entries,
            ActionWheelEntry centerEntry,
            ActionWheelStyle style = null)
        {
            ArgumentNullException.ThrowIfNull(entries);

            // OpenNew is now equivalent to requesting an open on the next UI tick.
            RequestOpen(ownerPlayer, ownerHeldItemType, entries, centerEntry, style);
        }

        private static void RequestOpen(
            int ownerPlayer,
            int ownerHeldItemType,
            IEnumerable<ActionWheelEntry> entries,
            ActionWheelEntry centerEntry,
            ActionWheelStyle style)
        {
            ArgumentNullException.ThrowIfNull(entries);

            // Ignore duplicate open requests from the current owner.
            if (IsActive && _ownerPlayer == ownerPlayer && _ownerHeldItemType == ownerHeldItemType)
            {
                return;
            }

            _pendingOpen = new PendingOpenRequest
            {
                OwnerPlayer = ownerPlayer,
                OwnerHeldItemType = ownerHeldItemType,
                Entries = [.. entries],
                CenterEntry = centerEntry,
                Style = style
            };
        }

        internal static void ConsumePendingOpenIfAny()
        {
            if (_pendingOpen == null)
            {
                return;
            }

            // If the wheel is already active for this owner, just drop the request.
            if (IsActive && _ownerPlayer == _pendingOpen.OwnerPlayer && _ownerHeldItemType == _pendingOpen.OwnerHeldItemType)
            {
                _pendingOpen = null;
                return;
            }

            // Only open if there isn't an active wheel, otherwise replace it.
            Vector2 position = GetDefaultOpenPosition();
            _active = new ActionWheel(_pendingOpen.Entries, _pendingOpen.CenterEntry, _pendingOpen.Style, position);
            _ownerPlayer = _pendingOpen.OwnerPlayer;
            _ownerHeldItemType = _pendingOpen.OwnerHeldItemType;

            _pendingOpen = null;
        }

        public static void CancelActive()
        {
            if (_active != null)
            {
                _active.Close();
                _active = null;
            }

            _pendingOpen = null;
            _ownerPlayer = -1;
            _ownerHeldItemType = 0;
        }

        public static bool IsOwnedBy(int ownerPlayer, int ownerHeldItemType)
        {
            return IsActive && _ownerPlayer == ownerPlayer && _ownerHeldItemType == ownerHeldItemType;
        }

        private ActionWheel(IEnumerable<ActionWheelEntry> entries, ActionWheelEntry centerEntry, ActionWheelStyle style, Vector2 position)
        {
            ArgumentNullException.ThrowIfNull(entries);
            _entries = [.. entries];
            if (_entries.Count == 0) throw new ArgumentException("Action wheel must have at least one entry.", nameof(entries));

            _centerEntry = centerEntry;
            _style = style ?? new ActionWheelStyle();
            Active = true;
            Position = position;
            HoveredIndex = -1;
            CenterHovered = false;
            OnWheelMenu = false;
            _openedAtUpdateCount = Main.GameUpdateCount;
            RebuildVisibleEntries();
        }

        public void Close()
        {
            Active = false;
            HoveredIndex = -1;
            CenterHovered = false;
            OnWheelMenu = false;
        }

        /// <summary>
        /// Updates hovered index and performs commit/cancel actions.
        /// Mirrors Terraria's `WiresRadial` behavior: left click selects, left click outside closes,
        /// and right click cancels/closes.
        /// </summary>
        public void Update(bool selectJustPressed, bool cancelJustPressed)
        {
            if (!Active) return;

            RebuildVisibleEntries();

            Vector2 cursor = GetEffectiveCursor();
            // Bare-minimum mouse wheel behavior: treat input as raw screen pixels.
            CenterHovered = ComputeCenterHovered(cursor, usingGamepad: false);
            HoveredIndex = CenterHovered ? -1 : ComputeHoveredIndex(cursor, usingGamepad: false);
            OnWheelMenu = CenterHovered || HoveredIndex != -1;

            // Depending on call order, the "open" right-click press can be observed on the next UI tick.
            // Give it a 1-tick grace period so opening the wheel doesn't instantly cancel it.
            if (cancelJustPressed && Main.GameUpdateCount > _openedAtUpdateCount + 1)
            {
                Close();
                return;
            }

            if (selectJustPressed && HoveredIndex != -1)
            {
                _visibleEntries[HoveredIndex].OnSelect();
                return;
            }

            if (selectJustPressed && CenterHovered && _centerEntry != null)
            {
                _centerEntry.OnSelect();
                return;
            }

            if (selectJustPressed && HoveredIndex == -1 && _style.CloseOnClickOutside)
            {
                Close();
            }
        }

        /// <summary>
        /// Draws the wheel using the provided SpriteBatch.
        /// Note: Interface layers already run inside an active SpriteBatch; do not Begin/End here.
        /// </summary>
        public void Draw(SpriteBatch spriteBatch)
        {
            if (!Active) return;
            ArgumentNullException.ThrowIfNull(spriteBatch);

            int count = _visibleEntries.Count;
            DynamicSpriteFont font = FontAssets.MouseText.Value;
            Texture2D backNormal = TextureAssets.WireUi[0].Value;
            Texture2D backHover = TextureAssets.WireUi[1].Value;
            float uiScale = Main.UIScale;

            if (_centerEntry != null)
            {
                bool hovered = CenterHovered;
                bool selected = _centerEntry.IsSelected != null && _centerEntry.IsSelected();
                Texture2D back = hovered ? backHover : backNormal;
                Color backColor = hovered ? _style.HoverBackground : _style.Background;
                float scale = _style.CenterSlotScale * uiScale;
                spriteBatch.Draw(back, Position, null, backColor, 0f, back.Size() * 0.5f, scale, SpriteEffects.None, 0f);

                string label = _centerEntry.ResolveLabel();
                Vector2 size = font.MeasureString(label) * uiScale;
                Vector2 textPosUi = Position - size * 0.5f;
                Color textColor = selected ? _style.SelectedTextColor : _style.TextColor;
                spriteBatch.DrawString(font, label, textPosUi, textColor, 0f, Vector2.Zero, uiScale, SpriteEffects.None, 0f);
            }

            if (count <= 0) return;
            for (int i = 0; i < count; i++)
            {
                bool hovered = i == HoveredIndex;
                var entry = _visibleEntries[i];
                bool selected = entry.IsSelected != null && entry.IsSelected();

                Vector2 centerUi = GetSlotCenter(i, count);
                Texture2D back = hovered ? backHover : backNormal;
                Color backColor = hovered ? _style.HoverBackground : _style.Background;
                spriteBatch.Draw(back, centerUi, null, backColor, 0f, back.Size() * 0.5f, _style.SlotScale * uiScale, SpriteEffects.None, 0f);

                string label = entry.ResolveLabel();
                Vector2 size = font.MeasureString(label) * uiScale;
                Vector2 textPosUi = centerUi - size * 0.5f;
                Color textColor = selected ? _style.SelectedTextColor : _style.TextColor;
                spriteBatch.DrawString(font, label, textPosUi, textColor, 0f, Vector2.Zero, uiScale, SpriteEffects.None, 0f);
            }
        }

        public int HitTestHoveredIndex(Vector2 cursorScreen, bool usingGamepad = false)
        {
            if (!Active)
            {
                return -1;
            }

            RebuildVisibleEntries();
            if (ComputeCenterHovered(cursorScreen, usingGamepad))
            {
                return -1;
            }
            return ComputeHoveredIndex(cursorScreen, usingGamepad);
        }

        private void RebuildVisibleEntries()
        {
            _visibleEntries.Clear();
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.IsVisible != null && !e.IsVisible())
                {
                    continue;
                }

                _visibleEntries.Add(e);
            }
        }

        private Vector2 GetEffectiveCursor()
        {
            return new Vector2(Main.mouseX, Main.mouseY);
        }

        private static Vector2 GetDefaultOpenPosition()
        {
            return new Vector2(Main.mouseX, Main.mouseY);
        }

        private bool ComputeCenterHovered(Vector2 cursor, bool usingGamepad)
        {
            if (_centerEntry == null)
            {
                return false;
            }

            float uiScale = Main.UIScale;
            float radius = _style.CenterHoverRadiusPx * uiScale;
            return Vector2.Distance(Position, cursor) < radius;
        }

        private int ComputeHoveredIndex(Vector2 cursor, bool usingGamepad)
        {
            int count = _visibleEntries.Count;
            if (count <= 0) return -1;

            float uiScale = Main.UIScale;
            bool anyHovered = false;
            int hoveredIndex = -1;

            if (!usingGamepad)
            {
                for (int i = 0; i < count; i++)
                {
                    Vector2 slot = GetSlotCenter(i, count);
                    bool hovered = Vector2.Distance(slot, cursor) < _style.MouseHoverRadiusPx * uiScale;
                    if (anyHovered)
                    {
                        hovered = false;
                    }

                    if (hovered)
                    {
                        anyHovered = true;
                        hoveredIndex = i;
                    }
                }

                return hoveredIndex;
            }

            Vector2 v = cursor - Position;
            float len = v.Length();
            if (len < _style.InnerDeadzoneRadiusPx * uiScale)
            {
                return -1;
            }

            // Vanilla WiresUI computes a cursor delta and derives direction/angle from it.
            // Normalize the delta so direction math is stable and easy to debug.
            Vector2 vNorm = v / len;
            float cursorAngle = vNorm.ToRotation();
            float slice = MathHelper.TwoPi / count;
            float halfSlice = slice * 0.5f;

            for (int i = 0; i < count; i++)
            {
                Vector2 dir = GetSlotCenter(i, count) - Position;
                float slotAngle = dir.ToRotation();
                float diff = Math.Abs(MathHelper.WrapAngle(cursorAngle - slotAngle));
                bool hovered = diff <= halfSlice;
                if (anyHovered)
                {
                    hovered = false;
                }

                if (hovered)
                {
                    anyHovered = true;
                    hoveredIndex = i;
                }
            }

            return hoveredIndex;
        }

        private Vector2 GetSlotCenter(int index, int count)
        {
            if ((uint)index >= (uint)count) throw new ArgumentOutOfRangeException(nameof(index));

            float uiScale = Main.UIScale;
            float angle = -MathHelper.PiOver2 + MathHelper.TwoPi * (index / (float)count);
            Vector2 offset = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * (_style.RadiusPx * uiScale);
            return Position + offset;
        }
    }

    public sealed class ActionWheelEntry
    {
        public string Id { get; }
        public string Label { get; }
        public Func<string> LabelGetter { get; }
        public Texture2D Icon { get; }
        public Action OnSelect { get; }
        public Func<bool> IsSelected { get; }
        public Func<bool> IsVisible { get; }

        public ActionWheelEntry(string id, string label, Action onSelect, Func<bool> isSelected = null, Texture2D icon = null, Func<bool> isVisible = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Label = label ?? throw new ArgumentNullException(nameof(label));
            OnSelect = onSelect ?? throw new ArgumentNullException(nameof(onSelect));
            IsSelected = isSelected;
            Icon = icon;
            IsVisible = isVisible;
        }

        public ActionWheelEntry(string id, Func<string> labelGetter, Action onSelect, Func<bool> isSelected = null, Texture2D icon = null, Func<bool> isVisible = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            LabelGetter = labelGetter ?? throw new ArgumentNullException(nameof(labelGetter));
            OnSelect = onSelect ?? throw new ArgumentNullException(nameof(onSelect));
            IsSelected = isSelected;
            Icon = icon;
            IsVisible = isVisible;
        }

        internal string ResolveLabel()
        {
            string label = LabelGetter != null ? LabelGetter() : Label;
            return label ?? Id ?? "?";
        }
    }

    public sealed class ActionWheelStyle
    {
        public float RadiusPx { get; set; } = 45f;
        public float InnerDeadzoneRadiusPx { get; set; } = 20f;
        public float MouseHoverRadiusPx { get; set; } = 19f;
        public float CenterHoverRadiusPx { get; set; } = 22f;
        public float GamepadCursorDistancePx { get; set; } = 40f;
        public float SlotScale { get; set; } = 1f;
        public float CenterSlotScale { get; set; } = 1f;
        public bool CloseOnClickOutside { get; set; } = true;

        public Color Background { get; set; } = Color.White;
        public Color HoverBackground { get; set; } = Color.White;
        public Color TextColor { get; set; } = Color.White;
        public Color SelectedTextColor { get; set; } = new Color(255, 230, 120, 255);
    }

    /// <summary>
    /// Runtime host for the active ActionWheel instance.
    /// Kept in the same file as ActionWheel by design.
    /// </summary>
    internal sealed class ActionWheelRuntime : ModSystem
    {
        private static bool _prevLeftDown;
        private static bool _prevRightDown;
        private static int _heldItemMismatchTicks;
        private const int HeldItemMismatchGraceTicks = 1;

        public override void UpdateUI(GameTime gameTime)
        {
            // Open requests originate from item logic (game update). Consume them here so the wheel
            // uses the UI-phase mouse coordinates (matching vanilla radial behavior).
            ActionWheel.ConsumePendingOpenIfAny();

            if (!ActionWheel.IsActive)
            {
                _prevLeftDown = Main.mouseLeft;
                _prevRightDown = Main.mouseRight;
                _heldItemMismatchTicks = 0;
                return;
            }

            if (Main.gameMenu)
            {
                ActionWheel.CancelActive();
                _prevLeftDown = Main.mouseLeft;
                _prevRightDown = Main.mouseRight;
                _heldItemMismatchTicks = 0;
                return;
            }

            Player player = Main.LocalPlayer;
            if (ActionWheel.IsActive && (ActionWheelOwnerInvalid(player) || player.dead))
            {
                ActionWheel.CancelActive();
                _prevLeftDown = Main.mouseLeft;
                _prevRightDown = Main.mouseRight;
                _heldItemMismatchTicks = 0;
                return;
            }

            // While the wheel is open, consume mouse input so items/UI don't react.
            // Match vanilla radial behavior: only claim mouseInterface when actually hovering a wheel option.
            // Don't call UIView.OverWriteGameMouseInput() here:
            // it zeroes out Main.mouseLeft/Main.mouseRight which prevents the wheel from
            // receiving select/cancel clicks, and it can leave UIView.GameMouseOverwritten
            // stuck true which blocks tools from opening new wheels.

            // Don't rely on Main.mouseLeftRelease/Main.mouseRightRelease here; those flags can be
            // modified by other UI. Use edge detection instead.
            bool leftDown = Main.mouseLeft;
            bool rightDown = Main.mouseRight;
            bool select = leftDown && !_prevLeftDown;
            bool cancel = rightDown && !_prevRightDown;
            _prevLeftDown = leftDown;
            _prevRightDown = rightDown;
            ActionWheel.ActiveInstance.Update(selectJustPressed: select, cancelJustPressed: cancel);
            if (ActionWheel.IsActive && ActionWheel.ActiveInstance.OnWheelMenu)
            {
                player.mouseInterface = true;
            }

            if (!ActionWheel.IsActive)
            {
                ActionWheel.CancelActive();
                _heldItemMismatchTicks = 0;
            }
        }

        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            int mouseTextIndex = layers.FindIndex(l => l.Name.Equals("Vanilla: Mouse Text"));
            if (mouseTextIndex == -1)
            {
                mouseTextIndex = layers.Count;
            }

            layers.Insert(mouseTextIndex, new LegacyGameInterfaceLayer(
                "ArchitectsStencilMod: ActionWheel",
                DrawInterface,
                InterfaceScaleType.UI
            ));
        }

        private static bool DrawInterface()
        {
            if (!ActionWheel.IsActive)
            {
                return true;
            }

            ActionWheel.ActiveInstance.Draw(Main.spriteBatch);
            return true;
        }

        private static bool ActionWheelOwnerInvalid(Player player)
        {
            if (Main.myPlayer < 0)
            {
                return true;
            }

            if (ActionWheel.OwnerPlayer != Main.myPlayer)
            {
                return true;
            }

            // Close the wheel if the owning item is no longer held.
            // `player.HeldItem.type` can transiently desync while UI is open / mouse input is being claimed,
            // so allow a short grace period before cancelling.
            int heldType = player?.HeldItem?.type ?? 0;
            if (heldType == ActionWheel.OwnerHeldItemType)
            {
                _heldItemMismatchTicks = 0;
                return false;
            }

            _heldItemMismatchTicks++;
            return _heldItemMismatchTicks > HeldItemMismatchGraceTicks;
        }

    }
}

