#if DEBUG
#nullable enable

using System;
using System.IO;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.TestCases
{
    /// <summary>Resolves the on-disk dump directory for Test Case logs.</summary>
    internal static class TestCasePaths
    {
        private const string TestsDirectoryName = "tests";

        /// <summary>
        /// Returns <c>{SavePath}/TheArchitectsBlueprintMod/tests</c>, creating it if missing.
        /// </summary>
        public static string GetTestsDirectory()
        {
            // --- Reuse the mod save root, then ensure the tests dump folder exists.
            string dir = Path.Combine(TileMappingSystem.GetModDirectory(), TestsDirectoryName);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>Builds a unique log file path under the tests dump directory.</summary>
        public static string CreateRunLogPath(string runLabel)
        {
            // --- Sanitize the label and stamp the file name so parallel runs do not collide.
            string safe = SanitizeFileToken(runLabel);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return Path.Combine(GetTestsDirectory(), $"case-{safe}-{stamp}.log");
        }

        private static string SanitizeFileToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "run";

            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = value.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0 || chars[i] == ' ')
                    chars[i] = '_';
            }

            string token = new string(chars);
            return string.IsNullOrWhiteSpace(token) ? "run" : token;
        }
    }
}
#endif
