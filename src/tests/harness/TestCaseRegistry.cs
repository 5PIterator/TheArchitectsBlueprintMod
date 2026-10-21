#if DEBUG
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TheArchitectsBlueprintMod.Core.TestCases
{
    /// <summary>Discovers in-game Test Cases by reflecting concrete <see cref="ITestCase"/> types.</summary>
    internal static class TestCaseRegistry
    {
        private static readonly IReadOnlyList<ITestCase> _cases = Discover();

        public static IReadOnlyList<ITestCase> Cases => _cases;

        /// <summary>Instantiates every public parameterless <see cref="ITestCase"/> in this assembly, ordered by id.</summary>
        private static IReadOnlyList<ITestCase> Discover()
        {
            // --- Scan the mod assembly, construct each concrete Case, sort by Case Tag.

            Type contract = typeof(ITestCase);
            var discovered = new List<ITestCase>();

            Type[] types = contract.Assembly.GetTypes();
            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (!type.IsClass || type.IsAbstract || !contract.IsAssignableFrom(type))
                    continue;

                ConstructorInfo? ctor = type.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null);
                if (ctor is null)
                    continue;

                if (Activator.CreateInstance(type, nonPublic: true) is not ITestCase testCase)
                    continue;

                discovered.Add(testCase);
            }

            return discovered
                .OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
#endif
