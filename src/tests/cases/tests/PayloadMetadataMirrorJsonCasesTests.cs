using Newtonsoft.Json.Linq;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Cases.Tests;

public sealed class PayloadMetadataMirrorJsonCasesTests
{
    [Fact]
    public void IsMirrorJsonRoot_RejectsMissingFormat()
    {
        JObject root = JObject.Parse("""{ "schemaVersion": 4, "payload": [] }""");
        Assert.False(PayloadMetadata.IsMirrorJsonRoot(root));
    }

    [Fact]
    public void IsMirrorJsonRoot_RejectsSchemaBelowMinimum()
    {
        JObject root = JObject.Parse(
            """
            {
              "format": "TheArchitectsBlueprintMod.Payload",
              "schemaVersion": 2,
              "payload": []
            }
            """);

        Assert.False(PayloadMetadata.IsMirrorJsonRoot(root));
    }

    [Fact]
    public void IsMirrorJsonRoot_RejectsSchemaAboveMaximum()
    {
        int futureSchema = PayloadTagIO.SchemaVersion + 1;
        string json =
            $"{{\"format\":\"TheArchitectsBlueprintMod.Payload\",\"schemaVersion\":{futureSchema},\"payload\":[]}}";
        JObject root = JObject.Parse(json);

        Assert.False(PayloadMetadata.IsMirrorJsonRoot(root));
    }

    [Fact]
    public void IsMirrorJsonRoot_AcceptsCellsAlias()
    {
        JObject root = JObject.Parse(
            """
            {
              "format": "TheArchitectsBlueprintMod.Payload",
              "schemaVersion": 4,
              "cells": [
                { "x": 0, "y": 0, "c": { "tile": "Tile_1" } }
              ]
            }
            """);

        Assert.True(PayloadMetadata.IsMirrorJsonRoot(root));
    }

    [Fact]
    public void IsMirrorJsonRoot_AcceptsEmptyPayloadList()
    {
        JObject root = JObject.Parse(
            """
            {
              "format": "TheArchitectsBlueprintMod.Payload",
              "schemaVersion": 4,
              "payload": []
            }
            """);

        Assert.True(PayloadMetadata.IsMirrorJsonRoot(root));
    }

    [Fact]
    public void IsMirrorJsonRoot_RejectsDenseGridRows()
    {
        JObject root = JObject.Parse(
            """
            {
              "format": "TheArchitectsBlueprintMod.Payload",
              "schemaVersion": 4,
              "payload": [
                [ { "tile": "Tile_1" }, null ]
              ]
            }
            """);

        Assert.False(PayloadMetadata.IsMirrorJsonRoot(root));
    }
}
