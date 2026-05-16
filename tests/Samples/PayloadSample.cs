#if DEBUG
#nullable enable

namespace TheArchitectsBlueprintMod.Tests.Samples
{
    /// <summary>One <c>tests/Samples/*.tab.json</c> document; scenarios inspect payload content at runtime.</summary>
    internal sealed class PayloadSample(string fileName, string slug, string? embeddedResourceName)
    {
        public string FileName { get; } = fileName;
        public string Slug { get; } = slug;
        public string? EmbeddedResourceName { get; } = embeddedResourceName;
    }
}
#endif
