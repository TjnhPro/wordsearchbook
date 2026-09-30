# Brand Page Layout Validation Plan v2

Status: Implemented
Updated: 2026-09-30
Reference implementation: `D:\C# APP\Winform\coloringbook`

## 1. Objective

Add explicit, user-triggered validation for each Brand `page_layout.png` without decoding every PNG during workspace startup.

The workflow must provide a trustworthy certificate:

- The user explicitly selects **Validate layout**.
- Deep validation opens and decodes the PNG and checks the fixed `2588x3375` contract.
- A Brand-local certificate records the validated facts and a fast metadata fingerprint.
- Workspace refresh checks only the certificate and file metadata.
- Generation accepts only a currently certified Brand.

This version adopts the proven certificate semantics from ColoringBook while preserving WordSearchBook's rule that filesystem and validation work runs through the background task queue.

## 2. Locked decisions

- Track only `brands/{brand}/page_layout.png`; changes to `settings.json` do not invalidate the layout certificate.
- Store the certificate at `brands/{brand}/brand.validation.json`.
- Use three states: `NotValidated`, `Validated`, `NeedsValidation`.
- A metadata mismatch means `NeedsValidation`, not `Invalid`, because startup has not inspected the new image content.
- Do not compute or persist a full content SHA-256.
- Deep validation uses the existing System.Drawing dependency; do not add ImageMagick.
- A newly created Brand starts as `NotValidated`, even though the application created a valid white default layout.
- Validation remains available when global or Brand settings are invalid because the page-layout contract uses fixed application constants.
- Generation is blocked in both UI and backend unless the layout state is `Validated`.
- Generation still performs the renderer's existing PNG checks as defense against a file changing after the certificate gate.
- Do not add `FileSystemWatcher`. External changes are observed on application load, workspace refresh, or an explicit validation action.
- Do not automatically migrate legacy global page settings in this feature.

## 3. Public contracts

Add the following Core contracts under a Brand validation namespace.

```csharp
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
    public bool IsSuccess =>
        State.Status == BrandValidationStatus.Validated && Failures.Count == 0;
}
```

The service and store contracts are:

```csharp
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
```

Extend workspace contracts as follows:

```csharp
public sealed record WorkspaceBrand(
    string Id,
    BrandWordSearchSettings? Settings,
    BrandValidationState LayoutValidation,
    WorkspaceIssue? Issue);

public sealed record BrandPageLayoutValidationRequest(
    string RootPath,
    string BrandId);

public sealed record BrandPageLayoutValidationTaskResult(
    WorkspaceSnapshot Snapshot,
    BrandValidationResult Validation);
```

Add `BackgroundTaskKind.BrandPageLayoutValidation`.

## 4. Validation definition and certificate

### 4.1 Definition

The initial definition is fixed and does not read `settings.json`:

- Definition format version: `1`.
- `DefinitionChangedAtUtc`: `2026-09-30T00:00:00Z`.
- Entry key: `page-layout`.
- Target: file `page_layout.png`.
- Rules:
  - file exists;
  - image is readable;
  - actual image format is PNG;
  - width is `2588`;
  - height is `3375`.

Any future change to the tracked file scope or validation semantics must:

1. update `DefinitionChangedAtUtc` to the actual UTC rule-change time;
2. update the canonical definition manifest when required;
3. add or update tests proving existing certificates become `NeedsValidation`.

### 4.2 Definition signature

Build an invariant UTF-8 manifest, order entries and rules with `StringComparer.Ordinal`, then store lowercase SHA-256 prefixed with `sha256:`.

```text
definitionFormatVersion=1
definitionChangedAtUtc=2026-09-30T00:00:00.0000000+00:00
entry=page-layout
targetKind=file|path=page_layout.png
rule=dimensions:2588x3375
rule=exists
rule=format:png
rule=readable
```

### 4.3 Asset fingerprint

The fast fingerprint never reads file contents. It hashes this invariant manifest:

```text
assetFingerprintFormatVersion=1
entry=page-layout
file=page_layout.png|length={LengthBytes}|lastWriteUtcTicks={LastWriteTimeUtc.UtcTicks}
```

If the file is missing, the file line is:

```text
file=page_layout.png|missing
```

Path normalization rules:

- convert separators to `/`;
- reject rooted paths and `.` or `..` traversal segments;
- lowercase invariant;
- compare and sort with `StringComparer.Ordinal`.

The final fingerprint is lowercase SHA-256 prefixed with `sha256:`.

### 4.4 Persisted JSON

`brand.validation.json` uses web camelCase JSON and is written atomically through a temporary sibling file followed by replace/move.

```json
{
  "schemaVersion": 1,
  "assetFingerprintFormatVersion": 1,
  "definitionChangedAtUtc": "2026-09-30T00:00:00+00:00",
  "definitionSignature": "sha256:...",
  "fingerprint": "sha256:...",
  "validatedAtUtc": "2026-09-30T10:00:00+00:00",
  "requiresValidation": false,
  "assets": [
    {
      "relativePath": "page_layout.png",
      "width": 2588,
      "height": 3375
    }
  ]
}
```

`SchemaVersion` and `AssetFingerprintFormatVersion` both start at `1`.

## 5. State-check algorithm

`CheckStateAsync` is the only operation allowed during workspace startup and ordinary refresh.

Evaluate in this exact order:

1. Validate `rootPath` and `brandId` as safe existing paths.
2. Load `brand.validation.json`.
3. No certificate: return `NotValidated` immediately. Do not stat or open `page_layout.png`.
4. Schema or fingerprint-format mismatch: return `NeedsValidation` with `brand_validation_record_outdated`. Do not stat or open the PNG.
5. `RequiresValidation == true`: return `NeedsValidation` with `brand_validation_required`. Do not stat or open the PNG.
6. Definition timestamp or signature mismatch: return `NeedsValidation` with `brand_definition_changed`. Do not stat or open the PNG.
7. Calculate the current metadata fingerprint. This may read only file metadata.
8. Fingerprint mismatch: return `NeedsValidation` with `brand_fingerprint_changed`.
9. Validate certificate facts structurally:
   - exactly one fact;
   - normalized path is exactly `page_layout.png`;
   - width and height are positive and exactly `2588x3375`;
   - no duplicates or unexpected facts.
10. Invalid facts: return `NeedsValidation` with `brand_validation_record_invalid`.
11. Otherwise return `Validated` with certificate time, fingerprint and facts.

Cancellation must propagate. Malformed JSON, inaccessible certificate state or unexpected metadata errors must not crash workspace refresh; return `NeedsValidation` with `brand_validation_state_unavailable`.

The check-state implementation must never call `Image.FromFile`, `Image.FromStream`, `Bitmap`, or read PNG bytes.

## 6. Deep-validation algorithm

`ValidateAsync` runs only from the explicit background task.

1. Validate safe root and Brand paths.
2. Build the current definition and definition signature.
3. Try to load the previous certificate. A missing or malformed old certificate is treated as no reusable certificate for the deep operation.
4. Capture the metadata fingerprint before image inspection.
5. Validate `page_layout.png`:
   - missing → failure `page_layout_not_found`;
   - filesystem/permission read failure → `page_layout_read_failed`;
   - unreadable image data → `page_layout_invalid`;
   - non-PNG raw format → `page_layout_format_invalid`;
   - wrong dimensions → `page_layout_dimensions_invalid`, with actual and required size in the message.
6. Force image decode while the stream is open so a corrupt PNG cannot pass through lazy header inspection.
7. Capture the metadata fingerprint again after inspection.
8. Before/after mismatch → one `brand_changed_during_validation` failure.
9. Confirm the generated fact set exactly matches the tracked image target.
10. On any failure:
    - if no previous valid certificate exists, do not write a certificate and return state `NotValidated`;
    - if a previous certificate exists, atomically rewrite it with `RequiresValidation = true` and return `NeedsValidation`;
    - return every applicable validation failure to the task result.
11. On success, atomically save a new certificate with one `page_layout.png` fact and return `Validated`.

Do not persist transient validation failure lists in the certificate.

## 7. Background task and bridge flow

Add bridge request:

```json
{
  "id": "...",
  "type": "brand.layout.validate",
  "payload": { "brandId": "demo_1" }
}
```

The router enqueues:

- kind: `BrandPageLayoutValidation`;
- key: `brand-layout:{brandId}`;
- subject: `{brandId}`;
- request: `BrandPageLayoutValidationRequest`.

The worker:

1. reports `Validating page layout`;
2. calls `ValidateAsync`;
3. reports `Refreshing workspace`;
4. calls `IWorkspaceSnapshotService.RefreshAsync`;
5. returns `BrandPageLayoutValidationTaskResult`.

A completed validation with image-rule failures remains a successfully completed background task because validation itself completed. Only cancellation, unsafe paths, inability to persist a required certificate mutation, or an unexpected infrastructure failure makes the task fail.

Preserve the existing task polling response shape:

```json
{
  "task": { "...": "..." },
  "result": { "...workspace snapshot...": "..." },
  "brandValidationResult": {
    "state": { "status": "NeedsValidation" },
    "failures": []
  }
}
```

`BackgroundTaskDetail.Result` remains the workspace snapshot. Add nullable `BrandValidationResult`. In `task.get`, detect whether the stored result is a plain `WorkspaceSnapshot` or `BrandPageLayoutValidationTaskResult` and map accordingly. Existing task consumers must remain compatible.

## 8. Workspace and generation behavior

### Workspace snapshot

- Always call `CheckStateAsync` for each discovered Brand, independently from global/Brand settings loading.
- A global settings failure may set `WorkspaceBrand.Settings` to null and populate `Issue`, but must not hide layout validation state or the Validate button.
- Snapshot refresh reads the small certificate and, only for a current certificate, one file-metadata record. It never inspects PNG content.
- Brand creation does not create a certificate; its first snapshot is `NotValidated`.

### Generation gate

Before reading CSV, generating a puzzle or rendering a board:

1. call `CheckStateAsync` for the requested Brand;
2. continue only when status is `Validated`;
3. otherwise throw `WordSearchGenerationException` with code `brand_layout_not_validated` and include the state reason in the message;
4. retain the existing renderer validation for missing, unreadable, non-PNG or wrong-size layout files.

This closes the race where a Brand changes after the UI snapshot and before generation starts.

## 9. Desktop UI behavior

### Brand List

- Keep the existing `3/9` layout and independent list scrollbar.
- Display settings and layout status separately so a settings error does not masquerade as a layout error.
- Layout badge mapping:
  - `NotValidated` → neutral `Not validated`;
  - `Validated` → green `Validated`;
  - `NeedsValidation` → warning `Needs validation`.

### Brand Detail

Add a Page Layout card that is visible even when settings cannot be loaded. It shows:

- `page_layout.png`;
- required size `2588 × 3375`;
- current validation status;
- last validation time when available;
- abbreviated metadata fingerprint when available;
- state reason or latest validation failures;
- `Validate layout` button.

Button behavior:

- enqueue `brand.layout.validate`;
- show `Validating…` while its task is queued/running/cancelling;
- disable while Brand settings are dirty or saving and show `Save or discard settings changes before validating`;
- disable duplicate validation for the same Brand;
- preserve the selected Brand and search query;
- on completion, apply the returned workspace snapshot and display transient failures from `brandValidationResult`;
- clear transient failures when selecting another Brand or starting another validation for the selected Brand.

### Books

- Keep settings-readable Brands available in the Brand selector.
- Disable `Generate pages` when the selected Brand is not `Validated`.
- Show `Validate this Brand in Brand layouts before generating.`
- Backend enforcement remains authoritative even if a stale frontend sends the request directly.

## 10. Implementation phases

### Phase 1 — Core certificate and fingerprint contracts

Implement:

- validation models, definition and stable reason/failure codes;
- path normalization;
- definition signature calculator;
- metadata fingerprint calculator;
- Brand-local atomic JSON state store interface and implementation;
- DI registrations required by these components.

Tests:

- deterministic signature regardless rule ordering;
- fingerprint changes for missing, replaced or touched layout;
- untracked `settings.json` and `brand.validation.json` do not affect fingerprint;
- legacy/malformed/outdated records fail closed;
- atomic state round trip and temporary-file cleanup;
- path traversal is rejected.

Acceptance:

- contracts compile without Desktop dependency;
- no image API appears in fingerprint or state-store code.

Commit boundary:

```text
feat(core): define brand layout validation certificate
```

### Phase 2 — Deep validation service

Implement:

- `CheckStateAsync` with the exact early-exit order;
- `ValidateAsync` with System.Drawing PNG/readability/dimension checks;
- before/after fingerprint stability check;
- validated fact verification;
- `RequiresValidation` lifecycle.

Tests:

- no record returns `NotValidated` with zero metadata and image reads;
- current certificate returns `Validated` using metadata only;
- changed metadata returns `NeedsValidation` without image inspection;
- definition change invalidates the certificate without metadata inspection;
- valid PNG creates a certificate and fact;
- missing, corrupt, non-PNG and wrong-size files return stable failures;
- failed first validation writes no certificate;
- failed revalidation marks the prior certificate `RequiresValidation`;
- file mutation during validation cannot create a certificate;
- cancellation propagates.

Acceptance:

- startup state check never opens PNG content;
- deep validation never certifies an unstable or semantically invalid file.

Commit boundary:

```text
feat(infrastructure): validate brand page layouts
```

### Phase 3 — Workspace projection and generation gate

Implement:

- add validation state to `WorkspaceBrand`;
- calculate it independently of settings success;
- add validation service dependency to generation;
- fail generation before CSV/puzzle work unless state is `Validated`;
- keep renderer defense checks.

Tests:

- snapshot exposes `NotValidated`, `Validated` and `NeedsValidation`;
- layout state still appears when global or Brand settings are invalid;
- refresh for an unvalidated Brand performs no PNG metadata/content read;
- generation rejects uncertified and changed Brands;
- certified Brand reaches the existing four-artifact generation path;
- file change after gate is still rejected by the renderer.

Acceptance:

- no generation work starts for an uncertified Brand;
- workspace refresh cost is limited to certificate JSON plus metadata for current certificates.

Commit boundary:

```text
feat(core): gate generation on certified brand layouts
```

### Phase 4 — Background task and bridge integration

Implement:

- new task kind and request/result contracts;
- keyed worker registration;
- `brand.layout.validate` bridge route;
- task-detail mapping for combined snapshot and validation result;
- duplicate-task behavior using the existing manager key.

Tests:

- bridge rejects missing/unsafe Brand IDs;
- bridge enqueues the correct typed request;
- worker success returns validation result and refreshed snapshot;
- rule failure completes the task and returns failures;
- persistence/infrastructure failure fails the task with a stable error;
- cancellation and duplicate task behavior match existing queue semantics;
- existing task-detail payloads remain backward compatible.

Acceptance:

- all layout validation filesystem/image work occurs off the UI thread;
- existing refresh, settings, creation and generation tasks remain unchanged.

Commit boundary:

```text
feat(desktop): run brand layout validation in background
```

### Phase 5 — Brand and Books UI

Implement:

- validation badges in Brand List;
- Page Layout card and Validate button in Brand Detail;
- transient failure rendering;
- dirty/saving guard;
- active-task button state;
- Books Generate gate and guidance;
- documentation updates in README and UI guidelines;
- regenerate and commit Tailwind CSS.

Tests:

- frontend maps all three statuses correctly;
- Brand Detail renders validation independently from settings errors;
- button sends the correct typed message;
- dirty/saving state disables validation;
- task completion applies snapshot and failures without losing selection;
- Generate is disabled unless selected Brand is `Validated`;
- Node tests and CSS verification leave the working tree unchanged.

Acceptance:

- the user can copy/replace `page_layout.png`, click Validate, see actionable results and generate only after certification;
- Brand search, independent scrolling and unsaved-change protection continue to work.

Commit boundary:

```text
feat(desktop): surface brand layout validation
```

## 11. Final verification

Run:

```powershell
dotnet build WordSearchBook.sln -c Release --no-restore
dotnet test WordSearchBook.sln -c Release --no-build
npm test --prefix src/WordSearchBook.Desktop/Frontend
npm run verify:css --prefix src/WordSearchBook.Desktop/Frontend
git diff --check
git status --short
```

Manual acceptance scenario:

1. Start with an existing Brand containing a valid `2588x3375` PNG and no certificate.
2. Confirm workspace shows `Not validated` without decoding the PNG.
3. Click **Validate layout** and confirm status becomes `Validated`.
4. Confirm `brand.validation.json` is created beside the Brand assets.
5. Confirm generation produces board and complete page artifacts.
6. Replace or touch `page_layout.png`.
7. Refresh workspace and confirm status becomes `Needs validation` without deep image inspection.
8. Confirm generation is blocked.
9. Validate an incorrect-size PNG and confirm the UI reports actual and required dimensions.
10. Restore the correct PNG, validate again and confirm generation is re-enabled.

Final repository state must have a clean working tree and one commit per phase above.
