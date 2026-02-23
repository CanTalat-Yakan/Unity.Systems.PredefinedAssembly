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
        public const BindingFlags DefaultMethodFlags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        
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
        /// Returns loaded runtime assemblies.
        /// 
        /// Order:
        /// 1) Known Unity script assemblies (Assembly-CSharp, Assembly-CSharp-firstpass)
        /// 2) All other loaded assemblies (optionally including those with "Editor" in their name)
        /// 
        /// This is useful for reflection-based discovery systems that want consistent behavior
        /// across projects with or without asmdefs.
        /// </summary>
        public static List<Assembly> GetRuntimeAssemblies(bool includeEditorAssemblies = false)
        {
            var result = new List<Assembly>(64);

            var loaded = AppDomain.CurrentDomain.GetAssemblies();

            // 1) Prefer "known" Unity script assemblies first.
            var known = new[]
            {
                AssemblyType.AssemblyCSharp,
                AssemblyType.AssemblyCSharpFirstPass,
            };

            for (var i = 0; i < loaded.Length; i++)
            {
                var asm = loaded[i];
                if (asm == null) continue;
                if (asm.IsDynamic) continue;

                var asmName = asm.GetName().Name;
                if (string.IsNullOrEmpty(asmName)) continue;

                var asmType = GetAssemblyType(asmName);
                if (asmType.HasValue && Array.IndexOf(known, asmType.Value) >= 0)
                    result.Add(asm);
            }

            // 2) Add the rest (deduped), skipping Editor assemblies by heuristic.
            for (var i = 0; i < loaded.Length; i++)
            {
                var asm = loaded[i];
                if (asm == null) continue;
                if (asm.IsDynamic) continue;

                var name = asm.GetName().Name ?? string.Empty;
                if (!includeEditorAssemblies && name.IndexOf("Editor", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                if (!result.Contains(asm))
                    result.Add(asm);
            }

            return result;
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

                var types = SafeGetTypes(assembly).ToArray();
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
                    // Heuristic: skip editor assemblies/packages.
                    if (name.IndexOf("Editor", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                var asmTypes = SafeGetTypes(assembly);
                AddTypesFromAssembly(asmTypes as Type[] ?? asmTypes.ToArray(), types, interfaceType);
            }
        }

        /// <summary>
        /// Enumerates all loaded runtime types across the assemblies returned by <see cref="GetRuntimeAssemblies"/>.
        /// </summary>
        public static IEnumerable<Type> EnumerateRuntimeTypes(bool includeEditorAssemblies = false)
        {
            var assemblies = GetRuntimeAssemblies(includeEditorAssemblies);
            for (var i = 0; i < assemblies.Count; i++)
            {
                var asm = assemblies[i];
                var types = SafeGetTypes(asm);
                for (var j = 0; j < types.Count; j++)
                    yield return types[j];
            }
        }

        /// <summary>
        /// Enumerates methods of all runtime types.
        /// </summary>
        public static IEnumerable<MethodInfo> EnumerateRuntimeMethods(
            BindingFlags flags = DefaultMethodFlags,
            bool includeEditorAssemblies = false)
        {
            foreach (var type in EnumerateRuntimeTypes(includeEditorAssemblies))
            {
                if (type == null)
                    continue;

                MethodInfo[] methods;
                try { methods = type.GetMethods(flags); }
                catch { continue; }

                for (var i = 0; i < methods.Length; i++)
                    yield return methods[i];
            }
        }
        
        /// <summary>
        /// Enumerates runtime methods that have at least one <typeparamref name="TAttribute"/>.
        /// </summary>
        public static IEnumerable<MethodInfo> EnumerateRuntimeMethodsWithAttribute<TAttribute>(
            BindingFlags flags = DefaultMethodFlags,
            bool inherit = false,
            bool includeEditorAssemblies = false)
            where TAttribute : Attribute
        {
            foreach (var method in EnumerateRuntimeMethods(flags, includeEditorAssemblies))
            {
                if (method == null)
                    continue;

                var has = false;
                try { has = method.IsDefined(typeof(TAttribute), inherit); }
                catch { has = false; }

                if (has)
                    yield return method;
            }
        }

        /// <summary>
        /// Enumerates runtime methods and their <typeparamref name="TAttribute"/> instances.
        /// 
        /// Useful when your attribute allows multiple instances per method.
        /// </summary>
        public static IEnumerable<(MethodInfo Method, TAttribute Attribute)> EnumerateRuntimeMethodsWithAttributes<TAttribute>(
            BindingFlags flags = DefaultMethodFlags,
            bool inherit = false,
            bool includeEditorAssemblies = false)
            where TAttribute : Attribute
        {
            foreach (var method in EnumerateRuntimeMethods(flags, includeEditorAssemblies))
            {
                if (method == null)
                    continue;

                TAttribute[] attrs;
                try { attrs = method.GetCustomAttributes<TAttribute>(inherit).ToArray(); }
                catch { continue; }

                for (var i = 0; i < attrs.Length; i++)
                    yield return (method, attrs[i]);
            }
        }

        /// <summary>
        /// Safely returns all types from an assembly.
        /// Never throws; returns an empty list if the assembly can't be inspected.
        /// </summary>
        public static IReadOnlyList<Type> SafeGetTypes(Assembly assembly)
        {
            if (assembly == null || assembly.IsDynamic)
                return Array.Empty<Type>();

            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                if (e.Types == null)
                    return Array.Empty<Type>();

                return e.Types.Where(t => t != null).ToArray();
            }
            catch
            {
                return Array.Empty<Type>();
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
