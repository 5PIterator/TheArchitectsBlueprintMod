#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.Layers;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core.TestCases.LayerTests
{
    /// <summary>
    /// Asserts plants, candles, and small pebbles route to Plant/Frame (not Block),
    /// and that zero-count .itr channels resolve mushrooms while unmapped tiles fail.
    /// </summary>
    internal sealed class layer_R01 : ITestCase
    {
        public string Id => "layer_R01";
        public string Description =>
            "Normal mushrooms, water candles, and small pebbles classify as Plant/Frame; stone stays Block; .itr zero-count resolves mushrooms";

        public TestCaseType Type => TestCaseType.Regression;

        public IEnumerator Run(Player player, ITestCaseReporter reporter)
        {
            // --- Join LayerColumnsPlaced, observe Plant/Frame vs Block, check .itr channels, teardown.

            TestChamberSystem chamber = TestChamberSystem.Instance;
            Point mushroomSupport = chamber.LayerSupport(0);
            Point candleSupport = chamber.LayerSupport(1);
            Point pebbleSupport = chamber.LayerSupport(2);
            Point stonePos = chamber.LayerSupport(3);

            Point mushroomSubject = default;
            Point candleSubject = default;
            Point pebbleSubject = default;

            try
            {
                // Normal mushroom is Plants style 8 (frameX = 8 * 18).
                if (!LayerColumnFixture.TryJoinSupportSubject(
                        mushroomSupport,
                        TileID.Grass,
                        TileID.Plants,
                        out mushroomSubject,
                        out string? mushroomError,
                        subjectFrameX: 8 * 18,
                        subjectFrameY: 0))
                {
                    reporter.Fail(Id, mushroomError ?? "Normal mushroom setup failed.");
                    yield break;
                }

                if (!LayerColumnFixture.TryJoinSupportSubject(
                        candleSupport,
                        TileID.Stone,
                        TileID.WaterCandle,
                        out candleSubject,
                        out string? candleError))
                {
                    reporter.Fail(Id, candleError ?? "Water candle setup failed.");
                    yield break;
                }

                // Non-zero 1x1 pebble style on stone (frameX = 3 * 18, frameY = 0).
                if (!LayerColumnFixture.TryJoinSupportSubject(
                        pebbleSupport,
                        TileID.Stone,
                        TileID.SmallPiles,
                        out pebbleSubject,
                        out string? pebbleError,
                        subjectFrameX: 3 * 18,
                        subjectFrameY: 0))
                {
                    reporter.Fail(Id, pebbleError ?? "Small pebble setup failed.");
                    yield break;
                }

                if (!LayerColumnFixture.TryJoinStoneControl(stonePos, out string? stoneSetupError))
                {
                    reporter.Fail(Id, stoneSetupError ?? "Stone control setup failed.");
                    yield break;
                }

                if (!LayerIdentifyFixture.TryObserveAnchoredOrPlantOwnership(
                        mushroomSubject,
                        "normal mushroom",
                        out string? mushroomFail))
                {
                    reporter.Fail(Id, mushroomFail ?? "Normal mushroom ownership assert failed.");
                    yield break;
                }

                if (!LayerIdentifyFixture.TryObserveAnchoredOrPlantOwnership(
                        candleSubject,
                        "water candle",
                        out string? candleFail))
                {
                    reporter.Fail(Id, candleFail ?? "Water candle ownership assert failed.");
                    yield break;
                }

                if (!LayerIdentifyFixture.TryObserveFrameOwnership(
                        pebbleSubject,
                        "small pebble",
                        out string? pebbleFail))
                {
                    reporter.Fail(Id, pebbleFail ?? "Small pebble ownership assert failed.");
                    yield break;
                }

                if (!LayerIdentifyFixture.TryObserveBlockOwnership(
                        stonePos,
                        "stone control",
                        out string? stoneFail))
                {
                    reporter.Fail(Id, stoneFail ?? "Stone control ownership assert failed.");
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

                if (!LayerIdentifyFixture.TryObserveZeroCountPlantChannel(
                        TileID.Plants,
                        style: 8,
                        expectedVisualItemId: ItemID.Mushroom,
                        out string? channelFail))
                {
                    reporter.Fail(Id, channelFail ?? "Zero-count mushroom channel assert failed.");
                    yield break;
                }

                if (!LayerIdentifyFixture.TryObserveUnresolvedWhenUnmapped(
                        out string? unresolvedFail))
                {
                    reporter.Fail(Id, unresolvedFail ?? "UnresolvedInteraction assert failed.");
                    yield break;
                }

                reporter.Pass(
                    Id,
                    "Normal mushroom/Plant, water candle/Frame, small pebble/Frame+Echo; stone Block; "
                    + ".itr zero-count resolves Mushroom and rejects unmapped.");
            }
            finally
            {
                LayerColumnFixture.ClearTile(mushroomSupport);
                LayerColumnFixture.ClearTile(mushroomSubject);
                LayerColumnFixture.ClearTile(candleSupport);
                LayerColumnFixture.ClearTile(candleSubject);
                LayerColumnFixture.ClearTile(pebbleSupport);
                LayerColumnFixture.ClearTile(pebbleSubject);
                LayerColumnFixture.ClearTile(stonePos);
            }

            yield break;
        }
    }
}
#endif
