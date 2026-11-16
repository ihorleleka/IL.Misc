using System.Reflection;
using IL.Misc.Helpers;
using Xunit;

namespace IL.Misc.Tests.Helpers;

public class TypesAndAssembliesHelperTests
{
    [Fact]
    public void GetAssemblies_ReturnsMatchingAssemblies()
    {
        var result = TypesAndAssembliesHelper.GetAssemblies("IL.*");
        Assert.Contains(typeof(TypesAndAssembliesHelper).Assembly, result);
        Assert.Contains(typeof(TypesAndAssembliesHelperTests).Assembly, result);
        Assert.All(result, assembly => Assert.StartsWith("IL.", assembly.GetName().Name));
    }

    [Fact]
    public void GetAssemblies_SupportsQuestionMarkWithoutStar()
    {
        var result = TypesAndAssembliesHelper.GetAssemblies("IL.Mis?");
        Assert.Single(result);
        Assert.Same(typeof(TypesAndAssembliesHelper).Assembly, result[0]);
    }

    [Fact]
    public void GetAssemblies_ValidatesFilters()
    {
        Assert.Throws<ArgumentNullException>(() => TypesAndAssembliesHelper.GetAssemblies(null!));
        Assert.Throws<ArgumentException>(() => TypesAndAssembliesHelper.GetAssemblies("IL.*", null!));
    }

    [Fact]
    public void GetExportedTypes_PreservesVisibleTypesWhenSomeFailToLoad()
    {
        var exception = new ReflectionTypeLoadException(
            new Type?[] { typeof(TypesAndAssembliesHelperTests), null, typeof(HiddenType), typeof(PublicNestedType) },
            new Exception[] { new TypeLoadException("Missing optional dependency") });
        var result = TypesAndAssembliesHelper.GetExportedTypes(new ThrowingAssembly(exception));
        Assert.Equal(new[] { typeof(TypesAndAssembliesHelperTests), typeof(PublicNestedType) }, result);
    }

    [Theory]
    [InlineData(typeof(FileNotFoundException))]
    [InlineData(typeof(FileLoadException))]
    [InlineData(typeof(NotSupportedException))]
    [InlineData(typeof(BadImageFormatException))]
    [InlineData(typeof(TypeLoadException))]
    public void GetExportedTypes_SkipsExpectedLoaderFailures(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;
        Assert.Empty(TypesAndAssembliesHelper.GetExportedTypes(new ThrowingAssembly(exception)));
    }

    [Fact]
    public void GetExportedTypes_DoesNotHideUnexpectedFailures()
    {
        var exception = new InvalidOperationException("Unexpected failure");
        Assert.Same(exception, Assert.Throws<InvalidOperationException>(() =>
            TypesAndAssembliesHelper.GetExportedTypes(new ThrowingAssembly(exception))));
        Assert.Throws<ArgumentNullException>(() => TypesAndAssembliesHelper.GetExportedTypes(null!));
    }

    public sealed class PublicNestedType { }
    private sealed class HiddenType { }

    private sealed class ThrowingAssembly(Exception exception) : Assembly
    {
        public override Type[] GetExportedTypes() => throw exception;
    }
}
