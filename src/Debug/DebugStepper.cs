#nullable enable

using System;
using System.Collections;
using TheArchitectsBlueprintMod.Content.Items;
using Terraria;
using Terraria.GameInput;

namespace TheArchitectsBlueprintMod.Debug
{
    /// <summary>
    /// Manages step-through or budgeted auto execution of long-running processes (e.g. discovery, stepped paste).
    /// Manual sessions advance via <see cref="TryAdvanceSession"/> (debug stick primary use); auto sessions
    /// pump up to <see cref="AutoStepsPerFrame"/> enumerator steps each frame. Right-click cancels.
    /// </summary>
    public static class DebugStepper
    {
        /// <summary>Max enumerator advances per frame for budgeted auto sessions.</summary>
        public const int AutoStepsPerFrame = 500;

        private static StepperSession? _session;

        public static bool DebugStickEquipped { get; internal set; }

        /// <summary>
        /// Whether the player is holding the debug stick (by type).
        /// </summary>
        public static bool IsHoldingDebugStick(Player player)
        {
            if (player?.HeldItem?.ModItem is not DebugStick)
                return false;
            return true;
        }

        /// <summary>
        /// Starts a manual stepper session. Advance with <see cref="TryAdvanceSession"/> while holding the debug stick.
        /// Right-click cancels. Returns immediately; execution continues over frames.
        /// </summary>
        public static void StartSession(
            Player player,
            IEnumerator enumerator,
            Action? onComplete = null,
            Action? onCancel = null,
            Action<string>? log = null)
        {
            // --- Replace any prior session, then wait for stick-driven single steps.

            BeginSession(new StepperSession(
                player,
                enumerator,
                onComplete,
                onCancel,
                log,
                stepsPerFrame: 0,
                requireStickToAdvance: true));
            log?.Invoke("Hold Debug Stick: left-click to step, right-click to cancel.");
        }

        /// <summary>
        /// Starts a budgeted auto session that advances up to <paramref name="stepsPerFrame"/> enumerator
        /// steps each frame until complete. Right-click cancels.
        /// </summary>
        public static void StartAutoSession(
            IEnumerator enumerator,
            Action? onComplete = null,
            Action? onCancel = null,
            Action<string>? log = null,
            int stepsPerFrame = AutoStepsPerFrame)
        {
            // --- Pump a fixed budget each Update so discovery does not freeze the game.

            if (stepsPerFrame < 1)
                stepsPerFrame = 1;

            BeginSession(new StepperSession(
                player: null,
                enumerator,
                onComplete,
                onCancel,
                log,
                stepsPerFrame,
                requireStickToAdvance: false));
            log?.Invoke($"Debug stepper: auto-running ({stepsPerFrame} steps/frame). Right-click to cancel.");
        }

        /// <summary>
        /// Manual stick session when holding Debug Stick; otherwise budgeted auto session at
        /// <see cref="AutoStepsPerFrame"/> steps per frame.
        /// </summary>
        public static void RunEnumerator(
            IEnumerator enumerator,
            Player? player,
            bool allowStepping,
            Action? onCancel = null,
            Action<string>? log = null)
        {
            // --- Prefer click-to-step when debugging with the stick; otherwise spread work across frames.

            if (allowStepping && player is not null && IsHoldingDebugStick(player))
            {
                StartSession(player, enumerator, onComplete: null, onCancel: onCancel, log: log);
                return;
            }

            StartAutoSession(enumerator, onComplete: null, onCancel: onCancel, log: log);
        }

        /// <summary>
        /// Called each frame by DebugStepperSystem for auto pumping / cancel / validity checks.
        /// </summary>
        internal static void Update()
        {
            if (_session is null)
                return;

            _session.Update();
            if (_session.IsFinished)
                _session = null;
        }

        /// <summary>
        /// Cancels any active session. Safe to call from anywhere.
        /// </summary>
        public static void CancelSession()
        {
            _session?.Cancel();
            _session = null;
        }

        /// <summary>
        /// Advances the active manual session by one step (debug stick primary use).
        /// Returns false when there is no session.
        /// </summary>
        public static bool TryAdvanceSession(Player player)
        {
            if (_session is null || player.whoAmI != Main.myPlayer)
                return false;
            return _session.TryAdvanceOne(player);
        }

        /// <summary>Cancels any prior session, then installs <paramref name="session"/>.</summary>
        private static void BeginSession(StepperSession session)
        {
            // --- Drop in-flight work so only one enumerator owns the stepper.

            if (_session is not null)
            {
                session.Log?.Invoke("Debug stepper: previous session cancelled.");
                _session.Cancel();
            }

            _session = session;
        }

        private sealed class StepperSession
        {
            private readonly Player? _player;
            private readonly IEnumerator _enumerator;
            private readonly Action? _onComplete;
            private readonly Action? _onCancel;
            private readonly int _stepsPerFrame;
            private readonly bool _requireStickToAdvance;
            private bool _finished;

            public Action<string>? Log { get; }

            public bool IsFinished => _finished;

            public StepperSession(
                Player? player,
                IEnumerator enumerator,
                Action? onComplete,
                Action? onCancel,
                Action<string>? log,
                int stepsPerFrame,
                bool requireStickToAdvance)
            {
                _player = player;
                _enumerator = enumerator;
                _onComplete = onComplete;
                _onCancel = onCancel;
                Log = log;
                _stepsPerFrame = stepsPerFrame;
                _requireStickToAdvance = requireStickToAdvance;
            }

            /// <summary>Marks finished and invokes the cancel callback once.</summary>
            public void Cancel()
            {
                // --- Stop pumping and let callers restore chambers / UI state.

                if (_finished)
                    return;

                _finished = true;
                _onCancel?.Invoke();
                Log?.Invoke("Debug stepper: cancelled.");
            }

            /// <summary>Handles cancel input and auto-pumps budgeted sessions.</summary>
            public void Update()
            {
                // --- Bail on invalid players, honor right-click cancel, then spend the frame budget.

                if (_finished)
                    return;

                if (_requireStickToAdvance)
                {
                    if (_player?.active != true || _player.whoAmI != Main.myPlayer)
                    {
                        Cancel();
                        return;
                    }

                    if (!IsHoldingDebugStick(_player))
                        return;

                    if (PlayerInput.Triggers.JustPressed.MouseRight)
                    {
                        Cancel();
                        return;
                    }

                    return;
                }

                // Auto session: right-click cancels without requiring the debug stick.
                if (PlayerInput.Triggers.JustPressed.MouseRight)
                {
                    Cancel();
                    return;
                }

                // Advance up to the per-frame budget.
                for (int i = 0; i < _stepsPerFrame; i++)
                {
                    if (!AdvanceEnumerator())
                        break;
                }
            }

            /// <summary>Runs one <see cref="IEnumerator.MoveNext"/> when the player still holds the debug stick.</summary>
            public bool TryAdvanceOne(Player player)
            {
                // --- Manual sessions only; auto sessions advance from Update.

                if (_finished || !_requireStickToAdvance)
                    return false;
                if (_player != player || player.whoAmI != Main.myPlayer)
                    return false;
                if (!IsHoldingDebugStick(player))
                    return false;

                AdvanceEnumerator();
                return true;
            }

            /// <summary>Advances the enumerator once; completes the session when it ends.</summary>
            private bool AdvanceEnumerator()
            {
                // --- MoveNext until exhaustion, then fire onComplete.

                if (_finished)
                    return false;

                bool hasMore = _enumerator.MoveNext();
                if (!hasMore)
                {
                    _finished = true;
                    _onComplete?.Invoke();
                    Log?.Invoke("Debug stepper: complete.");
                    return false;
                }

                return true;
            }
        }
    }
}
