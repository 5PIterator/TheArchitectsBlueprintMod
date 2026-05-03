using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ArchitectsStencilMod.UI;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Terraria;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Bridges projection payloads to local JSON files and the import/export UI.
    /// </summary>
    public sealed class PayloadSerialization : ModSystem
    {
        public const string PayloadFileExtension = ".asmpayload.json";
        private const string PayloadDirectoryName = "Payloads";
        private const string PayloadFormat = "ArchitectsStencilMod.Payload";
        private const int SchemaVersion = 1;
        private static readonly Regex InvalidFileNameChars = new($"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]", RegexOptions.Compiled);

        /// <summary>Opens the export-name dialog for the provided projection.</summary>
        public static void BeginExport(Projection projection)
        {
            // --- Validate the projection, then delegate name collection to the UI layer.

            ArgumentNullException.ThrowIfNull(projection);
            if (!CanUseProjection(projection, requirePayload: true))
                return;

            string defaultProjectName = $"ASMPayload{DateTime.Now:yyyyMMdd_HHmmss}";
            PayloadFileDialogs.ShowExportNameDialog(
                defaultProjectName,
                projectName => ExportProjection(projection, projectName),
                onCancel: null);
        }

        /// <summary>Opens the import-list dialog for the provided projection.</summary>
        public static void BeginImport(Projection projection)
        {
            // --- Discover local payload files, then delegate selection to the UI layer.

            ArgumentNullException.ThrowIfNull(projection);
            if (!CanUseProjection(projection, requirePayload: false))
                return;

            IReadOnlyList<PayloadImportDialogEntry> entries = DiscoverPayloadFiles();
            if (entries.Count == 0)
            {
                Main.NewText("No payload files found in the ArchitectsStencilMod Payloads folder.", Color.Orange);
                return;
            }

            PayloadFileDialogs.ShowImportListDialog(
                entries,
                fullPath => ImportProjection(projection, fullPath),
                onCancel: null);
        }

        /// <summary>Returns the directory where payload JSON files are stored.</summary>
        public static string GetPayloadDirectory()
        {
            // --- Resolve and create the mod-local payload folder.

            string dir = Path.Combine(TileMappingSystem.GetModDirectory(), PayloadDirectoryName);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>Reads local JSON files and returns display entries for the import UI.</summary>
        public static IReadOnlyList<PayloadImportDialogEntry> DiscoverPayloadFiles()
        {
            // --- Gather every JSON file so metadata-wrapped and raw payload documents can both be selected.

            string dir = GetPayloadDirectory();
            if (!Directory.Exists(dir))
                return [];

            var entries = new List<PayloadImportDialogEntry>();
            foreach (string path in Directory.EnumerateFiles(dir, "*.json").OrderByDescending(File.GetLastWriteTimeUtc))
            {
                entries.Add(CreateDialogEntry(path));
            }

            return entries;
        }

        /// <summary>Exports a projection payload with metadata to the local payload directory.</summary>
        private static void ExportProjection(Projection projection, string projectName)
        {
            // --- Build the metadata wrapper, choose a safe path, and write formatted JSON.

            if (!CanUseProjection(projection, requirePayload: true))
                return;

            try
            {
                string displayName = string.IsNullOrWhiteSpace(projectName)
                    ? $"ASMPayload{DateTime.Now:yyyyMMdd_HHmmss}"
                    : projectName.Trim();
                string rawPayloadJson = projection.Payload.ToJson();
                var wrapper = new JObject
                {
                    ["metadata"] = new JObject
                    {
                        ["format"] = PayloadFormat,
                        ["schemaVersion"] = SchemaVersion,
                        ["projectName"] = displayName,
                        ["createdUtc"] = DateTime.UtcNow.ToString("O"),
                        ["modVersion"] = ModContent.GetInstance<global::ArchitectsStencilMod.ArchitectsStencilMod>().Version.ToString(),
                        ["width"] = projection.Payload.Width,
                        ["height"] = projection.Payload.Height,
                    },
                    ["payload"] = JToken.Parse(rawPayloadJson),
                };

                string path = GetAvailablePayloadPath(displayName);
                File.WriteAllText(path, wrapper.ToString(Formatting.Indented), Encoding.UTF8);
                Main.NewText($"Exported payload: {Path.GetFileName(path)}", Color.LightGreen);
            }
            catch (Exception ex)
            {
                Main.NewText($"Export failed: {ex.Message}", Color.OrangeRed);
            }
        }

        /// <summary>Imports a local JSON payload and applies it to the projection.</summary>
        private static void ImportProjection(Projection projection, string fullPath)
        {
            // --- Read the selected file, unwrap metadata when present, then apply the payload.

            if (!CanUseProjection(projection, requirePayload: false))
                return;

            try
            {
                string json = File.ReadAllText(fullPath, Encoding.UTF8);
                string payloadJson = ExtractPayloadJson(json);
                Payload payload = Payload.FromJson(payloadJson);
                if (!payload.IsDefined)
                {
                    Main.NewText("Import failed: payload file did not contain tile data.", Color.OrangeRed);
                    return;
                }

                projection.ApplyImportedPayload(payload);
                Main.NewText($"Imported payload: {Path.GetFileName(fullPath)}", Color.LightGreen);
            }
            catch (Exception ex)
            {
                Main.NewText($"Import failed: {ex.Message}", Color.OrangeRed);
            }
        }

        /// <summary>Returns the raw payload JSON from a metadata wrapper or a raw payload file.</summary>
        private static string ExtractPayloadJson(string json)
        {
            // --- Treat an object with a payload property as wrapped; otherwise preserve the full document.

            JToken root = JToken.Parse(json);
            if (root is JObject obj && obj.TryGetValue("payload", StringComparison.OrdinalIgnoreCase, out JToken? payloadToken))
                return payloadToken.ToString(Formatting.None);

            return root.ToString(Formatting.None);
        }

        /// <summary>Builds one display entry from a local payload JSON file.</summary>
        private static PayloadImportDialogEntry CreateDialogEntry(string fullPath)
        {
            // --- Prefer metadata fields, then fall back to file name and file timestamp.

            string fileName = Path.GetFileName(fullPath);
            string displayName = Path.GetFileNameWithoutExtension(fileName);
            string details = $"Modified {File.GetLastWriteTimeUtc(fullPath):yyyy-MM-dd HH:mm} UTC";

            try
            {
                JToken root = JToken.Parse(File.ReadAllText(fullPath, Encoding.UTF8));
                if (root is JObject obj && obj["metadata"] is JObject metadata)
                {
                    displayName = metadata.Value<string>("projectName") ?? displayName;
                    int? width = metadata.Value<int?>("width");
                    int? height = metadata.Value<int?>("height");
                    string createdUtc = metadata.Value<string>("createdUtc") ?? "unknown date";
                    details = width.HasValue && height.HasValue
                        ? $"{width.Value}x{height.Value} | {createdUtc}"
                        : createdUtc;
                }
            }
            catch
            {
                details = $"Raw JSON | {details}";
            }

            return new PayloadImportDialogEntry(displayName, details, fullPath);
        }

        /// <summary>Returns a unique filesystem path for a payload project name.</summary>
        private static string GetAvailablePayloadPath(string projectName)
        {
            // --- Sanitize the requested name and append a numeric suffix until the path is free.

            string safeName = SanitizeFileName(projectName);
            string dir = GetPayloadDirectory();
            string path = Path.Combine(dir, safeName + PayloadFileExtension);
            int suffix = 2;
            while (File.Exists(path))
            {
                path = Path.Combine(dir, $"{safeName}_{suffix}{PayloadFileExtension}");
                suffix++;
            }

            return path;
        }

        /// <summary>Converts a project name into a non-empty filename stem.</summary>
        private static string SanitizeFileName(string projectName)
        {
            // --- Replace invalid filename characters and trim whitespace/dots.

            string sanitized = InvalidFileNameChars.Replace(projectName, "_").Trim(' ', '.');
            return string.IsNullOrWhiteSpace(sanitized)
                ? $"ASMPayload{DateTime.Now:yyyyMMdd_HHmmss}"
                : sanitized;
        }

        /// <summary>Returns true when the projection can be used for import or export.</summary>
        private static bool CanUseProjection(Projection projection, bool requirePayload)
        {
            // --- Reject invalid, locked, or empty projections before file/UI work begins.

            if (Main.dedServ || Main.gameMenu)
                return false;

            if (!projection.IsDefined || projection.IsLocked)
                return false;

            if (requirePayload && !projection.HasPayload)
            {
                Main.NewText("Export failed: projection has no payload.", Color.OrangeRed);
                return false;
            }

            return true;
        }
    }
}
