using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core.TileMapping;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Orchestrates tile mapping compilation across layer sub-systems.
    /// Layers: Wall, Tile, Liquid, Wire, Paint.
    /// Centralizes path resolution for mod output (SourceMaps, debug logs). CompiledMaps are managed by ITRStorage.
    /// </summary>
    public sealed class TileMappingSystem : ModSystem
    {
        private const string ModDirName = "TheArchitectsBlueprintMod";
        private const string SourceMapsDir = "SourceMaps";

        /// <summary>
        /// Gets the mod's output directory. Creates it if missing.
        /// </summary>
        public static string GetModDirectory()
        {
            var dir = Path.Combine(Main.SavePath, ModDirName);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Gets full path for a file in TheArchitectsBlueprintMod root (e.g. debug logs).
        /// </summary>
        public static string GetPath(string fileName) => Path.Combine(GetModDirectory(), fileName);

        /// <summary>
        /// Gets the SourceMaps directory. Creates it if missing. Discovery output and editable config live here.
        /// </summary>
        public static string GetSourceMapsDirectory()
        {
            var dir = Path.Combine(GetModDirectory(), SourceMapsDir);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Gets full path for a file in TheArchitectsBlueprintMod\SourceMaps.
        /// </summary>
        public static string GetSourceMapPath(string fileName)
        {
            return Path.Combine(GetSourceMapsDirectory(), fileName);
        }

        /// <summary>
        /// Reads only format and mapType from the metadata without parsing the full document.
        /// Returns (format, mapType) or (null, null) if metadata is missing or invalid.
        /// </summary>
        public static (string? Format, string? MapType) ReadMetadataOnly(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });

                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                    return (null, null);

                string? format = null;
                string? mapType = null;

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject) break;
                    if (reader.TokenType != JsonTokenType.PropertyName) continue;

                    var propName = reader.GetString();
                    reader.Read();

                    if (string.Equals(propName, "metadata", StringComparison.Ordinal) && reader.TokenType == JsonTokenType.StartObject)
                    {
                        while (reader.Read())
                        {
                            if (reader.TokenType == JsonTokenType.EndObject) break;
                            if (reader.TokenType != JsonTokenType.PropertyName) continue;

                            var name = reader.GetString();
                            reader.Read();

                            if (string.Equals(name, "format", StringComparison.Ordinal))
                                format = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                            else if (string.Equals(name, "mapType", StringComparison.Ordinal))
                                mapType = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                            else
                                reader.Skip();
                        }
                        return (format, mapType);
                    }

                    reader.Skip();
                }

                return (null, null);
            }
            catch
            {
                return (null, null);
            }
        }

        /// <summary>
        /// Finds the first .itr file in the directory matching the given metadata. Returns null if none found.
        /// Uses lightweight metadata-only parsing to avoid full document load.
        /// </summary>
        public static string? FindFileByMetadata(string directory, string? format, string? mapType)
        {
            if (!Directory.Exists(directory)) return null;
            foreach (var path in Directory.EnumerateFiles(directory, "*.itr"))
            {
                var (fileFormat, fileMapType) = ReadMetadataOnly(path);
                if (MatchesMetadata(fileFormat, fileMapType, format, mapType))
                    return path;
            }
            return null;
        }

        private static bool MatchesMetadata(string? fileFormat, string? fileMapType, string? format, string? mapType)
        {
            if (format != null && !string.Equals(fileFormat, format, StringComparison.OrdinalIgnoreCase))
                return false;
            if (mapType != null && (fileMapType == null || !string.Equals(fileMapType, mapType, StringComparison.OrdinalIgnoreCase)))
                return false;
            return true;
        }

        /// <summary>
        /// Compiles all layer maps and writes them to JSON.
        /// Loads SourceMaps once, merges by layer, writes CompiledMaps.
        /// All messages are written to TileMapCompile_Debug.txt in addition to the msg callback.
        /// </summary>
        public static void Compile(Mod mod, Action<string> msg)
        {
            if (!TileMappingAuthority.UsesLocalMappingFilesystem())
            {
                msg("Tile map compilation runs only in single-player or on the server / listen host (not on remote multiplayer clients).");
                return;
            }

            var debugPath = GetPath("TileMapCompile_Debug.txt");
            using var debugWriter = new StreamWriter(debugPath, append: false);
            void Log(string s)
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {s}";
                debugWriter.WriteLine(line);
                debugWriter.Flush();
                msg(s);
            }

            Log("Tile map compilation started.");
            ItrCompiler.Compile(mod, Log);
            Log("Tile map compilation complete.");
        }
    }
}
