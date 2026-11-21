#if DEBUG
#nullable enable

using System.Collections;
using Terraria;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core.Layers;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Core.TestCases.LayerTests
{
    /// <summary>
    /// Pins VanillaMap.itr and asserts Plants style-8 zero-count visual plus SmallPiles Echo decode.
    /// </summary>
    internal sealed class layer_R02 : ITestCase
    {
        private const string MapFileName = "VanillaMap.itr";

        public string Id => "layer_R02";
        public string Description =>
            "VanillaMap.itr: Plants style 8 zero-count resolves Mushroom; SmallPiles Echo maps style 3";

        public TestCaseType Type => TestCaseType.Regression;

        public IEnumerator Run(Player player, ITestCaseReporter reporter)
        {
            // --- Guard loadability, pin VanillaMap, assert Plants channel + SmallPiles Echo.

            if (!TestCaseGuards.RequireInteractionMapLoadable(Id, reporter))
                yield break;

            if (!MapFixturePaths.TryResolve(MapFileName, out string mapPath, out string? pathError))
            {
                reporter.Fail(Id, pathError ?? $"Missing map fixture {MapFileName}.");
                yield break;
            }

            IEnumerator body = RunWithPinnedMap(reporter, mapPath);
            IEnumerator pinned = InteractionMapPathFixture.RunWith(mapPath, body);
            while (pinned.MoveNext())
                yield return pinned.Current;
        }

        private IEnumerator RunWithPinnedMap(ITestCaseReporter reporter, string mapPath)
        {
            // --- Assert map-backed Plants visual and classifier Echo for SmallPiles.

            if (!LayerIdentifyFixture.TryObserveZeroCountPlantChannel(
                    TileID.Plants,
                    style: 8,
                    expectedVisualItemId: ItemID.Mushroom,
                    out string? channelFail))
            {
                reporter.Fail(Id, channelFail ?? "VanillaMap Plants style-8 channel assert failed.");
                yield break;
            }

            if (!FrameOperations.TryResolveAmbientRubbleEcho(
                    TileID.SmallPiles,
                    frameX: 3 * 18,
                    frameY: 0,
                    out FrameOperations.AmbientRubbleEcho echo)
                || echo.EchoType != TileID.SmallPiles1x1Echo
                || echo.Style != 3)
            {
                reporter.Fail(
                    Id,
                    $"Small pebble Echo resolve expected SmallPiles1x1Echo style 3, "
                    + $"got type={echo.EchoType} style={echo.Style}.");
                yield break;
            }

            reporter.Pass(
                Id,
                $"Pinned {mapPath}: Plants style 8 → Mushroom; SmallPiles style 3 → SmallPiles1x1Echo.");
            yield break;
        }
    }
}
#endif
