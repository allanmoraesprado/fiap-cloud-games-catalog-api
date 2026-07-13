namespace CatalogApi.Application.Validators;

public static class GameValidator
{
    public static ValidationResult Validate(string? title, decimal price)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(title))
            errors.Add("Game title is required.");
        if (price < 0)
            errors.Add("Game price cannot be negative.");
        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Fail(errors.ToArray());
    }
}
