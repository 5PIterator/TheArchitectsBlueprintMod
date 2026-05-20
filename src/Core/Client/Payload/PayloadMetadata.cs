using System;
using System.Collections.Generic;

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
        byte[]? thumbnailPng = null)
    {
        public string? ProjectName { get; } = projectName;
        public string? PlayerName { get; } = playerName;
        public string? CreatedUtc { get; } = createdUtc;
        public int Width { get; } = width;
        public int Height { get; } = height;
        public IReadOnlyList<string>? ModsLoaded { get; } = modsLoaded;
        public byte[]? ThumbnailPng { get; } = thumbnailPng;
    }

    /// <summary>Display fallbacks and path helpers for payload files.</summary>
    internal static class PayloadMetadata
    {
        public const string UnknownProject = "Unknown";
        public const string UnknownPlayer = "Unknown";
        public const string TabExtension = ".tab";
        public const string LegacyJsonExtension = ".tab.json";

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

        public static bool IsLegacyJsonPath(string fullPath) =>
            fullPath.EndsWith(LegacyJsonExtension, StringComparison.OrdinalIgnoreCase);
    }
}
