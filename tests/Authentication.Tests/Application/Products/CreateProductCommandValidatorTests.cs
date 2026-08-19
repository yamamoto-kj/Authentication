using Authentication.Application.Products.Commands;
using FluentAssertions;
using Xunit;

namespace Authentication.Tests.Application.Products;

public class CreateProductCommandValidatorTests
{
    private readonly CreateProductCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_HasNoErrors()
    {
        var command = new CreateProductCommand("Widget", "A widget", 9.99m, 10);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", 9.99, 10)]
    public void Validate_EmptyName_HasError(string name, decimal price, int stock)
    {
        var command = new CreateProductCommand(name, null, price, stock);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateProductCommand.Name));
    }

    [Fact]
    public void Validate_NegativePrice_HasError()
    {
        var command = new CreateProductCommand("Widget", null, -1m, 10);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateProductCommand.Price));
    }

    [Fact]
    public void Validate_NegativeStock_HasError()
    {
        var command = new CreateProductCommand("Widget", null, 9.99m, -1);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateProductCommand.StockQuantity));
    }
}
