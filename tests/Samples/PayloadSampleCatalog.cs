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
            if (IsBinaryTab(sample.FileName))
            {
                if (!TryLoadDocumentBytes(sample, out byte[] bytes, out error))
                    return false;

                return PayloadSerialization.TryParsePayloadDocument(bytes, out payload, out _, out error);
            }

            if (!TryLoadDocument(sample, out string text, out error))
                return false;

            return PayloadSerialization.TryParsePayloadDocument(text, out payload, out error);
        }

        internal static string SlugFromFileName(string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName);
            if (name.EndsWith(".tab", StringComparison.OrdinalIgnoreCase))
                name = Path.GetFileNameWithoutExtension(name);
            return name.ToLowerInvariant();
        }

        private static PayloadSample[] Discover()
        {
            var bySlug = new Dictionary<string, PayloadSample>(StringComparer.OrdinalIgnoreCase);

            RegisterEmbeddedSamples(bySlug);
            RegisterDiskSamples(bySlug);

            if (bySlug.Count == 0)
            {
                throw new InvalidOperationException(
                    "No payload samples found. Add *.tab or *.tab.json under tests/Samples and embed them in the csproj.");
            }

            return bySlug.Values.OrderBy(s => s.FileName, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static void RegisterEmbeddedSamples(Dictionary<string, PayloadSample> bySlug)
        {
            foreach (string resourceName in typeof(PayloadSampleCatalog).Assembly.GetManifestResourceNames())
            {
                if (!IsEmbeddedSampleResource(resourceName))
                    continue;

                string fileName = ResourceNameToFileName(resourceName);
                if (!TryValidateSample(fileName, null, resourceName, out _))
                    continue;

                RegisterSample(bySlug, fileName, resourceName);
            }
        }

        private static void RegisterDiskSamples(Dictionary<string, PayloadSample> bySlug)
        {
            string? samplesDir = TryFindSamplesDirectory();
            if (samplesDir is null)
                return;

            foreach (string path in Directory.EnumerateFiles(samplesDir, "*.tab.json"))
            {
                string fileName = Path.GetFileName(path);
                if (!TryValidateSample(fileName, path, null, out _))
                    continue;

                RegisterSample(bySlug, fileName, null);
            }

            foreach (string path in Directory.EnumerateFiles(samplesDir, "*.tab"))
            {
                if (path.EndsWith(".tab.json", StringComparison.OrdinalIgnoreCase))
                    continue;

                string fileName = Path.GetFileName(path);
                if (!TryValidateSample(fileName, path, null, out _))
                    continue;

                RegisterSample(bySlug, fileName, null);
            }
        }

        private static void RegisterSample(Dictionary<string, PayloadSample> bySlug, string fileName, string? embeddedResourceName)
        {
            string slug = SlugFromFileName(fileName);
            bool isBinary = IsBinaryTab(fileName);
            if (bySlug.TryGetValue(slug, out PayloadSample? existing))
            {
                bool existingBinary = IsBinaryTab(existing.FileName);
                if (existingBinary && !isBinary)
                    return;
            }

            bySlug[slug] = new PayloadSample(fileName, slug, embeddedResourceName);
        }

        private static bool IsBinaryTab(string fileName) =>
            fileName.EndsWith(".tab", StringComparison.OrdinalIgnoreCase)
            && !fileName.EndsWith(".tab.json", StringComparison.OrdinalIgnoreCase);

        private static bool IsEmbeddedSampleResource(string resourceName)
        {
            if (!resourceName.Contains(".tests.Samples.", StringComparison.OrdinalIgnoreCase))
                return false;

            return resourceName.EndsWith(".tab.json", StringComparison.OrdinalIgnoreCase)
                || (resourceName.EndsWith(".tab", StringComparison.OrdinalIgnoreCase)
                    && !resourceName.EndsWith(".tab.json", StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryValidateSample(string fileName, string? diskPath, string? embeddedResourceName, out string? error)
        {
            error = null;
            if (IsBinaryTab(fileName))
            {
                if (!TryLoadDocumentBytes(fileName, GetDiskPath(fileName, diskPath), embeddedResourceName, out byte[] bytes, out error))
                    return false;

                return PayloadSerialization.TryParsePayloadDocument(bytes, out Payload binaryPayload, out _, out error)
                    && binaryPayload.IsDefined;
            }

            if (!TryLoadDocumentText(fileName, diskPath, embeddedResourceName, out string text, out error))
                return false;

            return PayloadSerialization.TryParsePayloadDocument(text, out Payload jsonPayload, out error)
                && jsonPayload.IsDefined;
        }

        internal static bool TryLoadDocument(PayloadSample sample, out string text, out string? error) =>
            TryLoadDocumentText(sample.FileName, GetDiskPath(sample.FileName, null), sample.EmbeddedResourceName, out text, out error);

        private static bool TryLoadDocumentBytes(PayloadSample sample, out byte[] bytes, out string? error) =>
            TryLoadDocumentBytes(sample.FileName, GetDiskPath(sample.FileName, null), sample.EmbeddedResourceName, out bytes, out error);

        private static bool TryLoadDocumentText(
            string fileName,
            string? diskPath,
            string? embeddedResourceName,
            out string text,
            out string? error)
        {
            error = null;
            string? path = GetDiskPath(fileName, diskPath);
            if (path is not null && File.Exists(path))
            {
                text = File.ReadAllText(path, Encoding.UTF8);
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

        private static bool TryLoadDocumentBytes(
            string fileName,
            string? diskPath,
            string? embeddedResourceName,
            out byte[] bytes,
            out string? error)
        {
            error = null;
            string? path = GetDiskPath(fileName, diskPath);
            if (path is not null && File.Exists(path))
            {
                bytes = File.ReadAllBytes(path);
                return true;
            }

            string? resource = embeddedResourceName ?? FindEmbeddedResourceForFile(fileName);
            if (resource is not null)
            {
                using Stream? stream = typeof(PayloadSampleCatalog).Assembly.GetManifestResourceStream(resource);
                if (stream is not null)
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    bytes = ms.ToArray();
                    return true;
                }
            }

            bytes = [];
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

        private static string? GetDiskPath(string fileName, string? explicitPath)
        {
            if (explicitPath is not null)
                return explicitPath;

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
