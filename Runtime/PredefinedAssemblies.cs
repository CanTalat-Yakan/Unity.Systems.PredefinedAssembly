using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UnityEssentials
{
    /// <summary>
    /// Utility class for working with predefined Unity assemblies and extracting types based on interface implementation.
    /// </summary>
    public static class PredefinedAssemblies
    {
        /// <summary>
        /// Enum representing commonly known Unity assemblies.
        /// </summary>
        public enum AssemblyType
        {
            AssemblyCSharpFirstPass,
            AssemblyCSharpEditorFirstPass,
            AssemblyCSharp,
            AssemblyCSharpEditor,
        }

        /// <summary>
        /// Maps an assembly name string to a predefined AssemblyType enum value.
        /// </summary>
        /// <param name="assemblyName">The name of the assembly.</param>
        /// <returns>The corresponding AssemblyType if known; otherwise, null.</returns>
        public static AssemblyType? GetAssemblyType(string assemblyName) =>
            assemblyName switch
            {
                "Assembly-CSharp-firstpass" => AssemblyType.AssemblyCSharpFirstPass,
                "Assembly-CSharp-Editor-firstpass" => AssemblyType.AssemblyCSharpEditorFirstPass,
                "Assembly-CSharp" => AssemblyType.AssemblyCSharp,
                "Assembly-CSharp-Editor" => AssemblyType.AssemblyCSharpEditor,
                _ => null
            };

        /// <summary>
        /// Searches known Unity runtime assemblies for types implementing the given interface type.
        /// If none are found in the predefined assemblies, it falls back to scanning all loaded non-editor assemblies.
        /// </summary>
        /// <param name="interfaceType">The interface type to search for.</param>
        /// <returns>A list of types that implement the given interface.</returns>
        public static List<Type> GetTypes(Type interfaceType)
        {
            // 1. Get all loaded assemblies
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            // 2. Filter predefined Unity assemblies
            var assemblyTypes = FilterAssemblies(assemblies);

            // 3. Check type implementations
            List<Type> types = new();
            if (assemblyTypes.TryGetValue(AssemblyType.AssemblyCSharp, out var csharpTypes))
                AddTypesFromAssembly(csharpTypes, types, interfaceType);
            if (assemblyTypes.TryGetValue(AssemblyType.AssemblyCSharpFirstPass, out var firstPassTypes))
                AddTypesFromAssembly(firstPassTypes, types, interfaceType);

            // 4. Fallback: in projects using asmdefs, settings often live outside Assembly-CSharp.
            //    If nothing was found, scan all loaded runtime assemblies (excluding obvious editor ones).
            if (types.Count == 0)
                AddTypesFromAssemblies(assemblies, types, interfaceType, includeEditorAssemblies: false);

            return types;
        }

        /// <summary>
        /// Filters the provided assemblies and categorizes them by a custom AssemblyType enum.
        /// Only assemblies that can be classified with a known AssemblyType are included.
        /// </summary>
        /// <param name="assemblies">An array of assemblies to filter.</param>
        /// <returns>
        /// A dictionary mapping each recognized AssemblyType to the array of types defined in its corresponding assembly.
        /// </returns>
        private static Dictionary<AssemblyType, Type[]> FilterAssemblies(Assembly[] assemblies)
        {
            var result = new Dictionary<AssemblyType, Type[]>();

            foreach (var assembly in assemblies)
            {
                var type = GetAssemblyType(assembly.GetName().Name);
                if (!type.HasValue) continue;

                var types = SafeGetTypes(assembly);
                result[type.Value] = types;
            }

            return result;
        }

        private static void AddTypesFromAssemblies(IEnumerable<Assembly> assemblies, ICollection<Type> types, Type interfaceType, bool includeEditorAssemblies)
        {
            foreach (var assembly in assemblies)
            {
                if (assembly == null) continue;
                if (assembly.IsDynamic) continue;

                var name = assembly.GetName().Name ?? string.Empty;
                if (!includeEditorAssemblies)
                {
                    // Heuristic: skip editor assemblies/packages.
                    if (name.IndexOf("Editor", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                }

                var asmTypes = SafeGetTypes(assembly);
                AddTypesFromAssembly(asmTypes, types, interfaceType);
            }
        }

        private static Type[] SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null).ToArray();
            }
        }

        /// <summary>
        /// Adds types from the given assembly array to the result list if they implement the given interface.
        /// </summary>
        /// <param name="assembly">The array of types from an assembly.</param>
        /// <param name="types">The list to populate with matching types.</param>
        /// <param name="interfaceType">The interface type to match against.</param>
        private static void AddTypesFromAssembly(Type[] assembly, ICollection<Type> types, Type interfaceType)
        {
            foreach (var type in assembly)
                if (type != null && type != interfaceType && interfaceType.IsAssignableFrom(type))
                    types.Add(type);
        }
    }
}
