using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>One payload file entry for the import dialog (no UI).</summary>
    public readonly record struct PayloadImportListEntry(
        string DisplayName,
        string FullPath,
        bool IsValid = true,
        string PlayerName = PayloadMetadata.UnknownPlayer,
        int Width = 0,
        int Height = 0,
        string? CreatedUtc = null,
        string? ModifiedUtc = null,
        IReadOnlyList<string>? ModsLoaded = null,
        bool IsLegacy = false,
        string? ErrorSummary = null,
        byte[]? ThumbnailPng = null)
    {
        internal static IReadOnlyList<PayloadImportListEntry> DiscoverInDirectory(string directory)
        {
            if (!Directory.Exists(directory))
                return [];

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pattern in new[] { $"*{PayloadMetadata.TabExtension}", $"*{PayloadMetadata.LegacyJsonExtension}" })
            {
                foreach (string path in Directory.EnumerateFiles(directory, pattern))
                    paths.Add(path);
            }

            var entries = new List<PayloadImportListEntry>();
            foreach (string path in paths.OrderByDescending(File.GetLastWriteTimeUtc))
            {
                TryRead(path, out PayloadImportListEntry entry, out _);
                entries.Add(entry);
            }

            return entries;
        }

        internal static bool TryRead(string fullPath, out PayloadImportListEntry entry, out string? error)
        {
            string fileStem = PayloadMetadata.FileStemFromName(Path.GetFileName(fullPath));
            string modified = PayloadMetadata.FormatModifiedLine(fullPath);

            if (PayloadMetadata.IsLegacyJsonPath(fullPath))
                return TryReadLegacyJson(fullPath, fileStem, modified, out entry, out error);

            try
            {
                using FileStream stream = File.OpenRead(fullPath);
                if (!PayloadTagIO.TryReadDocumentInfo(stream, out PayloadDocumentInfo docInfo, out error))
                {
                    entry = Invalid(fileStem, fullPath, modified, $"Invalid TAB file | {modified}");
                    return false;
                }

                entry = new PayloadImportListEntry(
                    PayloadMetadata.ResolveProjectName(docInfo.ProjectName, fileStem),
                    fullPath,
                    true,
                    PayloadMetadata.ResolvePlayerName(docInfo.PlayerName),
                    docInfo.Width,
                    docInfo.Height,
                    docInfo.CreatedUtc,
                    modified,
                    docInfo.ModsLoaded,
                    ThumbnailPng: docInfo.ThumbnailPng);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                entry = Invalid(fileStem, fullPath, modified, $"Read error | {modified}");
                return false;
            }
        }

        private static PayloadImportListEntry Invalid(string displayName, string fullPath, string modified, string errorSummary) =>
            new(displayName, fullPath, false, ModifiedUtc: modified, ErrorSummary: errorSummary);

        private static bool TryReadLegacyJson(
            string fullPath,
            string fileStem,
            string modified,
            out PayloadImportListEntry entry,
            out string? error)
        {
            entry = default;
            error = null;

            try
            {
                using var stream = File.OpenRead(fullPath);
                using var reader = new JsonTextReader(new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    CloseInput = true,
                };

                if (!reader.Read())
                {
                    error = "Empty file.";
                    entry = Invalid(fileStem, fullPath, modified, $"Empty file | {modified}");
                    return false;
                }

                if (reader.TokenType == JsonToken.StartObject)
                {
                    if (!TryReadLegacyWrapped(reader, fileStem, modified, fullPath, out entry, out error))
                        return false;
                    return entry.IsValid;
                }

                if (reader.TokenType == JsonToken.StartArray)
                {
                    if (!TryPeekLegacyRawGridDimensions(reader, out int width, out int height, out error))
                    {
                        entry = Invalid(fileStem, fullPath, modified, $"Invalid grid | {modified}");
                        return false;
                    }

                    entry = new PayloadImportListEntry(
                        fileStem,
                        fullPath,
                        true,
                        Width: width,
                        Height: height,
                        ModifiedUtc: modified,
                        IsLegacy: true);
                    return true;
                }

                error = "Root must be an object (TAB wrapper) or array (raw grid).";
                entry = Invalid(fileStem, fullPath, modified, $"Unsupported format | {modified}");
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                entry = Invalid(fileStem, fullPath, modified, $"Read error | {modified}");
                return false;
            }
        }

        private static bool TryReadLegacyWrapped(
            JsonTextReader reader,
            string fileStem,
            string modified,
            string fullPath,
            out PayloadImportListEntry entry,
            out string? error)
        {
            error = null;
            string? projectName = null;
            string? playerName = null;
            string? createdUtc = null;
            int? width = null;
            int? height = null;
            bool hasMetadata = false;
            bool payloadIsArray = false;
            bool sawPayload = false;

            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndObject)
                    break;

                if (reader.TokenType != JsonToken.PropertyName)
                    continue;

                string prop = (string)reader.Value!;
                if (!reader.Read())
                    break;

                if (string.Equals(prop, "metadata", StringComparison.OrdinalIgnoreCase))
                {
                    if (reader.TokenType != JsonToken.StartObject)
                    {
                        error = "metadata must be an object.";
                        entry = Invalid(fileStem, fullPath, modified, $"Invalid wrapper | {modified}");
                        return false;
                    }

                    hasMetadata = true;
                    ReadLegacyMetadataObject(reader, ref projectName, ref playerName, ref createdUtc, ref width, ref height);
                    continue;
                }

                if (string.Equals(prop, "payload", StringComparison.OrdinalIgnoreCase))
                {
                    sawPayload = true;
                    payloadIsArray = reader.TokenType == JsonToken.StartArray;
                    reader.Skip();
                }
            }

            if (hasMetadata && sawPayload && !payloadIsArray)
            {
                error = "payload must be an array.";
                entry = Invalid(fileStem, fullPath, modified, $"Invalid wrapper | {modified}");
                return false;
            }

            if (hasMetadata && !sawPayload)
            {
                error = "Wrapped document missing payload.";
                entry = Invalid(fileStem, fullPath, modified, $"Invalid wrapper | {modified}");
                return false;
            }

            entry = new PayloadImportListEntry(
                PayloadMetadata.ResolveProjectName(projectName, fileStem),
                fullPath,
                sawPayload && payloadIsArray,
                PayloadMetadata.ResolvePlayerName(playerName),
                width ?? 0,
                height ?? 0,
                createdUtc,
                modified,
                IsLegacy: true);
            return entry.IsValid;
        }

        private static void ReadLegacyMetadataObject(
            JsonTextReader reader,
            ref string? projectName,
            ref string? playerName,
            ref string? createdUtc,
            ref int? width,
            ref int? height)
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndObject)
                    return;

                if (reader.TokenType != JsonToken.PropertyName)
                    continue;

                string name = (string)reader.Value!;
                if (!reader.Read())
                    return;

                switch (name.ToLowerInvariant())
                {
                    case "projectname":
                        projectName = reader.Value as string;
                        break;
                    case "playername":
                        playerName = reader.Value as string;
                        break;
                    case "createdutc":
                        createdUtc = reader.Value as string;
                        break;
                    case "width" when reader.Value is long or int:
                        width = Convert.ToInt32(reader.Value);
                        break;
                    case "height" when reader.Value is long or int:
                        height = Convert.ToInt32(reader.Value);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
        }

        private static bool TryPeekLegacyRawGridDimensions(JsonTextReader reader, out int width, out int height, out string? error)
        {
            width = 0;
            height = 0;
            error = null;

            if (reader.TokenType != JsonToken.StartArray)
            {
                error = "Expected array root.";
                return false;
            }

            if (!reader.Read())
            {
                error = "Truncated grid.";
                return false;
            }

            if (reader.TokenType == JsonToken.EndArray)
                return true;

            if (reader.TokenType != JsonToken.StartArray)
            {
                error = "Expected 2D array (rows of arrays).";
                return false;
            }

            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndArray)
                    break;
                width++;
                reader.Skip();
            }

            height = 1;
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndArray)
                    break;

                if (reader.TokenType != JsonToken.StartArray)
                {
                    error = "Invalid row in raw grid.";
                    return false;
                }

                height++;
                reader.Skip();
            }

            return true;
        }
    }
}
