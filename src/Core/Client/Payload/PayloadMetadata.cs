using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace TheArchitectsBlueprintMod.Core
{
    public readonly struct PayloadExportMetadata(
        string projectName,
        string playerName,
        string createdUtc,
        string modVersion,
        string? terrariaVersion,
        IReadOnlyList<string>? modsLoaded,
        byte[]? thumbnailPng = null)
    {
        public string ProjectName { get; } = projectName;
        public string PlayerName { get; } = playerName;
        public string CreatedUtc { get; } = createdUtc;
        public string ModVersion { get; } = modVersion;
        public string? TerrariaVersion { get; } = terrariaVersion;
        public IReadOnlyList<string>? ModsLoaded { get; } = modsLoaded;
        public byte[]? ThumbnailPng { get; } = thumbnailPng;
    }

    public readonly struct PayloadDocumentInfo(
        string? projectName,
        string? playerName,
        string? createdUtc,
        int width,
        int height,
        IReadOnlyList<string>? modsLoaded,
        byte[]? thumbnailPng = null,
        string? modVersion = null,
        string? terrariaVersion = null)
    {
        public string? ProjectName { get; } = projectName;
        public string? PlayerName { get; } = playerName;
        public string? CreatedUtc { get; } = createdUtc;
        public int Width { get; } = width;
        public int Height { get; } = height;
        public IReadOnlyList<string>? ModsLoaded { get; } = modsLoaded;
        public byte[]? ThumbnailPng { get; } = thumbnailPng;
        public string? ModVersion { get; } = modVersion;
        public string? TerrariaVersion { get; } = terrariaVersion;
    }

    /// <summary>Canonical payload metadata keys (export / TagIO field names).</summary>
    public static class PayloadMetadataKeys
    {
        public const string ProjectName = "projectName";
        public const string PlayerName = "playerName";
        public const string Dimensions = "dimensions";
        public const string CreatedUtc = "createdUtc";
        public const string Modified = "modified";
        public const string ModVersion = "modVersion";
        public const string TerrariaVersion = "terrariaVersion";
        public const string ModsLoaded = "modsLoaded";

        public static readonly string[] DefaultDisplayOrder =
        [
            Dimensions,
            CreatedUtc,
            Modified,
            ModVersion,
            TerrariaVersion,
            ModsLoaded
        ];

        public static bool IsReserved(string key) =>
            string.Equals(key, ProjectName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, PlayerName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Display fallbacks and path helpers for payload files.</summary>
    internal static class PayloadMetadata
    {
        public const string UnknownProject = "Unknown";
        public const string UnknownPlayer = "Unknown";
        public const string TabExtension = ".tab";
        public const string JsonExtension = ".tab.json";
        public const string LegacyJsonExtension = JsonExtension;

        public static bool IsJsonPath(string fullPath) =>
            fullPath.EndsWith(JsonExtension, StringComparison.OrdinalIgnoreCase);

        public static bool IsLegacyJsonPath(string fullPath) => IsJsonPath(fullPath);

        public static bool IsMirrorJsonRoot(JObject root)
        {
            if (!root.TryGetValue("format", StringComparison.OrdinalIgnoreCase, out JToken? formatToken)
                || formatToken.Type != JTokenType.String)
                return false;

            if (!string.Equals(formatToken.Value<string>(), PayloadTagIO.FormatId, StringComparison.Ordinal))
                return false;

            if (!root.TryGetValue("schemaVersion", StringComparison.OrdinalIgnoreCase, out JToken? schemaToken)
                || schemaToken.Type != JTokenType.Integer)
                return false;

            int schema = schemaToken.Value<int>();
            if (schema < PayloadTagIO.MinSchemaVersion || schema > PayloadTagIO.SchemaVersion)
                return false;

            JToken? listToken = root["payload"] ?? root["cells"];
            if (listToken is not JArray array)
                return false;

            if (array.Count == 0)
                return true;

            if (array[0] is JArray)
                return false;

            if (array[0] is JObject first)
                return !first.ContainsKey("TileType") && !first.ContainsKey("WallType");

            return false;
        }

        public static string ResolveProjectName(string? projectName, string fileStem)
        {
            if (!string.IsNullOrWhiteSpace(projectName))
                return projectName.Trim();
            if (!string.IsNullOrWhiteSpace(fileStem))
                return fileStem;
            return UnknownProject;
        }

        public static string ResolvePlayerName(string? playerName) =>
            string.IsNullOrWhiteSpace(playerName) ? UnknownPlayer : playerName.Trim();

        public static string FileStemFromName(string fileName)
        {
            string stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
            if (stem.EndsWith(".tab", StringComparison.OrdinalIgnoreCase))
                stem = System.IO.Path.GetFileNameWithoutExtension(stem);
            return stem;
        }

        public static string FormatModifiedLine(string fullPath) =>
            $"Modified {System.IO.File.GetLastWriteTimeUtc(fullPath):yyyy-MM-dd HH:mm} UTC";
    }
}
