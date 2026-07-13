namespace CatalogApi.Application.Validators;

public record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ValidationResult Success() => new(true, Array.Empty<string>());
    public static ValidationResult Fail(params string[] errors) => new(false, errors);
}
