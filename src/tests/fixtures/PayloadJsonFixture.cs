#if DEBUG
#nullable enable

using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.TestCases
{
    /// <summary>Test Fixture: embedded JSON documents for import Cases.</summary>
    internal static class PayloadJsonFixture
    {
        internal static string MirrorDocumentJson =>
            $$"""
            {
              "format": "{{PayloadTagIO.FormatId}}",
              "schemaVersion": {{PayloadTagIO.SchemaVersion}},
              "metadata": {
                "projectName": "MirrorProject",
                "playerName": "Tester",
                "width": 1,
                "height": 1
              },
              "payload": [
                { "x": 0, "y": 0, "c": { "tile": "Tile_1", "style": 0 } }
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
                { "x": 0, "y": 0, "c": { "tile": "Tile_1", "style": 0 } },
                { "x": 1, "y": 0, "c": { "tile": "Tile_1", "style": 0 } }
              ]
            }
            """;
    }
}
#endif
