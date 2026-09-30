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
            var metadata = BrandAssetDiscovery.CaptureMetadata(rootPath, brandId);
            var fingerprint = BrandAssetFingerprintCalculator.Calculate(metadata);
            if (!string.Equals(record.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return NeedsValidation("brand_fingerprint_changed", record);
            }

            if (!HasExpectedFacts(record.Assets, metadata))
            {
                return NeedsValidation("brand_validation_record_invalid", record);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return NeedsValidation("brand_validation_state_unavailable", record);
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
        cancellationToken.ThrowIfCancellationRequested();

        var beforeMetadata = BrandAssetDiscovery.CaptureMetadata(rootPath, brandId);
        var beforeFingerprint = BrandAssetFingerprintCalculator.Calculate(beforeMetadata);
        var previous = await TryLoadReusableRecordAsync(rootPath, brandId, beforeMetadata, cancellationToken);
        var failures = new List<BrandValidationFailure>();
        var assets = new List<BrandValidationAssetFact>();
        var layoutPath = BrandAssetDiscovery.ResolveLayoutPath(rootPath, brandId);

        if (!File.Exists(layoutPath))
        {
            failures.Add(Failure(
                BrandValidationDefinition.PageLayoutRelativePath,
                "exists",
                "page_layout_not_found",
                $"Page layout was not found: {layoutPath}"));
        }
        else
        {
            var asset = ValidateImage(
                layoutPath,
                BrandValidationDefinition.PageLayoutRelativePath,
                requirePng: true,
                failures);
            if (asset is not null)
            {
                assets.Add(asset);
            }
        }

        foreach (var file in BrandAssetDiscovery.DiscoverTrackedFiles(rootPath, brandId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var asset = ValidateImage(file.FullPath, file.RelativePath, requirePng: false, failures);
            if (asset is not null)
            {
                assets.Add(asset);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var afterMetadata = BrandAssetDiscovery.CaptureMetadata(rootPath, brandId);
        var afterFingerprint = BrandAssetFingerprintCalculator.Calculate(afterMetadata);
        if (!string.Equals(beforeFingerprint, afterFingerprint, StringComparison.Ordinal))
        {
            failures.Add(Failure(
                "brand",
                "stable",
                "brand_changed_during_validation",
                "Brand assets changed during validation. Run validation again."));
        }

        if (failures.Count == 0 && !HasExpectedFacts(assets, afterMetadata))
        {
            failures.Add(Failure(
                "brand",
                "facts",
                "brand_validation_record_invalid",
                "Validated asset facts do not match the brand definition."));
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
            assets);
        await stateStore.SaveAsync(rootPath, brandId, record, cancellationToken);
        return new BrandValidationResult(
            new BrandValidationState(
                BrandValidationStatus.Validated,
                validatedAt,
                afterFingerprint,
                ValidatedAssets: record.Assets),
            []);
    }

    internal static string CaptureFingerprint(string rootPath, string brandId) =>
        BrandAssetFingerprintCalculator.Calculate(BrandAssetDiscovery.CaptureMetadata(rootPath, brandId));

    private async ValueTask<BrandValidationRecord?> TryLoadReusableRecordAsync(
        string rootPath,
        string brandId,
        IReadOnlyList<BrandValidationFileMetadata> metadata,
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
                   HasExpectedFacts(record.Assets, metadata)
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

    private static BrandValidationAssetFact? ValidateImage(
        string fullPath,
        string relativePath,
        bool requirePng,
        ICollection<BrandValidationFailure> failures)
    {
        try
        {
            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: false);
            using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            var isPng = image.RawFormat.Guid == ImageFormat.Png.Guid;
            var isJpeg = image.RawFormat.Guid == ImageFormat.Jpeg.Guid;
            if (requirePng && !isPng)
            {
                failures.Add(Failure(relativePath, "format:png", "page_layout_format_invalid", "Page layout must be a PNG image."));
            }
            else if (!requirePng && !isPng && !isJpeg)
            {
                failures.Add(Failure(relativePath, "format:jpeg|png", "brand_asset_format_invalid", $"Brand asset '{relativePath}' must be a PNG or JPEG image."));
            }

            if (image.Width != BrandValidationDefinition.PageWidth ||
                image.Height != BrandValidationDefinition.PageHeight)
            {
                var code = requirePng ? "page_layout_dimensions_invalid" : "brand_asset_dimensions_invalid";
                failures.Add(Failure(
                    relativePath,
                    $"dimensions:{BrandValidationDefinition.PageWidth}x{BrandValidationDefinition.PageHeight}",
                    code,
                    $"Image '{relativePath}' is {image.Width}x{image.Height}; required size is {BrandValidationDefinition.PageWidth}x{BrandValidationDefinition.PageHeight}."));
            }

            using var decoded = new Bitmap(image);
            _ = decoded.GetPixel(0, 0);
            return new BrandValidationAssetFact(relativePath, image.Width, image.Height);
        }
        catch (OutOfMemoryException exception)
        {
            failures.Add(UnreadableFailure(relativePath, requirePng, exception.Message));
        }
        catch (ArgumentException exception)
        {
            failures.Add(UnreadableFailure(relativePath, requirePng, exception.Message));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ExternalException)
        {
            var code = requirePng ? "page_layout_read_failed" : "brand_asset_read_failed";
            failures.Add(Failure(relativePath, "readable", code, $"Image '{relativePath}' could not be read: {exception.Message}"));
        }

        return null;
    }

    private static bool HasExpectedFacts(
        IReadOnlyList<BrandValidationAssetFact>? assets,
        IReadOnlyList<BrandValidationFileMetadata> metadata)
    {
        if (assets is null)
        {
            return false;
        }

        var expectedPaths = metadata
            .Where(file => file.LengthBytes is not null && file.LastWriteTimeUtc is not null)
            .Select(file => BrandValidationDefinition.NormalizeRelativePath(file.RelativePath))
            .ToHashSet(StringComparer.Ordinal);
        if (!expectedPaths.Contains(BrandValidationDefinition.PageLayoutRelativePath) ||
            assets.Count != expectedPaths.Count)
        {
            return false;
        }

        var actualPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in assets)
        {
            try
            {
                if (asset.Width != BrandValidationDefinition.PageWidth ||
                    asset.Height != BrandValidationDefinition.PageHeight ||
                    !actualPaths.Add(BrandValidationDefinition.NormalizeRelativePath(asset.RelativePath)))
                {
                    return false;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        return actualPaths.SetEquals(expectedPaths);
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

    private static BrandValidationFailure UnreadableFailure(string relativePath, bool requirePng, string detail) =>
        Failure(
            relativePath,
            "readable",
            requirePng ? "page_layout_invalid" : "brand_asset_invalid",
            $"Image '{relativePath}' is not readable: {detail}");

    private static BrandValidationFailure Failure(string target, string rule, string code, string message) =>
        new(target, rule, code, message);
}
