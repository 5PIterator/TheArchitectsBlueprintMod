using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using TheArchitectsBlueprintMod.Core.Layers;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Sparse payload documents via <see cref="TagIO"/> (schema 3–5).</summary>
    internal static class PayloadTagIO
    {
        public const string FormatId = "TheArchitectsBlueprintMod.Payload";
        public const int SchemaVersion = 5;
        public const int MinSchemaVersion = 3;

        private const string KeyFormat = "format";
        private const string KeySchemaVersion = "schemaVersion";
        private const string KeyMetadata = "metadata";
        private const string KeyPayload = "payload";
        private const string KeyEntities = "entities";
        private const string KeyModsLoaded = "modsLoaded";
        private const string KeyLegacyCells = "cells";
        private const string KeyCellBody = "c";
        private const string KeyThumbnailPng = "thumbnailPng";
        private const string KeyEntityName = "n";
        private const string KeyEntityData = "d";

        [Flags]
        private enum CellFlags : int
        {
            HasTile = 1 << 0,
            IsActuated = 1 << 1,
            IsHalfBlock = 1 << 2,
            TileInvisible = 1 << 3,
            WallInvisible = 1 << 4,
            TileFullbright = 1 << 5,
            WallFullbright = 1 << 6,
            SkipLiquid = 1 << 7,
            CheckingLiquid = 1 << 8,
            HasActuator = 1 << 9,
            RedWire = 1 << 10,
            BlueWire = 1 << 11,
            GreenWire = 1 << 12,
            YellowWire = 1 << 13,
        }

        public static PayloadExportMetadata CreateExportMetadata(string projectName, string playerName, byte[]? thumbnailPng = null)
        {
            var mods = new List<string>();
            try
            {
                for (int i = 0; i < ModLoader.Mods.Length; i++)
                {
                    Mod? m = ModLoader.Mods[i];
                    if (m is not null && !string.IsNullOrEmpty(m.Name))
                        mods.Add(m.Name);
                }
            }
            catch
            {
                // ignore during early load
            }

            return new PayloadExportMetadata(
                projectName,
                playerName,
                DateTime.UtcNow.ToString("O"),
                ModContent.GetInstance<global::TheArchitectsBlueprintMod.TheArchitectsBlueprintMod>().Version.ToString(),
                Main.versionNumber,
                mods.Count > 0 ? mods : null,
                thumbnailPng);
        }

        public static byte[]? TryEncodeThumbnailPng(Texture2D? texture)
        {
            if (texture is null)
                return null;

            try
            {
                using var stream = new MemoryStream();
                texture.SaveAsPng(stream, texture.Width, texture.Height);
                return stream.ToArray();
            }
            catch
            {
                return null;
            }
        }

        public static Texture2D? TryDecodeThumbnailPng(byte[]? png)
        {
            if (png is null or { Length: 0 })
                return null;

            try
            {
                return Texture2D.FromStream(Main.graphics.GraphicsDevice, new MemoryStream(png));
            }
            catch
            {
                return null;
            }
        }

        public static TagCompound BuildDocument(Payload payload, PayloadExportMetadata meta)
        {
            // --- Emit cells plus optional entities list for schema 5.

            ArgumentNullException.ThrowIfNull(payload);
            var cellEntries = new List<TagCompound>();
            if (payload.IsDefined)
            {
                IReadOnlyList<Payload.Entry> entries = payload.Entries;
                for (int i = 0; i < entries.Count; i++)
                {
                    Payload.Entry entry = entries[i];
                    Point rel = entry.Relative;
                    cellEntries.Add(new TagCompound
                    {
                        ["x"] = rel.X,
                        ["y"] = rel.Y,
                        [KeyCellBody] = CellBodyToTag(entry.Cell),
                    });
                }
            }

            var document = new TagCompound
            {
                [KeyFormat] = FormatId,
                [KeySchemaVersion] = SchemaVersion,
                [KeyMetadata] = MetadataToTag(meta, payload.Width, payload.Height),
                [KeyPayload] = cellEntries,
            };

            IReadOnlyList<PayloadEntity> entities = payload.Entities;
            if (entities.Count > 0)
            {
                var entityTags = new List<TagCompound>(entities.Count);
                for (int i = 0; i < entities.Count; i++)
                {
                    PayloadEntity entity = entities[i];
                    var tag = new TagCompound
                    {
                        ["x"] = entity.Relative.X,
                        ["y"] = entity.Relative.Y,
                        [KeyEntityName] = entity.FullName,
                    };
                    if (entity.HasData)
                        tag[KeyEntityData] = entity.Data!;
                    entityTags.Add(tag);
                }

                document[KeyEntities] = entityTags;
            }

            return document;
        }

        public static void WriteDocument(Stream stream, Payload payload, PayloadExportMetadata meta)
        {
            TagIO.ToStream(BuildDocument(payload, meta), stream, compress: true);
        }

        /// <summary>Writes a readable .tab.json mirror with compact nested modsLoaded, payload, and entities.</summary>
        public static void WriteJsonDocument(Stream stream, Payload payload, PayloadExportMetadata meta)
        {
            // --- Strip thumbnail, build JObject, then emit the hybrid compact JSON shape.

            PayloadExportMetadata stripped = new(
                meta.ProjectName,
                meta.PlayerName,
                meta.CreatedUtc,
                meta.ModVersion,
                meta.TerrariaVersion,
                meta.ModsLoaded,
                thumbnailPng: null);
            TagCompound document = BuildDocument(payload, stripped);
            JObject json = TagCompoundToJObject(document);
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 4096, leaveOpen: true);
            WriteHybridCompactJson(writer, json);
        }

        /// <summary>Writes an indented document shell with minified modsLoaded and one compact object per payload/entity line.</summary>
        private static void WriteHybridCompactJson(TextWriter writer, JObject root)
        {
            // --- Walk known root properties; compact only modsLoaded, payload, and entities.

            writer.WriteLine("{");
            bool first = true;

            // Emit each root property with a leading comma on the previous line when needed.
            void BeginProperty()
            {
                if (!first)
                    writer.WriteLine(",");
                first = false;
            }

            if (root.TryGetValue(KeyFormat, out JToken? format))
            {
                BeginProperty();
                writer.Write($"  \"{KeyFormat}\": ");
                WriteCompactToken(writer, format);
            }

            if (root.TryGetValue(KeySchemaVersion, out JToken? schema))
            {
                BeginProperty();
                writer.Write($"  \"{KeySchemaVersion}\": ");
                WriteCompactToken(writer, schema);
            }

            if (root.TryGetValue(KeyMetadata, out JToken? metaToken) && metaToken is JObject meta)
            {
                BeginProperty();
                writer.Write($"  \"{KeyMetadata}\": ");
                WriteHybridMetadata(writer, meta, indent: 2);
            }

            if (root.TryGetValue(KeyPayload, out JToken? payloadToken) && payloadToken is JArray payloadArray)
            {
                BeginProperty();
                writer.Write($"  \"{KeyPayload}\": ");
                WriteCompactObjectArray(writer, payloadArray, indent: 2);
            }

            if (root.TryGetValue(KeyEntities, out JToken? entitiesToken) && entitiesToken is JArray entitiesArray)
            {
                BeginProperty();
                writer.Write($"  \"{KeyEntities}\": ");
                WriteCompactObjectArray(writer, entitiesArray, indent: 2);
            }

            writer.WriteLine();
            writer.Write("}");
        }

        /// <summary>Writes metadata with indented properties and a single-line minified modsLoaded array.</summary>
        private static void WriteHybridMetadata(TextWriter writer, JObject meta, int indent)
        {
            // --- Indent each property line; minify modsLoaded (and other simple values) with Formatting.None.

            string pad = new string(' ', indent);
            string inner = new string(' ', indent + 2);
            writer.WriteLine("{");

            bool first = true;
            foreach (JProperty prop in meta.Properties())
            {
                if (!first)
                    writer.WriteLine(",");
                first = false;

                writer.Write(inner);
                writer.Write('"');
                writer.Write(prop.Name);
                writer.Write("\": ");
                WriteCompactToken(writer, prop.Value);
            }

            writer.WriteLine();
            writer.Write(pad);
            writer.Write('}');
        }

        /// <summary>Writes a JSON array of objects with one minified object per line.</summary>
        private static void WriteCompactObjectArray(TextWriter writer, JArray array, int indent)
        {
            // --- Open the array, stream each element as Formatting.None, then close at the parent indent.

            string pad = new string(' ', indent);
            string inner = new string(' ', indent + 2);
            writer.WriteLine("[");

            using (var jsonWriter = new JsonTextWriter(writer) { Formatting = Formatting.None, CloseOutput = false })
            {
                for (int i = 0; i < array.Count; i++)
                {
                    writer.Write(inner);
                    array[i].WriteTo(jsonWriter);
                    jsonWriter.Flush();
                    if (i < array.Count - 1)
                        writer.Write(',');
                    writer.WriteLine();
                }
            }

            writer.Write(pad);
            writer.Write(']');
        }

        /// <summary>Writes a JToken with no added whitespace.</summary>
        private static void WriteCompactToken(TextWriter writer, JToken token)
        {
            // --- Stream the token through a non-closing JsonTextWriter so values stay minified.

            using var jsonWriter = new JsonTextWriter(writer) { Formatting = Formatting.None, CloseOutput = false };
            token.WriteTo(jsonWriter);
            jsonWriter.Flush();
        }

        public static bool TryReadJsonDocument(string json, out Payload payload, out PayloadDocumentInfo info, out string? error)
        {
            payload = Payload.Empty;
            info = default;
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(json))
                {
                    error = "Empty document.";
                    return false;
                }

                if (JToken.Parse(json) is not JObject root)
                {
                    error = "Root must be a JSON object.";
                    return false;
                }

                if (!PayloadMetadata.IsMirrorJsonRoot(root))
                {
                    error = "Not a mirror payload JSON document.";
                    return false;
                }

                if (LooksLikeLegacySparsePayload(root))
                {
                    error = "Mirror payload list contains legacy tile snapshots.";
                    return false;
                }

                TagCompound tagRoot = JObjectToTagCompound(root);
                return TryReadDocument(tagRoot, out payload, out info, out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TryReadJsonDocumentInfo(string json, out PayloadDocumentInfo info, out string? error)
        {
            info = default;
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(json))
                {
                    error = "Empty document.";
                    return false;
                }

                if (JToken.Parse(json) is not JObject root)
                {
                    error = "Root must be a JSON object.";
                    return false;
                }

                if (!PayloadMetadata.IsMirrorJsonRoot(root))
                {
                    error = "Not a mirror payload JSON document.";
                    return false;
                }

                TagCompound tagRoot = JObjectToTagCompound(root);
                if (!TryValidateDocumentRoot(tagRoot, out _, out error))
                    return false;

                TagCompound metaTag = GetMetadataTag(tagRoot);
                return TryResolveDocumentInfo(tagRoot, metaTag, out info, out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TryReadDocument(Stream stream, out Payload payload, out PayloadDocumentInfo info, out string? error)
        {
            payload = Payload.Empty;
            info = default;
            error = null;
            try
            {
                TagCompound root = TagIO.FromStream(stream);
                return TryReadDocument(root, out payload, out info, out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TryReadDocument(TagCompound root, out Payload payload, out PayloadDocumentInfo info, out string? error)
        {
            payload = Payload.Empty;
            info = default;
            error = null;

            if (!TryValidateDocumentRoot(root, out int schema, out error))
                return false;

            TagCompound metaTag = GetMetadataTag(root);

            if (!TryResolveDocumentInfo(root, metaTag, out info, out error))
                return false;

            if (!TryGetPayloadListTag(root, out IList<TagCompound>? cellList) || cellList is null)
            {
                error = "Document missing payload.";
                return false;
            }

            var entries = new List<Payload.Entry>(cellList.Count);
            for (int i = 0; i < cellList.Count; i++)
            {
                TagCompound entryTag = cellList[i];
                int x = entryTag.GetInt("x");
                int y = entryTag.GetInt("y");
                if (!entryTag.ContainsKey(KeyCellBody) || entryTag[KeyCellBody] is not TagCompound body)
                    continue;

                // Tile bodies without style are invalid documents (style 0 must still be written).
                if (CellBodyRequiresStyle(body) && !body.ContainsKey("style"))
                {
                    error = $"Payload cell ({x},{y}): tile cells require style.";
                    return false;
                }

                if (!TryCellFromTag(body, out PayloadCell cell) || !cell.HasSparseContent)
                    continue;

                entries.Add(new Payload.Entry(new Point(x, y), cell));
            }

            payload = Payload.FromSparse(
                entries,
                info.Width,
                info.Height,
                ReadEntities(root, info.Width, info.Height, info.ModsLoaded));
            return payload.IsDefined;
        }

        /// <summary>Reads the optional schema-5 entities list; missing/empty yields no entities.</summary>
        private static PayloadEntity[] ReadEntities(
            TagCompound root,
            int width,
            int height,
            IReadOnlyList<string>? modsLoaded)
        {
            // --- Parse (x,y,n,d) tags and sanitize against document bounds using document modsLoaded.

            if (!root.ContainsKey(KeyEntities))
                return [];

            IList<TagCompound> list;
            try
            {
                list = root.GetList<TagCompound>(KeyEntities);
            }
            catch
            {
                return [];
            }

            if (list is null || list.Count == 0)
                return [];

            var entities = new List<PayloadEntity>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                TagCompound tag = list[i];
                int x = tag.GetInt("x");
                int y = tag.GetInt("y");
                if (!tag.ContainsKey(KeyEntityName))
                    continue;
                string name = tag.GetString(KeyEntityName);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                TagCompound? data = null;
                if (tag.ContainsKey(KeyEntityData) && tag[KeyEntityData] is TagCompound dataTag)
                    data = dataTag;

                entities.Add(new PayloadEntity(new Point(x, y), name, data));
            }

            return PayloadEntityCopy.Sanitize(entities, width, height, modsLoaded);
        }

        public static bool TryReadDocumentInfo(Stream stream, out PayloadDocumentInfo info, out string? error)
        {
            info = default;
            error = null;
            try
            {
                TagCompound root = TagIO.FromStream(stream);
                if (!TryValidateDocumentRoot(root, out _, out error))
                    return false;

                TagCompound metaTag = GetMetadataTag(root);
                return TryResolveDocumentInfo(root, metaTag, out info, out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryValidateDocumentRoot(TagCompound root, out int schema, out string? error)
        {
            schema = 0;
            error = null;

            if (!root.ContainsKey(KeyFormat) || root.GetString(KeyFormat) != FormatId)
            {
                error = "Unrecognized payload format.";
                return false;
            }

            schema = root.GetInt(KeySchemaVersion);
            if (schema < MinSchemaVersion || schema > SchemaVersion)
            {
                error = $"Unsupported schema version {schema} (supported {MinSchemaVersion}–{SchemaVersion}).";
                return false;
            }

            return true;
        }

        private static TagCompound GetMetadataTag(TagCompound root) =>
            root.ContainsKey(KeyMetadata) && root[KeyMetadata] is TagCompound meta ? meta : [];

        private static bool TryResolveDocumentInfo(TagCompound root, TagCompound metaTag, out PayloadDocumentInfo info, out string? error)
        {
            info = ReadDocumentInfo(metaTag);
            error = null;

            int width = info.Width;
            int height = info.Height;
            if (width <= 0 || height <= 0)
            {
                if (!TryInferDimensionsFromPayload(root, out width, out height))
                {
                    error = "Could not resolve payload dimensions from metadata or payload list.";
                    return false;
                }
            }

            info = new PayloadDocumentInfo(
                info.ProjectName,
                info.PlayerName,
                info.CreatedUtc,
                width,
                height,
                info.ModsLoaded,
                info.ThumbnailPng,
                info.ModVersion,
                info.TerrariaVersion);
            return true;
        }

        private static bool TryGetPayloadListTag(TagCompound root, out IList<TagCompound>? cellList)
        {
            if (root.ContainsKey(KeyPayload))
            {
                cellList = root.GetList<TagCompound>(KeyPayload);
                return cellList is not null;
            }

            if (root.ContainsKey(KeyLegacyCells))
            {
                cellList = root.GetList<TagCompound>(KeyLegacyCells);
                return cellList is not null;
            }

            cellList = null;
            return false;
        }

        private static bool TryInferDimensionsFromPayload(TagCompound root, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (!TryGetPayloadListTag(root, out IList<TagCompound>? cellList) || cellList is null or { Count: 0 })
                return false;

            int maxX = 0;
            int maxY = 0;
            for (int i = 0; i < cellList.Count; i++)
            {
                TagCompound entryTag = cellList[i];
                if (!entryTag.ContainsKey("x") || !entryTag.ContainsKey("y"))
                    continue;

                if (!entryTag.ContainsKey(KeyCellBody) || entryTag[KeyCellBody] is not TagCompound body || body.Count == 0)
                    continue;

                maxX = Math.Max(maxX, entryTag.GetInt("x"));
                maxY = Math.Max(maxY, entryTag.GetInt("y"));
            }

            width = maxX + 1;
            height = maxY + 1;
            return width > 0 && height > 0;
        }

        internal static bool CellHasBodyContent(PayloadCell cell)
        {
            if (cell.IsEmpty)
                return false;

            if (cell.Detail.HasTile)
                return true;
            if (cell.Wall.Type > 0)
                return true;
            if (cell.Liquid.Type != 0)
                return true;
            if (cell.Liquid.Amount > 0)
                return true;
            if (cell.Block.FrameX != 0 || cell.Block.FrameY != 0)
                return true;
            if (cell.Wall.FrameX != 0 || cell.Wall.FrameY != 0)
                return true;

            PayloadCell.TileData data = cell.Data!;
            if (data.TileStyle != 0 || data.TileAlternate != 0)
                return true;
            if (data.TWWStateData.Slope != SlopeType.Solid)
                return true;
            if (data.TWWStateData.BlockType != BlockType.Solid)
                return true;
            if (cell.Paint.TileColor != 0 || cell.Paint.WallColor != 0)
                return true;
            if (cell.Wire.Data != 0)
                return true;
            return PackFlags(cell) != 0;
        }

        /// <summary>Returns true when a cell body is a placed tile and must include placeStyle.</summary>
        private static bool CellBodyRequiresStyle(TagCompound body)
        {
            // --- Tile FullName or HasTile flag both imply placeStyle is required (including 0).

            if (body.ContainsKey("tile"))
                return true;

            if (body.ContainsKey("flags")
                && TryGetInt32(body, "flags", out int flags)
                && (flags & (int)CellFlags.HasTile) != 0)
            {
                return true;
            }

            return false;
        }

        public static TagCompound CellBodyToTag(PayloadCell cell)
        {
            if (!CellHasBodyContent(cell))
                throw new InvalidOperationException("Cannot serialize vacant cell.");

            var data = cell.Data!;
            var tag = new TagCompound();
            ushort tileType = cell.Block.Type;
            ushort wallType = cell.Wall.Type;

            if (cell.Detail.HasTile)
                tag["tile"] = ContentNameResolver.GetTileFullName(tileType);

            if (wallType > 0)
                tag["wall"] = ContentNameResolver.GetWallFullName(wallType);

            if (cell.Liquid.Type != 0)
                tag["liquid"] = ContentNameResolver.GetLiquidTypeName(cell.Liquid.Type);

            if (cell.Liquid.Amount > 0)
                tag["lAmt"] = cell.Liquid.Amount;

            if (cell.Block.FrameX != 0)
                tag["tFX"] = cell.Block.FrameX;
            if (cell.Block.FrameY != 0)
                tag["tFY"] = cell.Block.FrameY;

            if (cell.Wall.FrameX != 0)
                tag["wFX"] = cell.Wall.FrameX;
            if (cell.Wall.FrameY != 0)
                tag["wFY"] = cell.Wall.FrameY;

            // Tile cells always carry placeStyle (including 0); imports reject missing style.
            if (cell.Detail.HasTile)
                tag["style"] = data.TileStyle;
            if (data.TileAlternate != 0)
                tag["alt"] = data.TileAlternate;

            if (data.TWWStateData.Slope != SlopeType.Solid)
                tag["slope"] = (byte)data.TWWStateData.Slope;
            if (data.TWWStateData.BlockType != BlockType.Solid)
                tag["bType"] = (byte)data.TWWStateData.BlockType;

            if (cell.Paint.TileColor != 0)
                tag["paintT"] = cell.Paint.TileColor;
            if (cell.Paint.WallColor != 0)
                tag["paintW"] = cell.Paint.WallColor;

            if (cell.Wire.Data != 0)
                tag["wire"] = cell.Wire.Data;

            int flags = PackFlags(cell);
            if (flags != 0)
                tag["flags"] = flags;

            return tag;
        }

        internal static bool TryCellFromJObject(JObject body, out PayloadCell cell)
        {
            cell = default;
            if (body.Count == 0)
                return false;

            TagCompound tag = JObjectToTagCompound(body);
            return TryCellFromTag(tag, out cell);
        }

        public static bool TryCellFromTag(TagCompound tag, out PayloadCell cell)
        {
            cell = default;
            if (tag.Count == 0)
                return false;

            try
            {
                var data = new PayloadCell.TileData
                {
                    TileType = default,
                    WallType = default,
                    LiquidData = default,
                    TWBIData = default,
                    TWWStateData = default,
                };

                if (tag.ContainsKey("tile"))
                {
                    string? tileName = tag.GetString("tile");
                    int? resolved = ContentNameResolver.ResolveTileFullName(tileName);
                    // Unresolved FullNames become UnloadedSolidTile so UnknownLayer can still own them.
                    data.TileType.Type = resolved.HasValue
                        ? (ushort)resolved.Value
                        : TileLoaderSupport.ResolveUnloadableTileType(0);
                    data.TWWStateData.HasTile = true;
                }

                if (tag.ContainsKey("wall"))
                {
                    string? wallName = tag.GetString("wall");
                    int? resolved = ContentNameResolver.ResolveWallFullName(wallName);
                    // Unresolved wall FullNames become UnloadedWall rather than dropping the cell.
                    data.WallType.Type = resolved.HasValue
                        ? (ushort)resolved.Value
                        : TileLoaderSupport.ResolveUnloadableWallType(1);
                }

                if (tag.ContainsKey("liquid"))
                {
                    // Named liquids must resolve; do not silently fall back to water.
                    string? liquidName = tag.GetString("liquid");
                    int? resolved = ContentNameResolver.ResolveLiquidType(liquidName);
                    if (!resolved.HasValue)
                        return false;
                    data.LiquidData.LiquidType = resolved.Value;
                }

                if (tag.ContainsKey("lAmt") && TryGetByte(tag, "lAmt", out byte lAmt))
                    data.LiquidData.Amount = lAmt;

                if (tag.ContainsKey("tFX") && TryGetInt16(tag, "tFX", out short tFx))
                    data.TWWStateData.TileFrameX = tFx;
                if (tag.ContainsKey("tFY") && TryGetInt16(tag, "tFY", out short tFy))
                    data.TWWStateData.TileFrameY = tFy;
                if (tag.ContainsKey("tFN") && TryGetInt32(tag, "tFN", out int tFn))
                    data.TWWStateData.TileFrameNumber = tFn;

                if (tag.ContainsKey("wFX") && TryGetInt32(tag, "wFX", out int wFx))
                    data.TWWStateData.WallFrameX = wFx;
                if (tag.ContainsKey("wFY") && TryGetInt32(tag, "wFY", out int wFy))
                    data.TWWStateData.WallFrameY = wFy;
                if (tag.ContainsKey("wFN") && TryGetInt32(tag, "wFN", out int wFn))
                    data.TWWStateData.WallFrameNumber = wFn;

                data.TWWStateData.TileFrameNumber = 0;
                data.TWWStateData.WallFrameNumber = 0;

                // HasTile cells require an explicit style key (0 is valid; omission is not).
                if (CellBodyRequiresStyle(tag))
                {
                    if (!tag.ContainsKey("style") || !TryGetInt32(tag, "style", out int style) || style < 0)
                        return false;
                    data.TileStyle = style;
                }
                else if (tag.ContainsKey("style") && TryGetInt32(tag, "style", out int styleOnly))
                {
                    data.TileStyle = styleOnly;
                }

                if (tag.ContainsKey("alt") && TryGetInt32(tag, "alt", out int alt))
                    data.TileAlternate = alt;

                if (tag.ContainsKey("slope") && TryGetByte(tag, "slope", out byte slope))
                    data.TWWStateData.Slope = (SlopeType)slope;
                if (tag.ContainsKey("bType") && TryGetByte(tag, "bType", out byte bType))
                    data.TWWStateData.BlockType = (BlockType)bType;

                if (tag.ContainsKey("paintT") && TryGetByte(tag, "paintT", out byte paintT))
                    data.TWWStateData.TileColor = paintT;
                if (tag.ContainsKey("paintW") && TryGetByte(tag, "paintW", out byte paintW))
                    data.TWWStateData.WallColor = paintW;

                if (tag.ContainsKey("wire") && TryGetInt32(tag, "wire", out int wire))
                    data.TWWStateData.WireData = wire;

                if (tag.ContainsKey("flags") && TryGetInt32(tag, "flags", out int flags))
                    UnpackFlags(flags, ref data);

                cell = PayloadCell.FromData(data);
                return cell.HasSparseContent;
            }
            catch
            {
                return false;
            }
        }

        public static byte[] CellTagToBytes(TagCompound cellBody)
        {
            using var ms = new MemoryStream();
            TagIO.ToStream(cellBody, ms, compress: true);
            return ms.ToArray();
        }

        public static bool TryCellFromBytes(byte[] bytes, out PayloadCell cell)
        {
            cell = default;
            try
            {
                using var ms = new MemoryStream(bytes);
                TagCompound tag = TagIO.FromStream(ms);
                return TryCellFromTag(tag, out cell);
            }
            catch
            {
                return false;
            }
        }

        private static TagCompound MetadataToTag(PayloadExportMetadata meta, int width, int height)
        {
            var tag = new TagCompound
            {
                ["format"] = FormatId,
                ["schemaVersion"] = SchemaVersion,
                ["projectName"] = meta.ProjectName,
                ["playerName"] = meta.PlayerName,
                ["createdUtc"] = meta.CreatedUtc,
                ["modVersion"] = meta.ModVersion,
                ["width"] = width,
                ["height"] = height,
            };

            if (!string.IsNullOrEmpty(meta.TerrariaVersion))
                tag["terrariaVersion"] = meta.TerrariaVersion;

            if (meta.ModsLoaded is { Count: > 0 })
                tag[KeyModsLoaded] = meta.ModsLoaded;

            if (meta.ThumbnailPng is { Length: > 0 })
                tag[KeyThumbnailPng] = meta.ThumbnailPng;

            return tag;
        }

        private static PayloadDocumentInfo ReadDocumentInfo(TagCompound metaTag)
        {
            string? projectName = metaTag.ContainsKey("projectName") ? metaTag.GetString("projectName") : null;
            string? playerName = metaTag.ContainsKey("playerName") ? metaTag.GetString("playerName") : null;
            string? createdUtc = metaTag.ContainsKey("createdUtc") ? metaTag.GetString("createdUtc") : null;
            int width = metaTag.ContainsKey("width") ? metaTag.GetInt("width") : 0;
            int height = metaTag.ContainsKey("height") ? metaTag.GetInt("height") : 0;

            IReadOnlyList<string>? modsLoaded = null;
            if (metaTag.ContainsKey(KeyModsLoaded))
            {
                IList<string> mods = metaTag.GetList<string>(KeyModsLoaded);
                if (mods is { Count: > 0 })
                    modsLoaded = new List<string>(mods);
            }

            byte[]? thumbnailPng = null;
            if (metaTag.ContainsKey(KeyThumbnailPng))
            {
                try
                {
                    thumbnailPng = metaTag.GetByteArray(KeyThumbnailPng);
                }
                catch
                {
                    thumbnailPng = null;
                }
            }

            string? modVersion = metaTag.ContainsKey("modVersion") ? metaTag.GetString("modVersion") : null;
            string? terrariaVersion = metaTag.ContainsKey("terrariaVersion") ? metaTag.GetString("terrariaVersion") : null;

            return new PayloadDocumentInfo(
                projectName,
                playerName,
                createdUtc,
                width,
                height,
                modsLoaded,
                thumbnailPng,
                modVersion,
                terrariaVersion);
        }

        private static int PackFlags(PayloadCell cell)
        {
            int flags = 0;
            if (cell.Detail.HasTile) flags |= (int)CellFlags.HasTile;
            if (cell.Detail.IsActuated) flags |= (int)CellFlags.IsActuated;
            if (cell.Detail.IsHalfBlock) flags |= (int)CellFlags.IsHalfBlock;
            if (cell.Paint.TileInvisible) flags |= (int)CellFlags.TileInvisible;
            if (cell.Paint.WallInvisible) flags |= (int)CellFlags.WallInvisible;
            if (cell.Paint.TileFullbright) flags |= (int)CellFlags.TileFullbright;
            if (cell.Paint.WallFullbright) flags |= (int)CellFlags.WallFullbright;
            if (cell.Liquid.SkipLiquid) flags |= (int)CellFlags.SkipLiquid;
            if (cell.Liquid.CheckingLiquid) flags |= (int)CellFlags.CheckingLiquid;
            if (cell.Wire.HasActuator) flags |= (int)CellFlags.HasActuator;
            if (cell.Wire.RedWire) flags |= (int)CellFlags.RedWire;
            if (cell.Wire.BlueWire) flags |= (int)CellFlags.BlueWire;
            if (cell.Wire.GreenWire) flags |= (int)CellFlags.GreenWire;
            if (cell.Wire.YellowWire) flags |= (int)CellFlags.YellowWire;
            return flags;
        }

        private static void UnpackFlags(int flags, ref PayloadCell.TileData data)
        {
            if ((flags & (int)CellFlags.HasTile) != 0)
                data.TWWStateData.HasTile = true;
            if ((flags & (int)CellFlags.IsActuated) != 0)
                data.TWWStateData.IsActuated = true;
            if ((flags & (int)CellFlags.IsHalfBlock) != 0)
                data.TWWStateData.IsHalfBlock = true;
            if ((flags & (int)CellFlags.TileInvisible) != 0)
                data.TWBIData.IsTileInvisible = true;
            if ((flags & (int)CellFlags.WallInvisible) != 0)
                data.TWBIData.IsWallInvisible = true;
            if ((flags & (int)CellFlags.TileFullbright) != 0)
                data.TWBIData.IsTileFullbright = true;
            if ((flags & (int)CellFlags.WallFullbright) != 0)
                data.TWBIData.IsWallFullbright = true;
            if ((flags & (int)CellFlags.SkipLiquid) != 0)
                data.LiquidData.SkipLiquid = true;
            if ((flags & (int)CellFlags.CheckingLiquid) != 0)
                data.LiquidData.CheckingLiquid = true;
            if ((flags & (int)CellFlags.HasActuator) != 0)
                data.TWWStateData.HasActuator = true;
            if ((flags & (int)CellFlags.RedWire) != 0)
                data.TWWStateData.RedWire = true;
            if ((flags & (int)CellFlags.BlueWire) != 0)
                data.TWWStateData.BlueWire = true;
            if ((flags & (int)CellFlags.GreenWire) != 0)
                data.TWWStateData.GreenWire = true;
            if ((flags & (int)CellFlags.YellowWire) != 0)
                data.TWWStateData.YellowWire = true;
        }

        private static bool LooksLikeLegacySparsePayload(JObject root)
        {
            JToken? listToken = root[KeyPayload] ?? root[KeyLegacyCells];
            if (listToken is not JArray { Count: > 0 } list)
                return false;

            if (list[0] is JArray)
                return true;

            if (list[0] is not JObject first)
                return false;

            return first.ContainsKey("TileType") || first.ContainsKey("WallType");
        }

        private static JObject TagCompoundToJObject(TagCompound tag) =>
            (JObject)NbtTagValues.ToJson(tag);

        private static TagCompound JObjectToTagCompound(JObject obj) =>
            NbtTagValues.FromJson(obj) as TagCompound ?? [];

        private static bool TryGetInt32(TagCompound tag, string key, out int value)
        {
            value = 0;
            if (!tag.ContainsKey(key))
                return false;

            object raw = tag[key];
            switch (raw)
            {
                case int i:
                    value = i;
                    return true;
                case short s:
                    value = s;
                    return true;
                case byte b:
                    value = b;
                    return true;
                case long l:
                    value = (int)l;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetInt16(TagCompound tag, string key, out short value)
        {
            value = 0;
            if (!TryGetInt32(tag, key, out int i))
                return false;

            value = (short)i;
            return true;
        }

        private static bool TryGetByte(TagCompound tag, string key, out byte value)
        {
            value = 0;
            if (!TryGetInt32(tag, key, out int i))
                return false;

            value = (byte)i;
            return true;
        }
    }
}
