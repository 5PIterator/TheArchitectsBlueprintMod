#if DEBUG
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Tests.Samples
{
    internal static class PayloadSampleCatalog
    {
        private static PayloadSample[]? _cache;

        internal static IReadOnlyList<PayloadSample> All => _cache ??= Discover();

        internal static bool TryGetBySlug(string slug, out PayloadSample? sample)
        {
            sample = All.FirstOrDefault(s => string.Equals(s.Slug, slug, StringComparison.OrdinalIgnoreCase));
            return sample is not null;
        }

        internal static bool TryLoad(PayloadSample sample, out Payload payload, out string? error)
        {
            payload = default!;
            if (!TryLoadDocument(sample, out string text, out error))
                return false;

            return PayloadSerialization.TryParsePayloadDocument(text, out payload, out error);
        }

        internal static string SlugFromFileName(string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName);
            return name.ToLowerInvariant();
        }

        private static PayloadSample[] Discover()
        {
            var byFile = new Dictionary<string, PayloadSample>(StringComparer.OrdinalIgnoreCase);

            RegisterEmbeddedSamples(byFile);
            RegisterDiskSamples(byFile);

            if (byFile.Count == 0)
            {
                throw new InvalidOperationException(
                    "No payload samples found. Add *.tab.json under tests/Samples and embed them in the csproj.");
            }

            return byFile.Values.OrderBy(s => s.FileName, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static void RegisterEmbeddedSamples(Dictionary<string, PayloadSample> byFile)
        {
            foreach (string resourceName in typeof(PayloadSampleCatalog).Assembly.GetManifestResourceNames())
            {
                if (!IsEmbeddedSampleResource(resourceName))
                    continue;

                string fileName = ResourceNameToFileName(resourceName);
                if (!TryParseSampleDocument(fileName, null, resourceName, out _))
                    continue;

                byFile[fileName] = new PayloadSample(fileName, SlugFromFileName(fileName), resourceName);
            }
        }

        private static void RegisterDiskSamples(Dictionary<string, PayloadSample> byFile)
        {
            string? samplesDir = TryFindSamplesDirectory();
            if (samplesDir is null)
                return;

            foreach (string path in Directory.EnumerateFiles(samplesDir, "*.tab.json"))
            {
                string fileName = Path.GetFileName(path);
                if (!TryParseSampleDocument(fileName, path, null, out _))
                    continue;

                byFile[fileName] = new PayloadSample(fileName, SlugFromFileName(fileName), null);
            }
        }

        private static bool IsEmbeddedSampleResource(string resourceName) =>
            resourceName.EndsWith(".tab.json", StringComparison.OrdinalIgnoreCase)
            && resourceName.Contains(".tests.Samples.", StringComparison.OrdinalIgnoreCase);

        private static bool TryParseSampleDocument(
            string fileName,
            string? diskPath,
            string? embeddedResourceName,
            out string text)
        {
            if (!TryLoadDocumentText(fileName, diskPath, embeddedResourceName, out text, out _))
                return false;

            return PayloadSerialization.TryParsePayloadDocument(text, out Payload payload, out _)
                && payload.IsDefined;
        }

        internal static bool TryLoadDocument(PayloadSample sample, out string text, out string? error) =>
            TryLoadDocumentText(sample.FileName, GetDiskPath(sample.FileName), sample.EmbeddedResourceName, out text, out error);

        private static bool TryLoadDocumentText(
            string fileName,
            string? diskPath,
            string? embeddedResourceName,
            out string text,
            out string? error)
        {
            error = null;
            if (diskPath is not null && File.Exists(diskPath))
            {
                text = File.ReadAllText(diskPath, Encoding.UTF8);
                return true;
            }

            string? resource = embeddedResourceName ?? FindEmbeddedResourceForFile(fileName);
            if (resource is not null)
            {
                using Stream? stream = typeof(PayloadSampleCatalog).Assembly.GetManifestResourceStream(resource);
                if (stream is not null)
                {
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    text = reader.ReadToEnd();
                    return true;
                }
            }

            text = "";
            error = $"Sample '{fileName}' not found (tests/Samples or embedded resource).";
            return false;
        }

        private static string? FindEmbeddedResourceForFile(string fileName)
        {
            foreach (string name in typeof(PayloadSampleCatalog).Assembly.GetManifestResourceNames())
            {
                if (!IsEmbeddedSampleResource(name))
                    continue;
                if (name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                    return name;
            }

            return null;
        }

        private static string ResourceNameToFileName(string resourceName)
        {
            int lastDot = resourceName.LastIndexOf('.');
            int prevDot = resourceName.LastIndexOf('.', lastDot - 1);
            if (prevDot >= 0 && lastDot > prevDot)
                return resourceName[(prevDot + 1)..];
            return resourceName;
        }

        private static string? GetDiskPath(string fileName)
        {
            string? dir = TryFindSamplesDirectory();
            return dir is null ? null : Path.Combine(dir, fileName);
        }

        private static string? TryFindSamplesDirectory()
        {
            string? dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "TheArchitectsBlueprintMod.csproj")))
                    return Path.Combine(dir, "tests", "Samples");

                dir = Path.GetDirectoryName(dir);
            }

            return null;
        }
    }
}
#endif
