﻿using IL.Misc.Helpers;
using Xunit;

namespace IL.Misc.Tests.Helpers;

public class TypesAndAssembliesHelperTests
{
    [Fact]
    public void GetAssemblies_ReturnsMatchingAssemblies()
    {
        // Arrange
        var assemblyFilters = new[] { "IL.*" };

        // Act
        var result = TypesAndAssembliesHelper.GetAssemblies(assemblyFilters);

        // Assert
        Assert.Equal(2, result.Length);
    }
}