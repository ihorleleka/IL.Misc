using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace IL.Misc.Helpers;

/// <summary>Best-effort discovery of assemblies and their public types.</summary>
public static class TypesAndAssembliesHelper
{
    /// <summary>Finds loaded assemblies and direct references of the calling assembly matching the filters.</summary>
    /// <remarks>Filters are case-sensitive simple names supporting '*' and '?'. Unloadable references are skipped.</remarks>
    [RequiresUnreferencedCode("Assembly discovery requires assemblies and types that trimming may remove.")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Assembly[] GetAssemblies(params string[] assemblyFilters)
    {
        ArgumentNullException.ThrowIfNull(assemblyFilters);
        if (Array.Exists(assemblyFilters, static filter => filter is null))
        {
            throw new ArgumentException("Assembly filters cannot contain null values.", nameof(assemblyFilters));
        }

        var assemblyNames = new HashSet<string>(assemblyFilters.Where(static filter => !filter.Contains('*') && !filter.Contains('?')), StringComparer.Ordinal);
        var wildcardNames = assemblyFilters.Where(static filter => filter.Contains('*') || filter.Contains('?')).Distinct(StringComparer.Ordinal).ToArray();

        bool Matches(string name) => assemblyFilters.Length == 0 || assemblyNames.Contains(name) ||
            wildcardNames.Any(wildcard => name.MatchesWildcard(wildcard));

        var assemblies = new HashSet<Assembly>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (Matches(assembly.GetName().Name ?? string.Empty))
            {
                assemblies.Add(assembly);
            }
        }

        // Filter metadata before loading: a narrow query must not load every caller dependency.
        foreach (var reference in Assembly.GetCallingAssembly().GetReferencedAssemblies())
        {
            if (!Matches(reference.Name ?? string.Empty))
            {
                continue;
            }

            try
            {
                assemblies.Add(Assembly.Load(reference));
            }
            catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                // Optional/unavailable references must not prevent discovery of healthy assemblies.
            }
        }

        return assemblies.ToArray();
    }

    /// <summary>Returns visible types, retaining successfully loaded types when dependencies are missing.</summary>
    [RequiresUnreferencedCode("Exported types may be removed by trimming.")]
    public static IEnumerable<Type> GetExportedTypes(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>().Where(static type => type.IsVisible).ToArray();
        }
        catch (Exception ex) when (ex is NotSupportedException or FileNotFoundException or FileLoadException or BadImageFormatException or TypeLoadException)
        {
            return Type.EmptyTypes;
        }
    }
}
