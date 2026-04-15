using System;
using System.IO;
using System.Text.Json;
using ArchitectsStencilMod.Core.Client.PayloadImport;

namespace ArchitectsStencilMod.Core.Client.PayloadImport.Migration
{
    /// <summary>
    /// Payload JSON migration: schema version constants, version-agnostic <see cref="ImportToCurrent"/>,
    /// and sequential <see cref="MigrateToLatest"/> using registered <see cref="IPayloadVersionMigrator"/> steps.
    /// </summary>
    public static class MigrationSystem
    {
        /// <summary>CheatSheet brush JSON has no schema field; treated as this version in code.</summary>
        public const int CheatSheet = 0;

        /// <summary>Layer-facet object document (current on-disk shape for this mod).</summary>
        public const int LayerFacetsV1 = 1;

        private static readonly IPayloadVersionMigrator[] Migrators = [];

        /// <summary>
        /// Classify JSON with each format's <c>Identify</c>, then import and migrate to <see cref="PayloadJson.CurrentSchemaVersion"/>.
        /// </summary>
        public static PayloadDocument ImportToCurrent(string json, string? projectName = null, string? playerName = null)
        {
            ArgumentNullException.ThrowIfNull(json);

            using JsonDocument parsed = JsonDocument.Parse(json);
            JsonElement root = parsed.RootElement;

            if (CheatSheetPayloadFormat.Identify(root))
                return CheatSheetPayloadFormat.ImportToCurrent(json, root, projectName, playerName);

            if (LayerFacetsV1PayloadFormat.Identify(root))
                return LayerFacetsV1PayloadFormat.ImportToCurrent(json, root);

            throw new InvalidDataException("Unrecognized tile grid payload JSON shape.");
        }

        /// <summary>
        /// Runs registered migrators until <see cref="PayloadDocument.SchemaVersion"/> equals <see cref="PayloadJson.CurrentSchemaVersion"/>.
        /// </summary>
        public static PayloadDocument MigrateToLatest(PayloadDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            if (document.SchemaVersion == CheatSheet)
            {
                throw new InvalidDataException(
                    "CheatSheet schema (0) must be imported via CheatSheet payload format (root JSON array), not as a document with schemaVersion.");
            }

            if (document.SchemaVersion > PayloadJson.CurrentSchemaVersion)
            {
                throw new NotSupportedException(
                    $"Tile grid payload schemaVersion {document.SchemaVersion} is newer than supported {PayloadJson.CurrentSchemaVersion}.");
            }

            PayloadDocument current = document;
            while (current.SchemaVersion < PayloadJson.CurrentSchemaVersion)
            {
                IPayloadVersionMigrator? step = FindMigrator(current.SchemaVersion);
                if (step is null)
                {
                    throw new NotSupportedException(
                        $"Migration from schemaVersion {current.SchemaVersion} to {PayloadJson.CurrentSchemaVersion} is not implemented.");
                }

                current = step.Migrate(current);
            }

            return current;
        }

        private static IPayloadVersionMigrator? FindMigrator(int fromVersion)
        {
            for (int i = 0; i < Migrators.Length; i++)
            {
                if (Migrators[i].FromVersion == fromVersion)
                    return Migrators[i];
            }

            return null;
        }

        /// <summary>
        /// One upgrade step between stored schema versions (e.g. when <see cref="PayloadJson.CurrentSchemaVersion"/> increments).
        /// </summary>
        public interface IPayloadVersionMigrator
        {
            int FromVersion { get; }

            int ToVersion { get; }

            PayloadDocument Migrate(PayloadDocument document);
        }
    }
}
