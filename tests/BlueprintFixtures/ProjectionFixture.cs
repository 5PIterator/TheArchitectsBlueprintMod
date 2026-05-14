#if DEBUG
#nullable enable

using System;
using ArchitectsBlueprintMod.Core;

namespace ArchitectsBlueprintMod.Tests.Fixtures
{
    /// <summary>Creates <see cref="Projection"/> instances from an existing selection.</summary>
    internal static class ProjectionFixture
    {
        internal static bool TryCreate(Selection selection, bool cutSource, out Projection projection, out string? error)
        {
            projection = null!;
            error = null;
            ArgumentNullException.ThrowIfNull(selection);
            try
            {
                projection = new Projection(selection.Owner, selection, cutSource);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static bool TryCreateWithImportedPayload(
            Selection selection,
            Payload payload,
            bool cutSource,
            out Projection projection,
            out string? error)
        {
            if (!TryCreate(selection, cutSource, out projection, out error))
                return false;

            projection.SetPayload(payload, discardSelection: true);
            return true;
        }
    }
}
#endif
