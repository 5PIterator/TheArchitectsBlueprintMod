using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TheArchitectsBlueprintMod.UI;
using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Reads, writes, and parses local payload files. Browser session logic lives in <see cref="PayloadBrowser"/>.
    /// </summary>
    public sealed class PayloadSerialization : ModSystem
    {
        public const string PayloadFileExtension = PayloadMetadata.TabExtension;
        public const string ReadablePayloadFileExtension = PayloadMetadata.JsonExtension;
        public const string LegacyPayloadFileExtension = PayloadMetadata.LegacyJsonExtension;
        private const string PayloadDirectoryName = "Payloads";
        private static readonly Regex InvalidFileNameChars = new($"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]", RegexOptions.Compiled);
        internal static bool TryGetPayloadIoHighlight(Player player, out Projection? projection) =>
            PayloadBrowser.TryGetPayloadIoHighlight(player, out projection);

        public override void OnWorldUnload() => PayloadBrowser.Close(commitActivePreview: false);

        public static string GetPayloadDirectory()
        {
            string dir = Path.Combine(TileMappingSystem.GetModDirectory(), PayloadDirectoryName);
            Directory.CreateDirectory(dir);
            return dir;
        }

        internal static bool TryWriteExportedWrapper(Projection projection, string projectDisplayName, string fullPath, out string? error) =>
            TryWriteExportedWrapper(projection, projectDisplayName, fullPath, PayloadExportFormat.CompactTab, out error);

        internal static bool TryWriteExportedWrapper(
            Projection projection,
            string projectDisplayName,
            string fullPath,
            PayloadExportFormat format,
            out string? error)
        {
            error = null;
            ArgumentNullException.ThrowIfNull(projection);
            try
            {
                if (!projection.HasPayload)
                {
                    error = "Projection has no payload.";
                    return false;
                }

                string displayName = string.IsNullOrWhiteSpace(projectDisplayName)
                    ? $"TABPayload{DateTime.Now:yyyyMMdd_HHmmss}"
                    : projectDisplayName.Trim();
                WriteExportWrapperToFile(projection, displayName, fullPath, format);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void WriteExportWrapperToFile(Projection projection, string displayName, string fullPath, PayloadExportFormat format)
        {
            string playerName = ResolvePlayerName(projection.Owner);
            Payload payload = projection.Payload;
            using FileStream stream = File.Create(fullPath);
            if (format == PayloadExportFormat.ReadableJson)
            {
                PayloadExportMetadata meta = PayloadTagIO.CreateExportMetadata(displayName, playerName);
                PayloadTagIO.WriteJsonDocument(stream, payload, meta);
                return;
            }

            PayloadExportMetadata tabMeta = BuildExportMetadata(displayName, playerName, payload);
            PayloadTagIO.WriteDocument(stream, payload, tabMeta);
        }

        private static void WriteExportWrapperToFile(Projection projection, string displayName, string fullPath)
        {
            WriteExportWrapperToFile(projection, displayName, fullPath, PayloadExportFormat.CompactTab);
        }

        private static PayloadExportMetadata BuildExportMetadata(string displayName, string playerName, Payload payload)
        {
            Texture2D? thumb = PayloadPreview.MakeThumbnail(payload);
            try
            {
                byte[]? png = PayloadTagIO.TryEncodeThumbnailPng(thumb);
                return PayloadTagIO.CreateExportMetadata(displayName, playerName, png);
            }
            finally
            {
                thumb?.Dispose();
            }
        }

        private static string ResolvePlayerName(int ownerWhoAmI)
        {
            if ((uint)ownerWhoAmI >= Main.maxPlayers)
                return string.Empty;

            Player player = Main.player[ownerWhoAmI];
            return player.active ? player.name : string.Empty;
        }

        internal static byte[] BuildExportDocumentBytes(Projection projection, string displayName)
        {
            PayloadExportMetadata meta = BuildExportMetadata(displayName, ResolvePlayerName(projection.Owner), projection.Payload);
            using var ms = new MemoryStream();
            PayloadTagIO.WriteDocument(ms, projection.Payload, meta);
            return ms.ToArray();
        }

        internal static bool TryReadPayloadFromFile(string fullPath, out Payload payload, out string? error) =>
            TryReadPayloadFromFile(fullPath, out payload, out _, out error);

        internal static bool TryReadPayloadFromFile(
            string fullPath,
            out Payload payload,
            out PayloadDocumentInfo documentInfo,
            out string? error)
        {
            payload = Payload.Empty;
            documentInfo = default;
            error = null;
            try
            {
                if (!File.Exists(fullPath))
                {
                    error = $"File not found: {fullPath}";
                    return false;
                }

                if (PayloadMetadata.IsJsonPath(fullPath))
                {
                    string json = File.ReadAllText(fullPath, Encoding.UTF8);
                    if (!PayloadJsonImport.TryImport(json, out payload, out documentInfo, out _, out error))
                        return false;
                    return payload.IsDefined;
                }

                using FileStream stream = File.OpenRead(fullPath);
                if (!PayloadTagIO.TryReadDocument(stream, out payload, out documentInfo, out error))
                    return false;

                if (!payload.IsDefined)
                {
                    error = "Document did not contain a defined tile grid.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static bool TryParsePayloadDocument(string json, out Payload payload, out string? error)
        {
            payload = Payload.Empty;
            error = null;
            try
            {
                if (!PayloadJsonImport.TryImport(json, out payload, out _, out _, out error))
                    return false;
                return payload.IsDefined;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static bool TryParsePayloadDocument(byte[] bytes, out Payload payload, out PayloadDocumentInfo info, out string? error)
        {
            payload = Payload.Empty;
            info = default;
            error = null;
            try
            {
                using var ms = new MemoryStream(bytes);
                if (PayloadTagIO.TryReadDocument(ms, out payload, out info, out error))
                    return payload.IsDefined;

                ms.Position = 0;
                string text = Encoding.UTF8.GetString(bytes);
                if (PayloadJsonImport.TryImport(text, out payload, out info, out _, out error))
                    return payload.IsDefined;

                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static string SanitizeFileName(string projectName)
        {
            string sanitized = InvalidFileNameChars.Replace(projectName, "_").Trim(' ', '.');
            return string.IsNullOrWhiteSpace(sanitized) ? string.Empty : sanitized;
        }
    }
}
