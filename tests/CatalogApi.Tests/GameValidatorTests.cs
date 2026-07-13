using CatalogApi.Application.Validators;
using FluentAssertions;
using Xunit;

namespace CatalogApi.Tests;

public class GameValidatorTests
{
    [Fact]
    public void Title_required()
        => GameValidator.Validate("", 10m).IsValid.Should().BeFalse();

    [Fact]
    public void Negative_price_invalid()
        => GameValidator.Validate("Game", -1m).IsValid.Should().BeFalse();

    [Fact]
    public void Valid_data()
        => GameValidator.Validate("Game", 50m).IsValid.Should().BeTrue();
}
