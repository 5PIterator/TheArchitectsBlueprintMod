#if DEBUG
#nullable enable

using System.Collections;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Config;

namespace TheArchitectsBlueprintMod.Core.TestCases
{
    /// <summary>
    /// Test Fixture that pins TileMappingConfig.CompiledInteractionMapPath for a Case body, then restores.
    /// </summary>
    internal static class InteractionMapPathFixture
    {
        /// <summary>
        /// Sets the compiled InteractionMap load path for the duration of <paramref name="body"/>, then restores.
        /// </summary>
        public static IEnumerator RunWith(string absoluteMapPath, IEnumerator body)
        {
            // --- Snapshot the config path, pin the fixture absolute path, pump body, always restore.

            TileMappingConfig config = ModContent.GetInstance<TileMappingConfig>();
            string previous = config.CompiledInteractionMapPath ?? "";
            config.CompiledInteractionMapPath = absoluteMapPath;

            while (true)
            {
                bool moved;
                try
                {
                    moved = body.MoveNext();
                }
                catch
                {
                    config.CompiledInteractionMapPath = previous;
                    throw;
                }

                if (!moved)
                    break;

                yield return body.Current;
            }

            config.CompiledInteractionMapPath = previous;
        }
    }
}
#endif
