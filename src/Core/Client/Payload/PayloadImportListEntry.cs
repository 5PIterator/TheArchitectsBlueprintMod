using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

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
        string? ModVersion = null,
        string? TerrariaVersion = null,
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

            if (PayloadMetadata.IsJsonPath(fullPath))
                return TryReadJson(fullPath, fileStem, modified, out entry, out error);

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
                    docInfo.ModVersion,
                    docInfo.TerrariaVersion,
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

        private static bool TryReadJson(
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
                string json = File.ReadAllText(fullPath, Encoding.UTF8);
                if (!PayloadJsonImport.TryPeekDocumentInfo(json, out PayloadDocumentInfo docInfo, out bool isLegacy, out error))
                {
                    entry = Invalid(fileStem, fullPath, modified, $"Invalid JSON payload | {modified}");
                    return false;
                }

                entry = new PayloadImportListEntry(
                    PayloadMetadata.ResolveProjectName(docInfo.ProjectName, fileStem),
                    fullPath,
                    docInfo.Width > 0 && docInfo.Height > 0,
                    PayloadMetadata.ResolvePlayerName(docInfo.PlayerName),
                    docInfo.Width,
                    docInfo.Height,
                    docInfo.CreatedUtc,
                    modified,
                    docInfo.ModVersion,
                    docInfo.TerrariaVersion,
                    docInfo.ModsLoaded,
                    IsLegacy: isLegacy);
                return entry.IsValid;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                entry = Invalid(fileStem, fullPath, modified, $"Read error | {modified}");
                return false;
            }
        }
    }
}
