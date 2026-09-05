using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Cases.Tests;

/// <summary>Headless peek-only JSON tests (no Payload materialization).</summary>
public sealed class PayloadJsonPeekCasesTests
{
    private const string LegacySparseJson =
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

    [Fact]
    public void TryPeekDocumentInfo_LegacySparseJson_MarksLegacyAndReadsMetadata()
    {
        bool ok = PayloadJsonImport.TryPeekDocumentInfo(LegacySparseJson, out PayloadDocumentInfo info, out bool isLegacy, out string? error);
        Assert.True(ok, error);
        Assert.True(isLegacy);
        Assert.Equal("LegacyProject", info.ProjectName);
        Assert.Equal(2, info.Width);
        Assert.Equal(1, info.Height);
    }

    [Fact]
    public void TryPeekDocumentInfo_EmptyJson_Fails()
    {
        bool ok = PayloadJsonImport.TryPeekDocumentInfo("", out _, out _, out string? error);
        Assert.False(ok);
        Assert.Equal("Empty document.", error);
    }
}
