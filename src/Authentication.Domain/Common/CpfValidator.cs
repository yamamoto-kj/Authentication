namespace Authentication.Domain.Common;

/// <summary>
/// Validates the Brazilian CPF check-digit algorithm. CPF is the system's
/// global user identifier (Identity UserName), so a malformed one must
/// never reach persistence - this is checked wherever a CPF is accepted
/// from a client (admin user provisioning today; self-service flows later).
/// </summary>
public static class CpfValidator
{
    public static bool IsValid(string? cpf)
    {
        if (string.IsNullOrWhiteSpace(cpf))
        {
            return false;
        }

        var digits = new string(cpf.Where(char.IsDigit).ToArray());

        if (digits.Length != 11)
        {
            return false;
        }

        // Reject sequences like "00000000000", "11111111111" etc. - they
        // pass the check-digit math below but are never real documents.
        if (digits.Distinct().Count() == 1)
        {
            return false;
        }

        var numbers = digits.Select(c => c - '0').ToArray();

        var firstCheckDigit = ComputeCheckDigit(numbers, 9);
        if (firstCheckDigit != numbers[9])
        {
            return false;
        }

        var secondCheckDigit = ComputeCheckDigit(numbers, 10);
        return secondCheckDigit == numbers[10];
    }

    /// <summary>Normalizes to digits-only, or null if the input isn't a valid CPF.</summary>
    public static string? Normalize(string? cpf)
    {
        if (!IsValid(cpf))
        {
            return null;
        }

        return new string(cpf!.Where(char.IsDigit).ToArray());
    }

    private static int ComputeCheckDigit(int[] numbers, int length)
    {
        var sum = 0;
        var multiplier = length + 1;

        for (var i = 0; i < length; i++)
        {
            sum += numbers[i] * multiplier;
            multiplier--;
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
