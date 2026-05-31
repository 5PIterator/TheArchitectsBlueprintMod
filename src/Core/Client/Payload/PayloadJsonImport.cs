using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Imports various JSON payload shapes and normalizes them to sparse <see cref="Payload"/>.</summary>
    internal static class PayloadJsonImport
    {
        private const string KeyPayload = "payload";
        private const string KeyMetadata = "metadata";
        private const string KeyCellBody = "c";

        public static bool TryImport(
            string json,
            out Payload payload,
            out PayloadDocumentInfo info,
            out bool isLegacy,
            out string? error)
        {
            payload = Payload.Empty;
            info = default;
            isLegacy = false;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Empty document.";
                return false;
            }

            try
            {
                JToken root = JToken.Parse(json);

                if (root is JObject obj && PayloadMetadata.IsMirrorJsonRoot(obj))
                {
                    isLegacy = false;
                    return PayloadTagIO.TryReadJsonDocument(json, out payload, out info, out error);
                }

                isLegacy = true;

                if (root is JObject wrapObj)
                    return TryImportWrappedObject(wrapObj, out payload, out info, out error);

                if (root is JArray gridArray)
                    return TryImportDenseGrid(gridArray, metadataWidth: 0, metadataHeight: 0, null, out payload, out info, out error);

                error = "Unsupported JSON root.";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TryPeekDocumentInfo(string json, out PayloadDocumentInfo info, out bool isLegacy, out string? error)
        {
            info = default;
            isLegacy = false;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Empty document.";
                return false;
            }

            try
            {
                JToken root = JToken.Parse(json);

                if (root is JObject obj && PayloadMetadata.IsMirrorJsonRoot(obj))
                {
                    isLegacy = false;
                    return PayloadTagIO.TryReadJsonDocumentInfo(json, out info, out error);
                }

                isLegacy = true;

                if (root is JObject wrapObj)
                {
                    ReadMetadataFromObject(wrapObj[KeyMetadata] as JObject, out string? projectName, out string? playerName,
                        out string? createdUtc, out int width, out int height, out IReadOnlyList<string>? modsLoaded,
                        out string? modVersion, out string? terrariaVersion);

                    JToken? payloadToken = wrapObj[KeyPayload] ?? wrapObj["cells"];
                    if (payloadToken is JArray payloadArray)
                    {
                        if (width <= 0 || height <= 0)
                            InferDimensionsFromPayloadArray(payloadArray, out width, out height);

                        if (payloadArray.Count > 0 && payloadArray[0] is JArray)
                        {
                            if (width <= 0 || height <= 0)
                                PeekDenseGridDimensions(payloadArray, out width, out height);
                        }
                    }

                    info = new PayloadDocumentInfo(projectName, playerName, createdUtc, width, height, modsLoaded,
                        thumbnailPng: null, modVersion, terrariaVersion);
                    return width > 0 && height > 0 || payloadToken is JArray { Count: 0 };
                }

                if (root is JArray gridArray)
                {
                    PeekDenseGridDimensions(gridArray, out int width, out int height);
                    info = new PayloadDocumentInfo(null, null, null, width, height, null);
                    return true;
                }

                error = "Unsupported JSON root.";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryImportWrappedObject(JObject root, out Payload payload, out PayloadDocumentInfo info, out string? error)
        {
            payload = Payload.Empty;
            info = default;
            error = null;

            ReadMetadataFromObject(root[KeyMetadata] as JObject, out string? projectName, out string? playerName,
                out string? createdUtc, out int width, out int height, out IReadOnlyList<string>? modsLoaded,
                out string? modVersion, out string? terrariaVersion);

            JToken? payloadToken = root[KeyPayload] ?? root["cells"];
            if (payloadToken is null)
            {
                error = "Wrapped document missing payload.";
                return false;
            }

            bool ok;
            if (payloadToken is JArray payloadArray)
            {
                if (payloadArray.Count > 0 && payloadArray[0] is JArray)
                    ok = TryImportDenseGrid(payloadArray, width, height, modsLoaded, out payload, out _, out error);
                else
                    ok = TryImportSparseList(payloadArray, width, height, out payload, out error);
            }
            else
            {
                error = "payload must be an array.";
                return false;
            }

            if (!ok)
                return false;

            if (width <= 0 || height <= 0)
            {
                width = payload.Width;
                height = payload.Height;
            }

            info = new PayloadDocumentInfo(projectName, playerName, createdUtc, width, height, modsLoaded,
                thumbnailPng: null, modVersion, terrariaVersion);
            return payload.IsDefined;
        }

        private static bool TryImportDenseGrid(
            JArray rows,
            int metadataWidth,
            int metadataHeight,
            IReadOnlyList<string>? _,
            out Payload payload,
            out PayloadDocumentInfo info,
            out string? error)
        {
            payload = Payload.Empty;
            info = default;
            error = null;

            int height = rows.Count;
            if (height <= 0)
            {
                error = "Grid has no rows.";
                return false;
            }

            if (rows[0] is not JArray firstRow)
            {
                error = "Expected 2D array (rows of arrays).";
                return false;
            }

            int width = firstRow.Count;
            if (metadataWidth > 0)
                width = metadataWidth;
            if (metadataHeight > 0)
                height = metadataHeight;

            var entries = new List<Payload.Entry>();
            int rowCount = Math.Min(height, rows.Count);
            for (int y = 0; y < rowCount; y++)
            {
                if (rows[y] is not JArray row)
                    continue;

                int colCount = Math.Min(width, row.Count);
                for (int x = 0; x < colCount; x++)
                {
                    JToken cellToken = row[x];
                    if (cellToken.Type == JTokenType.Null)
                        continue;

                    if (!TryCellFromToken(cellToken, out PayloadCell cell) || !cell.HasSparseContent)
                        continue;

                    entries.Add(new Payload.Entry(new Point(x, y), cell));
                }
            }

            payload = Payload.FromSparse(entries, width, height);
            if (!payload.IsDefined)
            {
                error = "Document did not contain a defined tile grid.";
                return false;
            }

            return true;
        }

        private static bool TryImportSparseList(JArray list, int metadataWidth, int metadataHeight, out Payload payload, out string? error)
        {
            payload = Payload.Empty;
            error = null;

            var entries = new List<Payload.Entry>(list.Count);
            int maxX = -1;
            int maxY = -1;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is not JObject entryObj)
                    continue;

                if (entryObj["x"] is null || entryObj["y"] is null)
                    continue;

                int x = entryObj["x"]!.Value<int>();
                int y = entryObj["y"]!.Value<int>();

                JToken? bodyToken = entryObj[KeyCellBody];
                if (bodyToken is null && HasTileSnapshotShape(entryObj))
                    bodyToken = entryObj;

                if (bodyToken is null)
                    continue;

                if (!TryCellFromToken(bodyToken, out PayloadCell cell) || !cell.HasSparseContent)
                    continue;

                entries.Add(new Payload.Entry(new Point(x, y), cell));
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }

            int width = metadataWidth > 0 ? metadataWidth : maxX + 1;
            int height = metadataHeight > 0 ? metadataHeight : maxY + 1;
            if (width <= 0 || height <= 0)
            {
                error = "Could not resolve payload dimensions.";
                return false;
            }

            payload = Payload.FromSparse(entries, width, height);
            if (!payload.IsDefined)
            {
                error = "Document did not contain a defined tile grid.";
                return false;
            }

            return true;
        }

        private static bool TryCellFromToken(JToken token, out PayloadCell cell)
        {
            cell = default;
            if (token is JObject obj)
            {
                if (obj.ContainsKey("tile") || obj.ContainsKey("wall"))
                    return PayloadTagIO.TryCellFromJObject(obj, out cell);

                if (HasTileSnapshotShape(obj))
                {
                    try
                    {
                        cell = PayloadCell.FromJson(obj.ToString(Formatting.None));
                        return !cell.IsEmpty;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }

            return false;
        }

        private static bool HasTileSnapshotShape(JObject obj) =>
            obj.ContainsKey("TileType") || obj.ContainsKey("WallType");

        private static void InferDimensionsFromPayloadArray(JArray list, out int width, out int height)
        {
            width = 0;
            height = 0;
            int maxX = -1;
            int maxY = -1;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is not JObject entryObj)
                    continue;
                if (entryObj["x"] is null || entryObj["y"] is null)
                    continue;

                maxX = Math.Max(maxX, entryObj["x"]!.Value<int>());
                maxY = Math.Max(maxY, entryObj["y"]!.Value<int>());
            }

            width = maxX + 1;
            height = maxY + 1;
        }

        private static void PeekDenseGridDimensions(JArray rows, out int width, out int height)
        {
            width = 0;
            height = rows.Count;
            if (height > 0 && rows[0] is JArray firstRow)
                width = firstRow.Count;
        }

        private static void ReadMetadataFromObject(
            JObject? meta,
            out string? projectName,
            out string? playerName,
            out string? createdUtc,
            out int width,
            out int height,
            out IReadOnlyList<string>? modsLoaded,
            out string? modVersion,
            out string? terrariaVersion)
        {
            projectName = null;
            playerName = null;
            createdUtc = null;
            width = 0;
            height = 0;
            modsLoaded = null;
            modVersion = null;
            terrariaVersion = null;

            if (meta is null)
                return;

            if (meta.TryGetValue("projectName", StringComparison.OrdinalIgnoreCase, out JToken? pn))
                projectName = pn.Type == JTokenType.String ? pn.Value<string>() : null;
            if (meta.TryGetValue("playerName", StringComparison.OrdinalIgnoreCase, out JToken? pl))
                playerName = pl.Type == JTokenType.String ? pl.Value<string>() : null;
            if (meta.TryGetValue("createdUtc", StringComparison.OrdinalIgnoreCase, out JToken? cu))
                createdUtc = cu.Type == JTokenType.String ? cu.Value<string>() : null;
            if (meta.TryGetValue("creationDate", StringComparison.OrdinalIgnoreCase, out JToken? cd) && createdUtc is null)
                createdUtc = cd.Type == JTokenType.String ? cd.Value<string>() : null;
            if (meta.TryGetValue("modVersion", StringComparison.OrdinalIgnoreCase, out JToken? mv))
                modVersion = mv.Type == JTokenType.String ? mv.Value<string>() : null;
            if (meta.TryGetValue("terrariaVersion", StringComparison.OrdinalIgnoreCase, out JToken? tv))
                terrariaVersion = tv.Type == JTokenType.String ? tv.Value<string>() : null;

            if (meta.TryGetValue("width", StringComparison.OrdinalIgnoreCase, out JToken? w))
                width = w.Type == JTokenType.Integer ? w.Value<int>() : 0;
            if (meta.TryGetValue("height", StringComparison.OrdinalIgnoreCase, out JToken? h))
                height = h.Type == JTokenType.Integer ? h.Value<int>() : 0;

            if (meta.TryGetValue("modsLoaded", StringComparison.OrdinalIgnoreCase, out JToken? mods) && mods is JArray modsArray)
            {
                var list = new List<string>(modsArray.Count);
                for (int i = 0; i < modsArray.Count; i++)
                {
                    if (modsArray[i].Type == JTokenType.String)
                    {
                        string? name = modsArray[i].Value<string>();
                        if (!string.IsNullOrEmpty(name))
                            list.Add(name);
                    }
                }

                if (list.Count > 0)
                    modsLoaded = list;
            }
        }
    }
}
