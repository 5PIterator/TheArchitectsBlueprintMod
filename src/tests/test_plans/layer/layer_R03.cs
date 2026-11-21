#if DEBUG
#nullable enable

using System.Collections;
using Terraria;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Core.TestCases.LayerTests
{
    /// <summary>
    /// Pins ModdedMap.itr and asserts Calamity AstralDirt zero-count plus AstralNormalSmallPiles Echo ammo.
    /// Abandons when CalamityMod is not loaded.
    /// </summary>
    internal sealed class layer_R03 : ITestCase
    {
        private const string MapFileName = "ModdedMap.itr";
        private const string RequiredMod = "CalamityMod";
        private const string AstralDirtTileName = "CalamityMod/AstralDirt";
        private const string AstralDirtItemName = "CalamityMod/AstralDirt";
        private const string AstralPilesTileName = "CalamityMod/AstralNormalSmallPiles";
        private const string AstralPilesEchoTileName = "CalamityMod/AstralNormalSmallPilesEcho";

        public string Id => "layer_R03";
        public string Description =>
            "ModdedMap.itr: Calamity AstralDirt zero-count + AstralNormalSmallPiles Echo ammo (abandons without CalamityMod)";

        public TestCaseType Type => TestCaseType.Regression;

        public IEnumerator Run(Player player, ITestCaseReporter reporter)
        {
            // --- Guard loadability and Calamity, pin ModdedMap, assert AstralDirt + Echo ammo rows.

            if (!TestCaseGuards.RequireInteractionMapLoadable(Id, reporter))
                yield break;

            if (!TestCaseGuards.RequireModsLoaded(Id, reporter, RequiredMod))
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
            // --- Resolve Calamity tile/item ids, then assert zero-count and Echo ammo against ModdedMap.

            int? astralDirtTile = ContentNameResolver.ResolveMain("Tile", AstralDirtTileName);
            if (astralDirtTile is not > 0)
            {
                reporter.Fail(Id, $"Could not resolve tile {AstralDirtTileName} while {RequiredMod} is loaded.");
                yield break;
            }

            int? astralDirtItem = ContentNameResolver.ResolveItem(AstralDirtItemName);
            if (astralDirtItem is not > 0)
            {
                reporter.Fail(Id, $"Could not resolve item {AstralDirtItemName} while {RequiredMod} is loaded.");
                yield break;
            }

            if (!LayerIdentifyFixture.TryObserveZeroCountTileChannel(
                    astralDirtTile.Value,
                    style: 0,
                    expectedVisualItemId: astralDirtItem.Value,
                    out string? dirtFail))
            {
                reporter.Fail(Id, dirtFail ?? "ModdedMap AstralDirt zero-count channel assert failed.");
                yield break;
            }

            int? pilesTile = ContentNameResolver.ResolveMain("Tile", AstralPilesTileName);
            int? pilesEchoTile = ContentNameResolver.ResolveMain("Tile", AstralPilesEchoTileName);
            if (pilesTile is not > 0)
            {
                reporter.Fail(Id, $"Could not resolve tile {AstralPilesTileName} while {RequiredMod} is loaded.");
                yield break;
            }

            if (pilesEchoTile is not > 0)
            {
                reporter.Fail(Id, $"Could not resolve tile {AstralPilesEchoTileName} while {RequiredMod} is loaded.");
                yield break;
            }

            if (!LayerIdentifyFixture.TryObserveEchoAmmoRow(
                    pilesTile.Value,
                    style: 0,
                    expectedFakeTileType: pilesEchoTile.Value,
                    expectedAmmoItemId: astralDirtItem.Value,
                    out string? echoFail))
            {
                reporter.Fail(Id, echoFail ?? "ModdedMap AstralNormalSmallPiles Echo ammo assert failed.");
                yield break;
            }

            reporter.Pass(
                Id,
                $"Pinned {mapPath}: {AstralDirtTileName} zero-count → item; "
                + $"{AstralPilesTileName} Fake Echo + ammo {AstralDirtItemName}.");
            yield break;
        }
    }
}
#endif
