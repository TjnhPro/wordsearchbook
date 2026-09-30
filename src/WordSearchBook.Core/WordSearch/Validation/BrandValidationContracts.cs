namespace WordSearchBook.Core.WordSearch.Validation;

public enum BrandValidationStatus
{
    NotValidated,
    Validated,
    NeedsValidation
}

public sealed record BrandValidationAssetFact(
    string RelativePath,
    int Width,
    int Height);

public sealed record BrandValidationRecord(
    int SchemaVersion,
    int AssetFingerprintFormatVersion,
    DateTimeOffset DefinitionChangedAtUtc,
    string DefinitionSignature,
    string Fingerprint,
    DateTimeOffset ValidatedAtUtc,
    bool RequiresValidation,
    IReadOnlyList<BrandValidationAssetFact> Assets);

public sealed record BrandValidationState(
    BrandValidationStatus Status,
    DateTimeOffset? ValidatedAtUtc = null,
    string? Fingerprint = null,
    string? ReasonCode = null,
    IReadOnlyList<BrandValidationAssetFact>? ValidatedAssets = null);

public sealed record BrandValidationFailure(
    string Target,
    string Rule,
    string Code,
    string Message);

public sealed record BrandValidationResult(
    BrandValidationState State,
    IReadOnlyList<BrandValidationFailure> Failures)
{
    public bool IsSuccess => State.Status == BrandValidationStatus.Validated && Failures.Count == 0;
}

public interface IBrandValidationService
{
    ValueTask<BrandValidationState> CheckStateAsync(
        string rootPath,
        string brandId,
        CancellationToken cancellationToken = default);

    ValueTask<BrandValidationResult> ValidateAsync(
        string rootPath,
        string brandId,
        CancellationToken cancellationToken = default);
}

public interface IBrandValidationStateStore
{
    ValueTask<BrandValidationRecord?> LoadAsync(
        string rootPath,
        string brandId,
        CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        string rootPath,
        string brandId,
        BrandValidationRecord record,
        CancellationToken cancellationToken = default);
}

