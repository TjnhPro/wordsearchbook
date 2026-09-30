using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Validation;

namespace WordSearchBook.Infrastructure.WordSearch.Validation;

public sealed class BrandValidationService(IBrandValidationStateStore stateStore) : IBrandValidationService
{
    public async ValueTask<BrandValidationState> CheckStateAsync(
        string rootPath,
        string brandId,
        CancellationToken cancellationToken = default)
    {
        ValidateRoot(rootPath);
        JsonBrandValidationStateStore.ValidateBrandId(brandId);
        cancellationToken.ThrowIfCancellationRequested();

        BrandValidationRecord? record;
        try
        {
            record = await stateStore.LoadAsync(rootPath, brandId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WordSearchGenerationException exception) when (exception.Code == "brand_validation_state_unavailable")
        {
            return NeedsValidation("brand_validation_state_unavailable");
        }

        if (record is null)
        {
            return new BrandValidationState(BrandValidationStatus.NotValidated);
        }

        if (record.SchemaVersion != BrandValidationDefinition.SchemaVersion ||
            record.AssetFingerprintFormatVersion != BrandValidationDefinition.AssetFingerprintFormatVersion)
        {
            return NeedsValidation("brand_validation_record_outdated", record);
        }

        if (record.RequiresValidation)
        {
            return NeedsValidation("brand_validation_required", record);
        }

        if (record.DefinitionChangedAtUtc != BrandValidationDefinition.ChangedAtUtc ||
            !string.Equals(record.DefinitionSignature, BrandValidationDefinition.Signature, StringComparison.Ordinal))
        {
            return NeedsValidation("brand_definition_changed", record);
        }

        try
        {
            var fingerprint = CaptureFingerprint(rootPath, brandId);
            if (!string.Equals(record.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return NeedsValidation("brand_fingerprint_changed", record);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return NeedsValidation("brand_validation_state_unavailable", record);
        }

        if (!HasExpectedFacts(record.Assets))
        {
            return NeedsValidation("brand_validation_record_invalid", record);
        }

        return new BrandValidationState(
            BrandValidationStatus.Validated,
            record.ValidatedAtUtc,
            record.Fingerprint,
            ValidatedAssets: record.Assets);
    }

    public async ValueTask<BrandValidationResult> ValidateAsync(
        string rootPath,
        string brandId,
        CancellationToken cancellationToken = default)
    {
        ValidateRoot(rootPath);
        JsonBrandValidationStateStore.ValidateBrandId(brandId);
        var layoutPath = ResolveLayoutPath(rootPath, brandId);
        cancellationToken.ThrowIfCancellationRequested();

        var previous = await TryLoadReusableRecordAsync(rootPath, brandId, cancellationToken);
        var beforeFingerprint = CaptureFingerprint(rootPath, brandId);
        var failures = new List<BrandValidationFailure>();
        BrandValidationAssetFact? asset = null;

        if (!File.Exists(layoutPath))
        {
            failures.Add(Failure("exists", "page_layout_not_found", $"Page layout was not found: {layoutPath}"));
        }
        else
        {
            try
            {
                await using var stream = new FileStream(
                    layoutPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: true);
                using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
                if (image.RawFormat.Guid != ImageFormat.Png.Guid)
                {
                    failures.Add(Failure("format:png", "page_layout_format_invalid", "Page layout must be a PNG image."));
                }

                if (image.Width != BrandValidationDefinition.PageWidth ||
                    image.Height != BrandValidationDefinition.PageHeight)
                {
                    failures.Add(Failure(
                        $"dimensions:{BrandValidationDefinition.PageWidth}x{BrandValidationDefinition.PageHeight}",
                        "page_layout_dimensions_invalid",
                        $"Page layout is {image.Width}x{image.Height}; required size is {BrandValidationDefinition.PageWidth}x{BrandValidationDefinition.PageHeight}."));
                }

                using var decoded = new Bitmap(image);
                _ = decoded.GetPixel(0, 0);
                asset = new BrandValidationAssetFact(
                    BrandValidationDefinition.PageLayoutRelativePath,
                    image.Width,
                    image.Height);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (OutOfMemoryException exception)
            {
                failures.Add(Failure("readable", "page_layout_invalid", $"Page layout is not a readable PNG image: {exception.Message}"));
            }
            catch (ArgumentException exception)
            {
                failures.Add(Failure("readable", "page_layout_invalid", $"Page layout is not a readable PNG image: {exception.Message}"));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ExternalException)
            {
                failures.Add(Failure("readable", "page_layout_read_failed", $"Page layout could not be read: {exception.Message}"));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var afterFingerprint = CaptureFingerprint(rootPath, brandId);
        if (!string.Equals(beforeFingerprint, afterFingerprint, StringComparison.Ordinal))
        {
            failures.Add(Failure("stable", "brand_changed_during_validation", "Page layout changed during validation. Run validation again."));
        }

        if (asset is not null && !HasExpectedFacts([asset]))
        {
            failures.Add(Failure("facts", "brand_validation_record_invalid", "Validated asset facts do not match the page-layout definition."));
        }

        if (failures.Count != 0)
        {
            return await CompleteFailureAsync(rootPath, brandId, previous, failures, cancellationToken);
        }

        var validatedAt = DateTimeOffset.UtcNow;
        var record = new BrandValidationRecord(
            BrandValidationDefinition.SchemaVersion,
            BrandValidationDefinition.AssetFingerprintFormatVersion,
            BrandValidationDefinition.ChangedAtUtc,
            BrandValidationDefinition.Signature,
            afterFingerprint,
            validatedAt,
            RequiresValidation: false,
            [asset!]);
        await stateStore.SaveAsync(rootPath, brandId, record, cancellationToken);
        return new BrandValidationResult(
            new BrandValidationState(
                BrandValidationStatus.Validated,
                validatedAt,
                afterFingerprint,
                ValidatedAssets: record.Assets),
            []);
    }

    private async ValueTask<BrandValidationRecord?> TryLoadReusableRecordAsync(
        string rootPath,
        string brandId,
        CancellationToken cancellationToken)
    {
        try
        {
            var record = await stateStore.LoadAsync(rootPath, brandId, cancellationToken);
            return record is not null &&
                   record.SchemaVersion == BrandValidationDefinition.SchemaVersion &&
                   record.AssetFingerprintFormatVersion == BrandValidationDefinition.AssetFingerprintFormatVersion &&
                   record.DefinitionChangedAtUtc == BrandValidationDefinition.ChangedAtUtc &&
                   string.Equals(record.DefinitionSignature, BrandValidationDefinition.Signature, StringComparison.Ordinal) &&
                   HasExpectedFacts(record.Assets)
                ? record
                : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WordSearchGenerationException exception) when (exception.Code == "brand_validation_state_unavailable")
        {
            return null;
        }
    }

    private async ValueTask<BrandValidationResult> CompleteFailureAsync(
        string rootPath,
        string brandId,
        BrandValidationRecord? previous,
        IReadOnlyList<BrandValidationFailure> failures,
        CancellationToken cancellationToken)
    {
        if (previous is null)
        {
            return new BrandValidationResult(
                new BrandValidationState(BrandValidationStatus.NotValidated),
                failures);
        }

        var invalidated = previous with { RequiresValidation = true };
        await stateStore.SaveAsync(rootPath, brandId, invalidated, cancellationToken);
        return new BrandValidationResult(
            NeedsValidation("brand_validation_required", invalidated),
            failures);
    }

    internal static string CaptureFingerprint(string rootPath, string brandId)
    {
        var path = ResolveLayoutPath(rootPath, brandId);
        var file = new FileInfo(path);
        file.Refresh();
        var metadata = file.Exists
            ? new BrandValidationFileMetadata(
                BrandValidationDefinition.PageLayoutRelativePath,
                file.Length,
                new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero))
            : BrandValidationFileMetadata.Missing(BrandValidationDefinition.PageLayoutRelativePath);
        return BrandAssetFingerprintCalculator.Calculate([metadata]);
    }

    private static bool HasExpectedFacts(IReadOnlyList<BrandValidationAssetFact>? assets)
    {
        if (assets is null || assets.Count != 1)
        {
            return false;
        }

        var asset = assets[0];
        try
        {
            return BrandValidationDefinition.NormalizeRelativePath(asset.RelativePath) == BrandValidationDefinition.PageLayoutRelativePath &&
                   asset.Width == BrandValidationDefinition.PageWidth &&
                   asset.Height == BrandValidationDefinition.PageHeight;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string ResolveLayoutPath(string rootPath, string brandId)
    {
        var certificatePath = JsonBrandValidationStateStore.ResolvePath(rootPath, brandId);
        return Path.Combine(Path.GetDirectoryName(certificatePath)!, BrandValidationDefinition.PageLayoutRelativePath);
    }

    private static void ValidateRoot(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (!Directory.Exists(rootPath))
        {
            throw new WordSearchGenerationException("root_not_found", $"Root directory was not found: {rootPath}");
        }
    }

    private static BrandValidationState NeedsValidation(string reasonCode, BrandValidationRecord? record = null) =>
        new(
            BrandValidationStatus.NeedsValidation,
            record?.ValidatedAtUtc,
            record?.Fingerprint,
            reasonCode,
            record?.Assets);

    private static BrandValidationFailure Failure(string rule, string code, string message) =>
        new(BrandValidationDefinition.PageLayoutRelativePath, rule, code, message);
}
