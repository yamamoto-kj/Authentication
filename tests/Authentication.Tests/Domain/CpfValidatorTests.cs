using Authentication.Domain.Common;
using FluentAssertions;
using Xunit;

namespace Authentication.Tests.Domain;

public class CpfValidatorTests
{
    [Theory]
    [InlineData("52998224725")]
    [InlineData("11144477735")]
    [InlineData("529.982.247-25")]
    public void IsValid_KnownValidCpf_ReturnsTrue(string cpf)
    {
        CpfValidator.IsValid(cpf).Should().BeTrue();
    }

    [Theory]
    [InlineData("11111111111")]
    [InlineData("00000000000")]
    [InlineData("12345678900")]
    [InlineData("529982247")]
    [InlineData("")]
    [InlineData(null)]
    public void IsValid_InvalidCpf_ReturnsFalse(string? cpf)
    {
        CpfValidator.IsValid(cpf).Should().BeFalse();
    }

    [Fact]
    public void Normalize_ValidFormattedCpf_ReturnsDigitsOnly()
    {
        CpfValidator.Normalize("529.982.247-25").Should().Be("52998224725");
    }

    [Fact]
    public void Normalize_InvalidCpf_ReturnsNull()
    {
        CpfValidator.Normalize("11111111111").Should().BeNull();
    }
}
