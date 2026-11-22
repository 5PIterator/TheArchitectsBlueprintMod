#if DEBUG
#nullable enable

using System;
using System.Collections;
using Terraria;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.Server;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core.TestCases
{
    /// <summary>Shared paste orchestration helpers for Test Cases.</summary>
    internal static class PasteHelpers
    {
        /// <summary>Hard cap so missing-material place retries cannot spin for minutes.</summary>
        private const int MaxPasteSteps = 10_000;

        /// <summary>Builds an empty defined payload used to clear a paste area.</summary>
        internal static Payload CreateEmptyPayload(int width, int height) =>
            Payload.FromSparse([], width, height);

        /// <summary>
        /// Starts a paste and yields one frame at a time until it finishes or times out.
        /// Local authority waits on active operations; remote clients wait on projection unlock / paste ACKs.
        /// </summary>
        internal static IEnumerator RunPasteAndAwait(
            Projection projection,
            ITestCaseReporter reporter,
            string caseTag,
            Action<bool, string?> onComplete,
            int maxSteps = MaxPasteSteps)
        {
            // --- StartPaste, then wait on local ops or remote ACK unlock with a shared frame budget.

            if (maxSteps < 1)
                maxSteps = MaxPasteSteps;

            PastingSystem pastingSystem = ModContent.GetInstance<PastingSystem>();
            bool remoteRequest = MpCoordinator.UsesRemotePasteRequestPath();
            if (remoteRequest)
                PasteNetworking.ClearClientAckObservations();

            if (!pastingSystem.StartPaste(projection, out PasteRejectReason rejectReason))
            {
                onComplete(false, $"Paste rejected: {rejectReason}.");
                yield break;
            }

            if (remoteRequest)
            {
                IEnumerator remote = AwaitRemotePasteLifecycle(projection, reporter, caseTag, onComplete, maxSteps);
                while (remote.MoveNext())
                    yield return remote.Current;
                yield break;
            }

            int steps = 0;
            while (pastingSystem.HasActiveOperationFor(projection))
            {
                if (steps++ >= maxSteps)
                {
                    pastingSystem.StopPaste(projection);
                    onComplete(
                        false,
                        $"Paste did not complete within {maxSteps} frames (often missing materials / stuck place retries).");
                    yield break;
                }

                // ProcessOperations skips ops while the debug stick is held; step those manually once per frame.
                Player local = Main.LocalPlayer;
                if (local is not null && DebugStepper.IsHoldingDebugStick(local))
                {
                    bool keep = pastingSystem.DebugStepOperation(projection);
                    if (!keep && pastingSystem.HasActiveOperationFor(projection))
                    {
                        pastingSystem.StopPaste(projection);
                        onComplete(false, "Paste stalled while operation was still active.");
                        yield break;
                    }
                }

                yield return null;
            }

            reporter.Info($"{caseTag}: paste completed in {steps} frame(s).");
            onComplete(true, null);
        }

        /// <summary>
        /// Starts a paste expected to be rejected either immediately (local host) or via Rejected ACK (remote).
        /// </summary>
        internal static IEnumerator RunPasteExpectReject(
            Projection projection,
            PasteRejectReason expectedReason,
            ITestCaseReporter reporter,
            string caseTag,
            Action<bool, string?> onComplete,
            int maxSteps = 600)
        {
            // --- Snapshot StartPaste false reasons on host; wait for Rejected ACK on remote request path.

            PastingSystem pastingSystem = ModContent.GetInstance<PastingSystem>();
            bool remoteRequest = MpCoordinator.UsesRemotePasteRequestPath();
            if (remoteRequest)
                PasteNetworking.ClearClientAckObservations();

            bool started = pastingSystem.StartPaste(projection, out PasteRejectReason rejectReason);
            if (!remoteRequest)
            {
                if (started)
                {
                    pastingSystem.StopPaste(projection);
                    onComplete(false, "Expected local paste reject, but StartPaste succeeded.");
                    yield break;
                }

                if (rejectReason != expectedReason)
                {
                    onComplete(false, $"Expected reject {expectedReason}, got {rejectReason}.");
                    yield break;
                }

                reporter.Info($"{caseTag}: local StartPaste rejected with {rejectReason}.");
                onComplete(true, null);
                yield break;
            }

            if (!started)
            {
                if (rejectReason != expectedReason)
                {
                    onComplete(false, $"Expected reject {expectedReason}, got immediate {rejectReason}.");
                    yield break;
                }

                reporter.Info($"{caseTag}: remote StartPaste rejected immediately with {rejectReason}.");
                onComplete(true, null);
                yield break;
            }

            int requestId = projection.Id;
            for (int steps = 0; steps < maxSteps; steps++)
            {
                if (PasteNetworking.TryGetLatestAck(requestId, PasteAckKind.Rejected, out var rejected)
                    && rejected.Reason == expectedReason)
                {
                    reporter.Info($"{caseTag}: remote Rejected ACK reason={rejected.Reason} in {steps} frame(s).");
                    onComplete(true, null);
                    yield break;
                }

                if (PasteNetworking.TryGetLatestAck(requestId, out var latest)
                    && latest.Kind != PasteAckKind.Accepted)
                {
                    onComplete(
                        false,
                        $"Expected Rejected/{expectedReason}, got {latest.Kind}/{latest.Reason}.");
                    yield break;
                }

                yield return null;
            }

            onComplete(false, $"Timed out waiting for Rejected/{expectedReason} ACK.");
        }

        /// <summary>Pastes an empty payload over the projection target to restore air after a Case.</summary>
        internal static IEnumerator TeardownPasteArea(
            Projection projection,
            int width,
            int height,
            ITestCaseReporter reporter,
            string caseTag)
        {
            // --- Replace the payload with empty cells, then run the same frame-yielded paste path.

            if (width <= 0 || height <= 0)
                yield break;

            Payload emptyPayload = CreateEmptyPayload(width, height);
            projection.SetPayload(emptyPayload);

            bool ok = false;
            string? error = null;
            IEnumerator clear = RunPasteAndAwait(
                projection,
                reporter,
                caseTag,
                (success, err) =>
                {
                    ok = success;
                    error = err;
                });

            while (clear.MoveNext())
                yield return clear.Current;

            if (ok)
            {
                reporter.Info($"{caseTag}: teardown clear completed.");
                yield break;
            }

            reporter.Error(caseTag, $"Teardown clear failed: {error}");
        }

        private static IEnumerator AwaitRemotePasteLifecycle(
            Projection projection,
            ITestCaseReporter reporter,
            string caseTag,
            Action<bool, string?> onComplete,
            int maxSteps)
        {
            // --- Remote paste has no local op; wait for unlock plus a Finished/Rejected/Cancelled ACK.

            int requestId = projection.Id;
            if (requestId <= 0)
            {
                onComplete(false, "Remote paste did not assign a correlation id.");
                yield break;
            }

            bool sawAccepted = false;
            for (int steps = 0; steps < maxSteps; steps++)
            {
                if (PasteNetworking.TryGetLatestAck(requestId, PasteAckKind.Accepted, out _))
                    sawAccepted = true;

                if (PasteNetworking.TryGetLatestAck(requestId, PasteAckKind.Finished, out _))
                {
                    reporter.Info(
                        $"{caseTag}: remote paste Finished after {steps} frame(s) (accepted={sawAccepted}).");
                    onComplete(true, null);
                    yield break;
                }

                if (PasteNetworking.TryGetLatestAck(requestId, PasteAckKind.Cancelled, out _))
                {
                    reporter.Info($"{caseTag}: remote paste Cancelled after {steps} frame(s).");
                    onComplete(true, null);
                    yield break;
                }

                if (PasteNetworking.TryGetLatestAck(requestId, PasteAckKind.Rejected, out var rejected))
                {
                    onComplete(false, $"Paste rejected: {rejected.Reason}.");
                    yield break;
                }

                // Unlock without a recorded ACK usually means ACK-timeout path.
                if (!projection.IsLocked && projection.Id == 0 && steps > 0)
                {
                    if (PasteNetworking.TryGetLatestAck(requestId, out var latest))
                    {
                        reporter.Info($"{caseTag}: remote paste ended with {latest.Kind} after unlock.");
                        onComplete(latest.Kind is PasteAckKind.Finished or PasteAckKind.Cancelled, null);
                        yield break;
                    }

                    onComplete(false, "Projection unlocked without a paste ACK (possible ACK timeout).");
                    yield break;
                }

                yield return null;
            }

            ModContent.GetInstance<PastingSystem>().StopPaste(projection);
            onComplete(false, $"Remote paste did not complete within {maxSteps} frames.");
        }
    }
}
#endif
