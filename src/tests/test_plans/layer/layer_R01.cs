#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core.TestCases.LayerTests
{
    /// <summary>
    /// Asserts glowing-mushroom plants and candles route to Frame/Plant, never Block.
    /// </summary>
    internal sealed class layer_R01 : ITestCase
    {
        public string Id => "layer_R01";
        public string Description => "Glowing mushroom plants and candles classify as Frame/Plant, not Block; stone stays Block";

        public TestCaseType Type => TestCaseType.Regression;

        public IEnumerator Run(Player player, ITestCaseReporter reporter)
        {
            // --- Join LayerColumnsPlaced, observe Frame/Plant vs Block, teardown columns.

            TestChamberSystem chamber = TestChamberSystem.Instance;
            Point mushroomSupport = chamber.LayerSupport(0);
            Point candleSupport = chamber.LayerSupport(1);
            Point stonePos = chamber.LayerSupport(2);

            Point mushroomSubject = default;
            Point candleSubject = default;

            try
            {
                if (!LayerColumnFixture.TryJoinSupportSubject(
                        mushroomSupport,
                        TileID.MushroomGrass,
                        TileID.MushroomPlants,
                        out mushroomSubject,
                        out string? mushroomError))
                {
                    reporter.Fail(Id, mushroomError ?? "Mushroom plant setup failed.");
                    yield break;
                }

                if (!LayerColumnFixture.TryJoinSupportSubject(
                        candleSupport,
                        TileID.Stone,
                        TileID.Candles,
                        out candleSubject,
                        out string? candleError))
                {
                    reporter.Fail(Id, candleError ?? "Candle setup failed.");
                    yield break;
                }

                if (!LayerColumnFixture.TryJoinStoneControl(stonePos, out string? stoneSetupError))
                {
                    reporter.Fail(Id, stoneSetupError ?? "Stone control setup failed.");
                    yield break;
                }

                if (!LayerIdentifyFixture.TryObserveAnchoredOrPlantOwnership(
                        mushroomSubject,
                        "mushroom plant",
                        out string? mushroomFail))
                {
                    reporter.Fail(Id, mushroomFail ?? "Mushroom plant ownership assert failed.");
                    yield break;
                }

                if (!LayerIdentifyFixture.TryObserveAnchoredOrPlantOwnership(
                        candleSubject,
                        "candle",
                        out string? candleFail))
                {
                    reporter.Fail(Id, candleFail ?? "Candle ownership assert failed.");
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

                reporter.Pass(
                    Id,
                    "Mushroom plant and candle are Frame/Plant (not Block); stone remains Block.");
            }
            finally
            {
                LayerColumnFixture.ClearTile(mushroomSupport);
                LayerColumnFixture.ClearTile(mushroomSubject);
                LayerColumnFixture.ClearTile(candleSupport);
                LayerColumnFixture.ClearTile(candleSubject);
                LayerColumnFixture.ClearTile(stonePos);
            }

            yield break;
        }
    }
}
#endif
