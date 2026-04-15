using System;
using System.IO;
using System.Text.Json;
using ArchitectsStencilMod.Core.Client.PayloadImport;

namespace ArchitectsStencilMod.Core.Client.PayloadImport.Migration
{
    /// <summary>
    /// Layer-facet payload: root JSON object with <c>schemaVersion</c>, <c>width</c>, <c>height</c>, <c>payload</c> (see <see cref="LayerFacetsV1Dtos"/>).
    /// <see cref="Identify"/> selects this format; <see cref="ImportToCurrent"/> normalizes version and runs <see cref="MigrationSystem.MigrateToLatest"/>.
    /// </summary>
    public static class LayerFacetsV1PayloadFormat
    {
        /// <summary>
        /// Whether <paramref name="root"/> is the object-shaped facet document (not a CheatSheet array).
        /// </summary>
        public static bool Identify(JsonElement root) => root.ValueKind == JsonValueKind.Object;

        /// <summary>
        /// Deserializes a facet object and migrates to <see cref="PayloadJson.CurrentSchemaVersion"/>.
        /// </summary>
        public static PayloadDocument ImportToCurrent(string json, JsonElement root)
        {
            if (!Identify(root))
                throw new InvalidDataException("Root JSON is not a layer-facets object.");

            bool hasSchemaProperty =
                root.TryGetProperty("schemaVersion", out _) || root.TryGetProperty("SchemaVersion", out _);

            PayloadDocument? doc = JsonSerializer.Deserialize<PayloadDocument>(json, PayloadJson.SerializerOptions);
            if (doc is null)
                throw new InvalidDataException("Tile grid payload JSON deserialized to null.");

            if (!hasSchemaProperty)
                doc.SchemaVersion = PayloadJson.CurrentSchemaVersion;
            else if (doc.SchemaVersion == MigrationSystem.CheatSheet)
            {
                throw new InvalidDataException(
                    "schemaVersion 0 (CheatSheet) is only valid as a root JSON array without a schema field.");
            }

            doc.ProjectName ??= string.Empty;
            doc.PlayerName ??= string.Empty;
            doc.Payload ??= [];

            return MigrationSystem.MigrateToLatest(doc);
        }
    }
}
