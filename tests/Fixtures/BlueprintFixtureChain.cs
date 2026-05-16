#if DEBUG
#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Tests.Fixtures
{
    /// <summary>
    /// Applies a <see cref="TestBlueprintChain"/> configuration to live game systems and tears down on dispose.
    /// Scenarios should only run checks; this type owns registration and cleanup.
    /// </summary>
    internal sealed class BlueprintFixtureChain : IDisposable
    {
        private readonly Player _player;
        private readonly Rectangle _bounds;
        private SelectionFixtureScope? _selectionScope;
        private bool _projectionRegistered;
        private WorldTileSnapshotScope? _worldSnapshots;

        public Player Player => _player;
        public Rectangle WorldTileBounds => _bounds;
        public Selection? Selection => _selectionScope?.Selection;
        public Projection? Projection { get; private set; }

        private BlueprintFixtureChain(Player player, Rectangle bounds)
        {
            _player = player;
            _bounds = bounds;
        }

        internal static bool TryEnterSelection(
            TestBlueprintChain config,
            out BlueprintFixtureChain? chain,
            out string? error)
        {
            chain = null;
            if (!ValidateConfig(config, out error))
                return false;

            var candidate = new BlueprintFixtureChain(config.Player, config.WorldTileBounds);
            if (!SelectionFixtureScope.TryEnterWithBounds(config.Player, config.WorldTileBounds, out candidate._selectionScope, out error))
                return false;

            chain = candidate;
            return true;
        }

        internal static bool TryEnterProjection(
            TestBlueprintChain config,
            out BlueprintFixtureChain? chain,
            out string? error) =>
            TryEnterProjection(config, cutSource: false, out chain, out error);

        internal static bool TryEnterProjection(
            TestBlueprintChain config,
            bool cutSource,
            out BlueprintFixtureChain? chain,
            out string? error) =>
            TryEnterProjection(config, cutSource, beforeCapture: null, out chain, out error);

        internal static bool TryEnterProjection(
            TestBlueprintChain config,
            bool cutSource,
            Action<BlueprintFixtureChain>? beforeCapture,
            out BlueprintFixtureChain? chain,
            out string? error)
        {
            if (!TryEnterSelection(config, out chain, out error))
                return false;

            beforeCapture?.Invoke(chain!);

            if (!chain!.EnterProjectionFromSelection(out error))
            {
                chain.Dispose();
                chain = null;
                return false;
            }

            chain.Projection!.SetCut(cutSource);
            if (!PayloadScenarioChecks.CutSourceEquals(chain.Projection, cutSource && chain.Projection.HasPayload, out error))
            {
                chain.Dispose();
                chain = null;
                return false;
            }

            return true;
        }

        internal static bool TryEnterImportedProjection(
            TestBlueprintChain config,
            Payload payload,
            bool cutSource,
            out BlueprintFixtureChain? chain,
            out string? error)
        {
            chain = null;
            ArgumentNullException.ThrowIfNull(payload);
            if (!payload.IsDefined)
            {
                error = "Payload is not defined.";
                return false;
            }

            if (!ValidateConfig(config, out error))
                return false;

            var candidate = new BlueprintFixtureChain(config.Player, config.WorldTileBounds);
            if (!SelectionFixtureScope.TryEnterWithBounds(config.Player, config.WorldTileBounds, out candidate._selectionScope, out error))
                return false;

            if (!candidate.EnterImportedProjection(payload, cutSource, out error))
            {
                candidate.Dispose();
                return false;
            }

            chain = candidate;
            return true;
        }

        private bool EnterProjectionFromSelection(out string? error)
        {
            error = null;
            Selection selection = Selection!;
            Projection = ModContent.GetInstance<ProjectionSystem>().NewProjection(_player, selection);
            if (Projection is null)
            {
                error = "ProjectionSystem.NewProjection returned null.";
                return false;
            }

            _projectionRegistered = true;
            if (!BlueprintTestAssertions.VerifyProjection(_player, Projection, expectPayload: true, out error))
                return false;

            return true;
        }

        private bool EnterImportedProjection(Payload payload, bool cutSource, out string? error)
        {
            error = null;
            if (!ProjectionFixture.TryCreateWithImportedPayload(Selection!, payload, cutSource, out Projection projection, out error))
                return false;

            ModContent.GetInstance<BlueprintSystem>().RegisterBlueprint(projection);
            projection.UpdateOverlay();
            Projection = projection;
            _projectionRegistered = true;
            if (!BlueprintTestAssertions.VerifyProjection(_player, Projection, expectPayload: true, out error))
                return false;

            Projection.SetCut(cutSource);
            return PayloadScenarioChecks.CutSourceEquals(Projection, cutSource, out error);
        }

        /// <summary>Snapshots world tiles in <paramref name="tileArea"/> before paste or setup mutates them.</summary>
        internal void ProtectWorldRegion(Rectangle tileArea)
        {
            if (tileArea.Width <= 0 || tileArea.Height <= 0)
                return;

            _worldSnapshots ??= new WorldTileSnapshotScope();
            _worldSnapshots.Capture(tileArea);
        }

        /// <summary>Protects paste target and, when enabled, the source cut area.</summary>
        internal void ProtectPasteAreas(Projection projection)
        {
            ProtectWorldRegion(projection.TargetArea);
            if (projection.CutSource && !projection.SourceArea.IsEmpty)
                ProtectWorldRegion(projection.SourceArea);
        }

        internal bool VerifyTeardown(out string? error)
        {
            if (!BlueprintTestAssertions.VerifyNoActivePaste(_player, out error))
                return false;

            if (!BlueprintTestAssertions.VerifyNoProjection(_player, out error))
                return false;

            if (_selectionScope is not null)
                return _selectionScope.VerifyAfterTeardown(out error);

            return BlueprintTestAssertions.VerifyNoSelection(_player, out error);
        }

        public void Dispose()
        {
            if (_projectionRegistered && Projection is not null)
            {
                ModContent.GetInstance<PastingSystem>().StopPaste(Projection);
                ModContent.GetInstance<ProjectionSystem>().RemoveProjection(Projection);
            }

            Projection = null;
            _projectionRegistered = false;
            _worldSnapshots?.Dispose();
            _worldSnapshots = null;
            _selectionScope?.Dispose();
            _selectionScope = null;
        }

        private static bool ValidateConfig(TestBlueprintChain config, out string? error)
        {
            error = null;
            if (!config.HasBounds)
            {
                error = "Chain has no tile bounds; use WithTileBounds* on the config.";
                return false;
            }

            return config.TryValidateInWorld(out error);
        }
    }
}
#endif
