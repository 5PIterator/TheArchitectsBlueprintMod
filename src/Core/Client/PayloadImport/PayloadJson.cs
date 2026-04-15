using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArchitectsStencilMod.Core.Client.PayloadImport.Migration;

namespace ArchitectsStencilMod.Core.Client.PayloadImport
{
    public static class PayloadJson
    {
        public const int CurrentSchemaVersion = MigrationSystem.LayerFacetsV1;

        private static readonly JsonSerializerOptions Options = CreateOptions();

        /// <summary>Serializer settings for layer-facet documents (files, network, migration).</summary>
        internal static JsonSerializerOptions SerializerOptions => Options;

        private static JsonSerializerOptions CreateOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
        }

        public static string Serialize(PayloadDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);
            return JsonSerializer.Serialize(document, Options);
        }

        /// <summary>
        /// Loads a payload document via <see cref="MigrationSystem.ImportToCurrent(string, string?, string?)"/>
        /// (format identification + migration to <see cref="CurrentSchemaVersion"/>).
        /// </summary>
        public static PayloadDocument Deserialize(string json)
        {
            PayloadDocument doc = MigrationSystem.ImportToCurrent(json);
            ValidateSchema(doc);
            return doc;
        }

        public static PayloadDocument ReadFromFile(string path)
        {
            string json = File.ReadAllText(path);
            return Deserialize(json);
        }

        public static void WriteToFile(string path, PayloadDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);
            string json = Serialize(document);
            File.WriteAllText(path, json);
        }

        public static void ValidateSchema(PayloadDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);
            if (document.SchemaVersion != CurrentSchemaVersion)
            {
                throw new NotSupportedException(
                    $"Tile grid payload schemaVersion {document.SchemaVersion} is not supported (expected {CurrentSchemaVersion}).");
            }

            if (document.Width < 0 || document.Height < 0)
                throw new InvalidDataException("width and height must be non-negative.");
        }
    }
}
