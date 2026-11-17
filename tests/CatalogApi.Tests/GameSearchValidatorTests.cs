using CatalogApi.Application.Validators;
using CatalogApi.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace CatalogApi.Tests;

public class GameSearchValidatorTests
{
    [Fact]
    public void Valid_input_is_trimmed_and_normalized()
    {
        var query = GameSearchValidator.ToQuery("  Cosmic Odyssey ", "  RPG ", 2, 5);

        query.Text.Should().Be("Cosmic Odyssey");
        query.Genre.Should().Be("RPG");
        query.Page.Should().Be(2);
        query.PageSize.Should().Be(5);
        query.From.Should().Be(5);
    }

    [Fact]
    public void Blank_genre_becomes_no_filter()
        => GameSearchValidator.ToQuery("racing", "   ", 1, 10).Genre.Should().BeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    [InlineData(" b ")]
    public void Missing_or_too_short_query_is_rejected(string? text)
        => GameSearchValidator.Validate(text, null, 1, 10).IsValid.Should().BeFalse();

    [Fact]
    public void Too_long_query_is_rejected()
        => GameSearchValidator.Validate(new string('x', 101), null, 1, 10).IsValid.Should().BeFalse();

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    [InlineData(201, 50)] // 201 * 50 > 10 000 (max result window)
    public void Out_of_range_pagination_is_rejected(int page, int pageSize)
        => GameSearchValidator.Validate("cosmic", null, page, pageSize).IsValid.Should().BeFalse();

    [Fact]
    public void Last_page_inside_the_result_window_is_accepted()
        => GameSearchValidator.Validate("cosmic", null, 200, 50).IsValid.Should().BeTrue();

    [Fact]
    public void Invalid_input_becomes_a_domain_exception_with_every_error()
    {
        var act = () => GameSearchValidator.ToQuery("a", null, 0, 100);

        act.Should().Throw<DomainException>()
            .Which.Message.Should().Contain("'q'").And.Contain("'page'").And.Contain("'pageSize'");
    }
}
