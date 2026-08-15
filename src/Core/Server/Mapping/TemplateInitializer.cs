using System;
using System.IO;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Debug;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Copies template files from Content/Templates to the mod output directories at launch.
    /// Skips files that already exist at the destination.
    /// </summary>
    internal sealed class TemplateInitializer : ModSystem
    {
        private const string TemplatesPrefix = "Content/Templates/";
        private const string SourceMapsTemplateDir = "SourceMaps/";
        private const string CompiledMapsTemplateDir = "CompiledMaps/";

        public override void Load()
        {
            // Remote MP clients: do not seed local SourceMaps / CompiledMaps; server is authoritative.
            if (!TileMappingAuthority.UsesLocalMappingFilesystem())
                return;

            MigrateSourceMapExtensions();
            CopyTemplates(Mod);
        }

        /// <summary>Renames leftover SourceMaps <c>*.itr</c> files to <c>*.itr.json</c>.</summary>
        private static void MigrateSourceMapExtensions()
        {
            // --- Append .json so Conversion.itr becomes Conversion.itr.json without overwriting.

            string directory = TileMappingSystem.GetSourceMapsDirectory();
            if (!Directory.Exists(directory))
                return;

            foreach (string path in Directory.EnumerateFiles(directory, "*.itr"))
            {
                if (path.EndsWith(TileMappingSystem.SourceMapExtension, StringComparison.OrdinalIgnoreCase))
                    continue;

                string destPath = path + ".json";
                if (File.Exists(destPath))
                    continue;

                try
                {
                    File.Move(path, destPath);
                }
                catch (Exception ex)
                {
                    TABLog.Warn($"Failed to rename {path} to {destPath}: {ex.Message}", "Template");
                }
            }
        }

        private static void CopyTemplates(Mod mod)
        {
            var fileNames = mod.GetFileNames();
            if (fileNames is null) return;

            foreach (var name in fileNames)
            {
                if (string.IsNullOrEmpty(name) || !name.StartsWith(TemplatesPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var relative = name[TemplatesPrefix.Length..];
                string destPath;

                if (relative.StartsWith(SourceMapsTemplateDir, StringComparison.OrdinalIgnoreCase))
                {
                    var fileName = relative[SourceMapsTemplateDir.Length..];
                    if (string.IsNullOrEmpty(fileName)) continue;
                    destPath = TileMappingSystem.GetSourceMapPath(fileName);
                }
                else if (relative.StartsWith(CompiledMapsTemplateDir, StringComparison.OrdinalIgnoreCase))
                {
                    var fileName = relative[CompiledMapsTemplateDir.Length..];
                    if (string.IsNullOrEmpty(fileName)) continue;
                    destPath = ItrStorage.GetCompiledMapPath(fileName);
                }
                else
                {
                    continue;
                }

                if (File.Exists(destPath)) continue;

                try
                {
                    var bytes = mod.GetFileBytes(name);
                    if (bytes is null || bytes.Length == 0) continue;

                    var dir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    File.WriteAllBytes(destPath, bytes);
                }
                catch (Exception ex)
                {
                    TABLog.Warn($"Failed to copy template {name}: {ex.Message}", "Template");
                }
            }
        }
    }
}
