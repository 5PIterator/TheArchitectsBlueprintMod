#if DEBUG
#nullable enable

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Shared JSON fixtures for in-game payload scenario tests.</summary>
    internal static class ScenariosTestFixtures
    {
        internal const string MirrorDocumentJson =
            """
            {
              "format": "TheArchitectsBlueprintMod.Payload",
              "schemaVersion": 4,
              "metadata": {
                "projectName": "MirrorProject",
                "playerName": "Tester",
                "width": 1,
                "height": 1
              },
              "payload": [
                { "x": 0, "y": 0, "c": { "tile": "Tile_1" } }
              ]
            }
            """;

        internal const string LegacySparseJson =
            """
            {
              "metadata": {
                "projectName": "LegacyProject",
                "width": 2,
                "height": 1
              },
              "payload": [
                { "x": 0, "y": 0, "c": { "tile": "Tile_1" } },
                { "x": 1, "y": 0, "c": { "tile": "Tile_1" } }
              ]
            }
            """;
    }
}
#endif
