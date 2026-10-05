#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests.PayloadTests
{
    /// <summary>Validates FromSparse keeps in-bounds cells and drops out-of-bounds placements.</summary>
    internal sealed class SparseFilterScenario : IScenarioTest
    {
        public string Id => "payload.sparse";
        public string Description => "FromSparse keeps in-bounds cells and drops out-of-bounds placements.";

        public IEnumerator Run(Player player, IScenarioTestReporter reporter)
        {
            // --- Feed one in-bounds, one out-of-bounds, and one empty cell into FromSparse.

            if (!ScenarioTestCells.TryCreateStone(out PayloadCell stone, out string? cellError))
            {
                reporter.Fail(Id, cellError ?? "Stone cell build failed.");
                yield break;
            }

            Payload payload = Payload.FromSparse(
                [
                    new Payload.Entry(new Point(0, 0), stone),
                    new Payload.Entry(new Point(4, 0), stone),
                    new Payload.Entry(new Point(1, 1), default),
                ],
                width: 2,
                height: 2);

            Payload expected = Payload.FromSparse([new Payload.Entry(new Point(0, 0), stone)], 2, 2);
            ScenarioPayloadAssert.PayloadsMatch(
                Id,
                reporter,
                expected,
                payload,
                new Rectangle(0, 0, 2, 2),
                "FromSparse filtering kept only the in-bounds stone cell.");
            yield break;
        }
    }
}
#endif
