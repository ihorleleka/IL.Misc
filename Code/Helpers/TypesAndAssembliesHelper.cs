using System.Globalization;
using System.Reflection;

namespace IL.Misc.Helpers;

public static class TypesAndAssembliesHelper
{
    public static Assembly[] GetAssemblies(params string[] assemblyFilters)
    {
        var assemblyNames = new HashSet<string>(assemblyFilters.Where(filter => !filter.Contains('*')));
        var wildcardNames = assemblyFilters.Except(assemblyNames).ToArray();

        var allAssemblies = new HashSet<Assembly>();
        allAssemblies.UnionWith(
            Assembly
                .GetCallingAssembly()
                .GetReferencedAssemblies()
                .Select(Assembly.Load));
        allAssemblies.UnionWith(
            AppDomain
                .CurrentDomain
                .GetAssemblies()
        );

        var assemblies = allAssemblies
            .Where(assembly =>
            {
                if (assemblyFilters.Length == 0)
                {
                    return true;
                }

                var nameToMatch = assembly.GetName().Name!;
                return assemblyNames.Contains(nameToMatch) || wildcardNames.Any(wildcard => nameToMatch.MatchesWildcard(wildcard));
            })
            .ToArray();

        return assemblies;
    }

    public static IEnumerable<Type> GetExportedTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (NotSupportedException)
        {
            // A type load exception would typically happen on an Anonymously Hosted DynamicMethods
            // Assembly, and it would be safe to skip this exception.
            return Type.EmptyTypes;
        }
        catch (FileLoadException)
        {
            // The assembly points to a not found assembly - ignore and continue
            return Type.EmptyTypes;
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Return the types that could be loaded. Types can contain null values.
            //return ex.Types.Where(type => type != null);
            return Type.EmptyTypes;
        }
        catch (Exception ex)
        {
            // Throw a more descriptive message containing the name of the assembly.
            //throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Unable to load types from assembly {0}. {1}", assembly.FullName, ex.Message), ex);
            return Type.EmptyTypes;
        }
    }
}