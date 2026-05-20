using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Sparse payload documents via <see cref="TagIO"/> (schema 3–4).</summary>
    internal static class PayloadTagIO
    {
        public const string FormatId = "TheArchitectsBlueprintMod.Payload";
        public const int SchemaVersion = 4;
        public const int MinSchemaVersion = 3;

        private const string KeyFormat = "format";
        private const string KeySchemaVersion = "schemaVersion";
        private const string KeyMetadata = "metadata";
        private const string KeyCells = "cells";
        private const string KeyCellBody = "c";
        private const string KeyThumbnailPng = "thumbnailPng";

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
            ArgumentNullException.ThrowIfNull(payload);
            var cellEntries = new List<TagCompound>();
            if (payload.IsDefined)
            {
                for (int y = 0; y < payload.Height; y++)
                {
                    for (int x = 0; x < payload.Width; x++)
                    {
                        if (!payload.TryGetCell(new Point(x, y), out PayloadCell cell) || cell.IsEmpty)
                            continue;

                        cellEntries.Add(new TagCompound
                        {
                            ["x"] = x,
                            ["y"] = y,
                            [KeyCellBody] = CellBodyToTag(cell),
                        });
                    }
                }
            }

            return new TagCompound
            {
                [KeyFormat] = FormatId,
                [KeySchemaVersion] = SchemaVersion,
                [KeyMetadata] = MetadataToTag(meta, payload.Width, payload.Height),
                [KeyCells] = cellEntries,
            };
        }

        public static void WriteDocument(Stream stream, Payload payload, PayloadExportMetadata meta)
        {
            TagIO.ToStream(BuildDocument(payload, meta), stream, compress: true);
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

            if (!root.ContainsKey(KeyCells))
            {
                error = "Document missing cells.";
                return false;
            }

            IList<TagCompound> cellList = root.GetList<TagCompound>(KeyCells);
            if (cellList is null)
            {
                error = "Document missing cells.";
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

                if (!TryCellFromTag(body, out PayloadCell cell) || cell.IsEmpty)
                    continue;

                entries.Add(new Payload.Entry(new Point(x, y), cell));
            }

            payload = Payload.FromSparse(entries, info.Width, info.Height);
            return payload.IsDefined;
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
            root.ContainsKey(KeyMetadata) && root[KeyMetadata] is TagCompound meta ? meta : new TagCompound();

        private static bool TryResolveDocumentInfo(TagCompound root, TagCompound metaTag, out PayloadDocumentInfo info, out string? error)
        {
            info = ReadDocumentInfo(metaTag);
            error = null;

            int width = info.Width;
            int height = info.Height;
            if (width <= 0 || height <= 0)
            {
                if (!TryInferDimensionsFromCells(root, out width, out height))
                {
                    error = "Could not resolve payload dimensions from metadata or cells.";
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
                info.ThumbnailPng);
            return true;
        }

        private static bool TryInferDimensionsFromCells(TagCompound root, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (!root.ContainsKey(KeyCells))
                return false;

            IList<TagCompound>? cellList = root.GetList<TagCompound>(KeyCells);
            if (cellList is null or { Count: 0 })
                return false;

            int maxX = 0;
            int maxY = 0;
            for (int i = 0; i < cellList.Count; i++)
            {
                TagCompound entryTag = cellList[i];
                if (!entryTag.ContainsKey("x") || !entryTag.ContainsKey("y"))
                    continue;

                maxX = Math.Max(maxX, entryTag.GetInt("x"));
                maxY = Math.Max(maxY, entryTag.GetInt("y"));
            }

            width = maxX + 1;
            height = maxY + 1;
            return width > 0 && height > 0;
        }

        public static TagCompound CellBodyToTag(PayloadCell cell)
        {
            if (cell.IsEmpty)
                throw new InvalidOperationException("Cannot serialize empty cell.");

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
            if (cell.Block.FrameNumber != 0)
                tag["tFN"] = cell.Block.FrameNumber;

            if (cell.Wall.FrameX != 0)
                tag["wFX"] = cell.Wall.FrameX;
            if (cell.Wall.FrameY != 0)
                tag["wFY"] = cell.Wall.FrameY;
            if (cell.Wall.FrameNumber != 0)
                tag["wFN"] = cell.Wall.FrameNumber;

            if (data.TileStyle != 0)
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

        public static bool TryCellFromTag(TagCompound tag, out PayloadCell cell)
        {
            cell = default;
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
                    if (!resolved.HasValue)
                        return false;
                    data.TileType.Type = (ushort)resolved.Value;
                    data.TWWStateData.HasTile = true;
                }

                if (tag.ContainsKey("wall"))
                {
                    string? wallName = tag.GetString("wall");
                    int? resolved = ContentNameResolver.ResolveWallFullName(wallName);
                    if (!resolved.HasValue)
                        return false;
                    data.WallType.Type = (ushort)resolved.Value;
                }

                if (tag.ContainsKey("liquid"))
                {
                    string? liquidName = tag.GetString("liquid");
                    int? resolved = ContentNameResolver.ResolveLiquidType(liquidName);
                    if (resolved.HasValue)
                        data.LiquidData.LiquidType = resolved.Value;
                }

                if (tag.ContainsKey("lAmt"))
                    data.LiquidData.Amount = tag.GetByte("lAmt");

                if (tag.ContainsKey("tFX"))
                    data.TWWStateData.TileFrameX = tag.GetShort("tFX");
                if (tag.ContainsKey("tFY"))
                    data.TWWStateData.TileFrameY = tag.GetShort("tFY");
                if (tag.ContainsKey("tFN"))
                    data.TWWStateData.TileFrameNumber = tag.GetInt("tFN");

                if (tag.ContainsKey("wFX"))
                    data.TWWStateData.WallFrameX = tag.GetInt("wFX");
                if (tag.ContainsKey("wFY"))
                    data.TWWStateData.WallFrameY = tag.GetInt("wFY");
                if (tag.ContainsKey("wFN"))
                    data.TWWStateData.WallFrameNumber = tag.GetInt("wFN");

                if (tag.ContainsKey("style"))
                    data.TileStyle = tag.GetInt("style");
                if (tag.ContainsKey("alt"))
                    data.TileAlternate = tag.GetInt("alt");

                if (tag.ContainsKey("slope"))
                    data.TWWStateData.Slope = (SlopeType)tag.GetByte("slope");
                if (tag.ContainsKey("bType"))
                    data.TWWStateData.BlockType = (BlockType)tag.GetByte("bType");

                if (tag.ContainsKey("paintT"))
                    data.TWWStateData.TileColor = tag.GetByte("paintT");
                if (tag.ContainsKey("paintW"))
                    data.TWWStateData.WallColor = tag.GetByte("paintW");

                if (tag.ContainsKey("wire"))
                    data.TWWStateData.WireData = tag.GetInt("wire");

                if (tag.ContainsKey("flags"))
                    UnpackFlags(tag.GetInt("flags"), ref data);

                cell = PayloadCell.FromData(data);
                return !cell.IsEmpty;
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
                tag["modsLoaded"] = meta.ModsLoaded;

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
            if (metaTag.ContainsKey("modsLoaded"))
            {
                IList<string> mods = metaTag.GetList<string>("modsLoaded");
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

            return new PayloadDocumentInfo(projectName, playerName, createdUtc, width, height, modsLoaded, thumbnailPng);
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
    }
}
