using TheArchitectsBlueprintMod.Config;
using Microsoft.Xna.Framework;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Shared paste limit calculations used for client UX hints and server-authoritative validation.</summary>
    internal static class PasteLimits
    {
        /// <summary>Counts included cells in the blueprint inclusion grid.</summary>
        public static int CountIncludedVolume(Blueprint blueprint)
        {
            if (blueprint is null || !blueprint.IsDefined || blueprint.Tiles is null)
                return 0;

            int count = 0;
            foreach (Blueprint.BlueprintCell tile in blueprint.Tiles)
            {
                if (tile.Included)
                    count++;
            }

            return count;
        }

        /// <summary>Returns true when execution width or height exceeds configured paste limits.</summary>
        public static bool ExceedsArea(PasteExecutionContext execution, PastingConfig config)
        {
            if (execution is null || !execution.IsDefined)
                return true;

            Rectangle area = execution.TargetArea;
            return area.Width > config.Limits.Client.MaxPasteWidth || area.Height > config.Limits.Client.MaxPasteHeight;
        }

        /// <summary>Returns true when included cell count exceeds the configured volume limit.</summary>
        public static bool ExceedsVolume(PasteExecutionContext execution, PastingConfig config)
        {
            if (execution is null || !execution.IsDefined)
                return true;

            return execution.CountIncludedVolume() > config.Limits.Client.VolumeLimit;
        }

        /// <summary>Returns true when projection width or height exceeds configured paste limits.</summary>
        public static bool ExceedsArea(Projection projection, PastingConfig config)
        {
            if (projection is null || !projection.IsDefined)
                return true;

            Rectangle area = projection.Area;
            return area.Width > config.Limits.Client.MaxPasteWidth || area.Height > config.Limits.Client.MaxPasteHeight;
        }

        /// <summary>Returns true when included cell count exceeds the configured volume limit.</summary>
        public static bool ExceedsVolume(Projection projection, PastingConfig config)
        {
            if (projection is null || !projection.IsDefined)
                return true;

            return CountIncludedVolume(projection) > config.Limits.Client.VolumeLimit;
        }
    }
}
