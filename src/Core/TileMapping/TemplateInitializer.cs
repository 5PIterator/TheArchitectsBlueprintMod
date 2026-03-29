using System;
using System.IO;
using ArchitectsStencilMod.Core;
using ArchitectsStencilMod.Debug;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core.TileMapping
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
            CopyTemplates(Mod);
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
                    ASMLog.Warn($"Failed to copy template {name}: {ex.Message}", "Template");
                }
            }
        }
    }
}
