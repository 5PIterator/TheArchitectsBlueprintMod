using System.Collections.Generic;

namespace ArchitectsStencilMod.Core.Client.PayloadImport
{
    /// <summary>
    /// Root JSON document: metadata + sparse <see cref="Payload"/> cells (layer facets per cell).
    /// </summary>
    public sealed class PayloadDocument
    {
        public int SchemaVersion { get; set; } = PayloadJson.CurrentSchemaVersion;

        public string ProjectName { get; set; } = string.Empty;

        public string PlayerName { get; set; } = string.Empty;

        public int Width { get; set; } = 0;

        public int Height { get; set; } = 0;

        public List<PayloadCell> Payload { get; set; } = [];
    }
}
