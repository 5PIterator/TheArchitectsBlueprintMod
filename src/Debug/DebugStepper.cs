#nullable enable

using System;
using System.Collections;
using TheArchitectsBlueprintMod.Content.Items;
using Terraria;
using Terraria.GameInput;

namespace TheArchitectsBlueprintMod.Debug
{
    /// <summary>
    /// Manages step-through execution of long-running processes (e.g. discovery, stepped paste).
    /// Steps are advanced via <see cref="TryAdvanceSession"/> (debug stick primary use); right-click cancels.
    /// </summary>
    public static class DebugStepper
    {
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
        /// Starts a stepper session. Advance with <see cref="TryAdvanceSession"/> while holding the debug stick.
        /// Right-click cancels. Returns immediately; execution continues over frames.
        /// </summary>
        /// <param name="player">Player who must hold the debug stick to step.</param>
        /// <param name="enumerator">Process that yields at each step point.</param>
        /// <param name="onComplete">Called when the process completes normally.</param>
        /// <param name="onCancel">Called when the process is cancelled (right-click).</param>
        /// <param name="log">Optional message callback (e.g. chat reply).</param>
        public static void StartSession(
            Player player,
            IEnumerator enumerator,
            Action? onComplete = null,
            Action? onCancel = null,
            Action<string>? log = null)
        {
            if (_session is not null)
            {
                log?.Invoke("Debug stepper: previous session cancelled.");
                _session.Cancel();
            }

            _session = new StepperSession(player, enumerator, onComplete, onCancel, log);
            log?.Invoke("Hold Debug Stick: left-click to step, right-click to cancel.");
        }

        /// <summary>
        /// Called each frame by DebugStepperSystem for cancel / validity checks (stepping is item-driven).
        /// </summary>
        internal static void Update()
        {
            if (_session is null) return;

            _session.Update();
            if (_session.IsFinished)
            {
                _session = null;
            }
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
        /// Advances the active session by one step (debug stick primary use). Returns false when there is no session.
        /// </summary>
        public static bool TryAdvanceSession(Player player)
        {
            if (_session is null || player.whoAmI != Main.myPlayer)
                return false;
            return _session.TryAdvanceOne(player);
        }

        private sealed class StepperSession
        {
            private readonly Player _player;
            private readonly IEnumerator _enumerator;
            private readonly Action? _onComplete;
            private readonly Action? _onCancel;
            private readonly Action<string>? _log;
            private bool _finished;

            public bool IsFinished => _finished;

            public StepperSession(
                Player player,
                IEnumerator enumerator,
                Action? onComplete,
                Action? onCancel,
                Action<string>? log)
            {
                _player = player;
                _enumerator = enumerator;
                _onComplete = onComplete;
                _onCancel = onCancel;
                _log = log;
            }

            public void Cancel()
            {
                _finished = true;
                _onCancel?.Invoke();
                _log?.Invoke("Debug stepper: cancelled.");
            }

            public void Update()
            {
                if (_finished) return;

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
            }

            /// <summary>Runs one <see cref="IEnumerator.MoveNext"/> when the player still holds the debug stick.</summary>
            public bool TryAdvanceOne(Player player)
            {
                if (_finished)
                    return false;
                if (_player != player || player.whoAmI != Main.myPlayer)
                    return false;
                if (!IsHoldingDebugStick(player))
                    return false;

                bool hasMore = _enumerator.MoveNext();
                if (!hasMore)
                {
                    _finished = true;
                    _onComplete?.Invoke();
                    _log?.Invoke("Debug stepper: complete.");
                }

                return true;
            }
        }
    }
}
