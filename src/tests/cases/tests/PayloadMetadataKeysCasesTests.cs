using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Cases.Tests;

public sealed class PayloadMetadataKeysCasesTests
{
    [Fact]
    public void IsReserved_MatchesProjectAndPlayerNameCaseInsensitive()
    {
        Assert.True(PayloadMetadataKeys.IsReserved("projectName"));
        Assert.True(PayloadMetadataKeys.IsReserved("PLAYERNAME"));
        Assert.False(PayloadMetadataKeys.IsReserved("dimensions"));
    }

    [Fact]
    public void DefaultDisplayOrder_StartsWithDimensionsAndContainsKnownKeys()
    {
        string[] order = PayloadMetadataKeys.DefaultDisplayOrder;

        Assert.NotEmpty(order);
        Assert.Equal(PayloadMetadataKeys.Dimensions, order[0]);
        Assert.Contains(PayloadMetadataKeys.ModVersion, order);
        Assert.Contains(PayloadMetadataKeys.TerrariaVersion, order);
        Assert.Contains(PayloadMetadataKeys.ModsLoaded, order);
    }
}
