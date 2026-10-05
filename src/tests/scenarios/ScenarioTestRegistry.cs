#if DEBUG
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Discovers in-game scenario tests by reflecting concrete <see cref="IScenarioTest"/> types.</summary>
    internal static class ScenarioTestRegistry
    {
        private static readonly IReadOnlyList<IScenarioTest> _scenarios = Discover();

        public static IReadOnlyList<IScenarioTest> Scenarios => _scenarios;

        /// <summary>Instantiates every public parameterless <see cref="IScenarioTest"/> in this assembly, ordered by id.</summary>
        private static IReadOnlyList<IScenarioTest> Discover()
        {
            // --- Scan the mod assembly, construct each concrete scenario, sort by dotted id.

            Type contract = typeof(IScenarioTest);
            var discovered = new List<IScenarioTest>();

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

                if (Activator.CreateInstance(type, nonPublic: true) is not IScenarioTest scenario)
                    continue;

                discovered.Add(scenario);
            }

            return discovered
                .OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
#endif
