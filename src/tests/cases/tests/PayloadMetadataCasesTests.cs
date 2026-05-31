using Newtonsoft.Json.Linq;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Cases.Tests;

public sealed class PayloadMetadataCasesTests
{
    [Fact]
    public void FileStemFromName_RemovesTabAndJsonExtensions()
    {
        string stemFromTab = PayloadMetadata.FileStemFromName("castle.tab");
        string stemFromJson = PayloadMetadata.FileStemFromName("castle.tab.json");

        Assert.Equal("castle", stemFromTab);
        Assert.Equal("castle", stemFromJson);
    }

    [Fact]
    public void ResolveProjectName_PrefersProjectNameThenFileStemThenFallback()
    {
        string fromProjectName = PayloadMetadata.ResolveProjectName("  My Project  ", "ignored");
        string fromFileStem = PayloadMetadata.ResolveProjectName("   ", "my_file");
        string fromFallback = PayloadMetadata.ResolveProjectName("", "");

        Assert.Equal("My Project", fromProjectName);
        Assert.Equal("my_file", fromFileStem);
        Assert.Equal(PayloadMetadata.UnknownProject, fromFallback);
    }

    [Fact]
    public void ResolvePlayerName_UsesUnknownForBlankValues()
    {
        string fromName = PayloadMetadata.ResolvePlayerName("  Alice  ");
        string fromBlank = PayloadMetadata.ResolvePlayerName(" ");
        string fromNull = PayloadMetadata.ResolvePlayerName(null);

        Assert.Equal("Alice", fromName);
        Assert.Equal(PayloadMetadata.UnknownPlayer, fromBlank);
        Assert.Equal(PayloadMetadata.UnknownPlayer, fromNull);
    }

    [Fact]
    public void IsJsonPath_OnlyMatchesTabJsonFiles()
    {
        Assert.True(PayloadMetadata.IsJsonPath("payload.tab.json"));
        Assert.True(PayloadMetadata.IsJsonPath(@"C:\tmp\payload.TAB.JSON"));
        Assert.False(PayloadMetadata.IsJsonPath("payload.tab"));
    }

    [Fact]
    public void IsMirrorJsonRoot_ReturnsTrueForMirrorPayloadDocument()
    {
        JObject root = JObject.Parse(
            """
            {
              "format": "TheArchitectsBlueprintMod.Payload",
              "schemaVersion": 4,
              "metadata": { "width": 1, "height": 1 },
              "payload": [
                { "x": 0, "y": 0, "c": { "tile": "Tile_1" } }
              ]
            }
            """);

        bool result = PayloadMetadata.IsMirrorJsonRoot(root);
        Assert.True(result);
    }

    [Fact]
    public void IsMirrorJsonRoot_ReturnsFalseForLegacyCellRows()
    {
        JObject root = JObject.Parse(
            """
            {
              "format": "TheArchitectsBlueprintMod.Payload",
              "schemaVersion": 4,
              "metadata": { "width": 1, "height": 1 },
              "payload": [
                { "TileType": 1, "WallType": 0 }
              ]
            }
            """);

        bool result = PayloadMetadata.IsMirrorJsonRoot(root);
        Assert.False(result);
    }
}
