const routeDefinitions = {
  books: { title: "Books", heading: "Word-search books", copy: "Validate CSV input, inspect topics, and publish print-ready output." },
  brands: { title: "Brands", heading: "Brand layouts", copy: "Review and edit the layout settings for an existing brand." },
  tasks: { title: "Tasks", heading: "Background tasks", copy: "Queued and running work stays isolated from the desktop UI thread." },
  settings: { title: "Settings", heading: "Workspace settings", copy: "Review the fixed output format and edit global board settings." }
};
const fixedPageSize = { width: 2588, height: 3375 };
const activeTaskStates = new Set(["Queued", "Running", "Cancelling"]);
const state = {
  route: "books", snapshot: null, tasks: [], selectedBookId: null, selectedBrandId: null,
  bookSearchQuery: "", selectedBookTab: "overview", bookValidationFeedback: null, bookProcessingFeedback: null,
  brandSearchQuery: "", brandBaselineId: null, brandBaseline: null, brandDirty: false,
  brandSaving: false, brandValidationFeedback: null, brandPreviewFeedback: null,
  pendingNavigation: null, client: null, pollTimers: new Map()
};

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>'"]/g, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", "\"": "&quot;" })[character]);
}

function parseBridgeData(data) { return typeof data === "string" ? JSON.parse(data) : data; }
function issueMarkup(issue) { return issue ? `<p class="issue-text">${escapeHtml(issue.message)}</p>` : ""; }
function badge(label, tone = "neutral") { return `<span class="badge badge-${tone}">${escapeHtml(label)}</span>`; }
function brandValidationPresentation(validation) {
  const status = validation?.status ?? "NotValidated";
  if (status === "Validated") return { label: "Validated", tone: "good" };
  if (status === "NeedsValidation") return { label: "Needs validation", tone: "warn" };
  return { label: "Not validated", tone: "neutral" };
}
function canGenerateWithBrand(brand) { return brand?.validation?.status === "Validated"; }
function bookDataValidationPresentation(validation) {
  const status = validation?.status ?? "NotValidated";
  if (status === "Validated") return { label: "Validated", tone: "good" };
  if (status === "Invalid") return { label: "Invalid", tone: "bad" };
  if (status === "NeedsValidation") return { label: "Needs validation", tone: "warn" };
  return { label: "Not validated", tone: "neutral" };
}
function bookOutputPresentation(output) {
  const status = output?.status ?? "Missing";
  if (status === "Ready") return { label: "Ready", tone: "good" };
  if (status === "Stale") return { label: "Stale", tone: "warn" };
  if (status === "Unavailable") return { label: "Unavailable", tone: "bad" };
  return { label: "Not processed", tone: "neutral" };
}
function filterBooks(books, query) {
  const normalized = String(query ?? "").trim().toLocaleLowerCase();
  return normalized ? books.filter(book => book.id.toLocaleLowerCase().includes(normalized)) : books;
}
function bookValidationFailureContext(failure) {
  const context = [];
  if (failure?.sourceRow) context.push(`Row ${failure.sourceRow}`);
  if (failure?.topic) context.push(String(failure.topic));
  return context.join(" · ") || "CSV";
}
function formatBytes(value) {
  const bytes = Number(value ?? 0);
  if (!Number.isFinite(bytes) || bytes <= 0) return "—";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 ** 2) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 ** 2)).toFixed(1)} MB`;
}
function formatDate(value) { return value ? new Date(value).toLocaleString() : "Never"; }
function isBookTaskActive(kind, bookId) {
  return state.tasks.some(task => task.kind === kind && task.subject === bookId && activeTaskStates.has(task.state));
}
function isBrandValidationActive(brandId) {
  return state.tasks.some(task => task.kind === "BrandValidation" && task.subject === brandId && activeTaskStates.has(task.state));
}
function isBrandPreviewActive(brandId) {
  return state.tasks.some(task => task.kind === "BrandPagePreview" && task.subject === brandId && activeTaskStates.has(task.state));
}
function brandPreviewActionDisabled(brandDirty, brandSaving, previewActive) {
  return brandDirty || brandSaving || previewActive;
}
function shortFingerprint(value) {
  const fingerprint = String(value ?? "");
  return fingerprint.length > 24 ? `${fingerprint.slice(0, 21)}…` : fingerprint || "—";
}
function filterBrands(brands, query) {
  const normalized = String(query ?? "").trim().toLocaleLowerCase();
  return normalized ? brands.filter(brand => brand.id.toLocaleLowerCase().includes(normalized)) : brands;
}
function createDebouncedAction(callback, delay = 250, timers = globalThis) {
  let timerId;
  return value => {
    if (timerId !== undefined) timers.clearTimeout(timerId);
    timerId = timers.setTimeout(() => callback(value), delay);
  };
}
function canonicalBrandSettings(settings) { return JSON.stringify(settings); }
function hasBrandSettingsChanged(settings, baseline) { return canonicalBrandSettings(settings) !== baseline; }
function shouldRenderForTaskUpdate(routeName, snapshotChanged, brandDirty = false) {
  if (routeName === "tasks") return true;
  if (routeName === "brands") return snapshotChanged && !brandDirty;
  return snapshotChanged;
}
function brandNavigationDisposition(routeName, brandDirty, brandSaving) {
  if (brandSaving) return "blocked";
  return routeName === "brands" && brandDirty ? "prompt" : "apply";
}
function validateBrandFolderName(value) {
  const name = String(value ?? "").trim();
  const reserved = new Set(["con", "prn", "aux", "nul", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9", "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9"]);
  if (!name) return "Enter a brand folder name.";
  if (name.length > 64) return "Brand folder name must be 64 characters or fewer.";
  if (name === "." || name === ".." || /[<>:"/\\|?*\u0000-\u001F]/u.test(name) || name.endsWith(".")) return "Enter a valid Windows folder name.";
  if (reserved.has(name.split(".")[0].toLocaleLowerCase())) return "This folder name is reserved by Windows.";
  return null;
}
function applyNavigation(destination, render) {
  if (destination.kind === "brand") {
    state.selectedBrandId = destination.value;
    state.brandValidationFeedback = null;
    state.brandPreviewFeedback = null;
    render("brands");
    return;
  }
  render(destination.value);
}
function requestNavigation(destination, documentRoot, render) {
  const disposition = brandNavigationDisposition(state.route, state.brandDirty, state.brandSaving);
  if (disposition === "blocked") return false;
  if (disposition === "prompt") {
    state.pendingNavigation = destination;
    documentRoot.querySelector("#unsaved-brand-dialog")?.showModal();
    return false;
  }
  applyNavigation(destination, render);
  return true;
}

function bookRowsMarkup(books, selectedBookId) {
  if (!books.length) return `<div class="book-list-empty"><strong>No matching books</strong><p>Try a different folder name.</p></div>`;
  const windowed = books.slice(0, 250);
  const rows = windowed.map(book => {
    const validation = bookDataValidationPresentation(book.dataValidation);
    return `<button class="book-row ${book.id === selectedBookId ? "book-row-active" : ""}" data-action="select-book" data-book-id="${escapeHtml(book.id)}"><span><strong>${escapeHtml(book.id)}</strong><small>${book.topicCount || 0} topic${book.topicCount === 1 ? "" : "s"}</small></span>${badge(validation.label, validation.tone)}</button>`;
  }).join("");
  const remainder = books.length > windowed.length
    ? `<p class="book-window-note">Showing the first ${windowed.length} matches. Refine the search to narrow the list.</p>`
    : "";
  return `${rows}${remainder}`;
}

function renderBooks() {
  if (!state.snapshot) return `<div class="empty-panel"><p class="empty-panel-title">Scanning workspace…</p><p class="empty-panel-copy">Books and brands are loaded by a background task.</p></div>`;
  const books = state.snapshot.books ?? [];
  const brands = state.snapshot.brands ?? [];
  if (!books.length) return `<div class="empty-panel"><p class="empty-panel-title">No books found</p><p class="empty-panel-copy">Add input/{book}/data.csv below ${escapeHtml(state.snapshot.rootPath)} and refresh.</p><button class="button-secondary mt-5" data-action="refresh">Refresh</button></div>`;

  if (!books.some(book => book.id === state.selectedBookId)) state.selectedBookId = books[0].id;
  const selected = books.find(book => book.id === state.selectedBookId);
  const validBrands = brands.filter(brand => !brand.issue);
  const selectedBrand = selected.selectedBrandId ?? "";
  const selectedBrandRecord = brands.find(brand => brand.id === selectedBrand);
  const selectedBrandValidated = canGenerateWithBrand(selectedBrandRecord);
  const validationActive = isBookTaskActive("BookDataValidation", selected.id);
  const processingActive = isBookTaskActive("BookProcessing", selected.id);
  const dataValidation = selected.dataValidation ?? { status: "NotValidated", topics: [], failures: [] };
  const dataPresentation = bookDataValidationPresentation(dataValidation);
  const output = selected.output ?? { status: "Missing" };
  const outputPresentation = bookOutputPresentation(output);
  const options = [`<option value="">Choose a brand</option>`, ...validBrands.map(brand => `<option value="${escapeHtml(brand.id)}" ${brand.id === selectedBrand ? "selected" : ""}>${escapeHtml(brand.id)}</option>`)].join("");
  const filteredBooks = filterBooks(books, state.bookSearchQuery);
  const validationFeedback = state.bookValidationFeedback?.bookId === selected.id
    ? state.bookValidationFeedback.failures ?? []
    : dataValidation.failures ?? [];
  const failureRows = validationFeedback.length
    ? `<div class="book-validation-errors"><h4>Validation issues</h4><ul>${validationFeedback.map(failure => `<li><span>${escapeHtml(bookValidationFailureContext(failure))}</span><p>${escapeHtml(failure.message)}</p></li>`).join("")}</ul></div>`
    : "";
  const topics = dataValidation.topics ?? [];
  const topicRows = topics.length
    ? topics.map(topic => `<tr><td>${escapeHtml(topic.name)}</td><td>${topic.keywordCount}/20</td><td>${badge(topic.isValid ? "Ready" : "Invalid", topic.isValid ? "good" : "bad")}</td></tr>`).join("")
    : `<tr><td colspan="3">Validate data.csv to load the topic summary.</td></tr>`;
  const overview = `<div class="book-tab-panel"><div class="book-status-grid"><div><span>CSV status</span><strong>${dataPresentation.label}</strong></div><div><span>Topics</span><strong>${dataValidation.topicCount ?? selected.topicCount ?? 0}</strong></div><div><span>Keywords</span><strong>${dataValidation.keywordCount ?? 0}</strong></div><div><span>Validated</span><strong>${escapeHtml(formatDate(dataValidation.validatedAtUtc))}</strong></div></div><div class="book-card"><div class="book-card-heading"><div><h4>data.csv</h4><p title="${escapeHtml(dataValidation.contentHash ?? "")}">${escapeHtml(shortFingerprint(dataValidation.contentHash))}</p></div>${badge(dataPresentation.label, dataPresentation.tone)}</div><div class="action-row"><button class="button-primary" data-action="validate-book-data" data-book-id="${escapeHtml(selected.id)}" ${validationActive || processingActive ? "disabled" : ""}>${validationActive ? "Validating…" : "Validate CSV"}</button></div></div>${failureRows}<div class="table-scroll book-topic-table"><table><thead><tr><th>Topic</th><th>Keywords</th><th>Status</th></tr></thead><tbody>${topicRows}</tbody></table></div></div>`;
  const processAllowed = dataValidation.status === "Validated" && Boolean(selectedBrand) && selectedBrandValidated && !selectedBrandRecord?.issue && !processingActive && !validationActive;
  const guidance = dataValidation.status !== "Validated"
    ? "Validate data.csv before processing."
    : !selectedBrand
      ? "Choose a Brand before processing."
      : selectedBrandRecord?.issue
        ? selectedBrandRecord.issue.message
        : !selectedBrandValidated
          ? "Validate this Brand in Brand layouts before processing."
          : "";
  const processingFeedback = state.bookProcessingFeedback?.bookId === selected.id ? state.bookProcessingFeedback : null;
  const outputPanel = `<div class="book-tab-panel"><div class="book-output-header"><div><span>Output status</span><div>${badge(outputPresentation.label, outputPresentation.tone)}</div></div><div><span>Last processed</span><strong>${escapeHtml(formatDate(output.processedAtUtc))}</strong></div></div><label class="field"><span>Brand</span><select data-action="assign-brand" data-book-id="${escapeHtml(selected.id)}" ${processingActive ? "disabled" : ""}>${options}</select></label>${guidance ? `<p class="generation-guidance">${escapeHtml(guidance)}</p>` : ""}<dl class="output-summary-grid"><div><dt>PDF</dt><dd>${escapeHtml(output.pdfFileName ?? `${selected.id}.interior.pdf`)}</dd><small>${formatBytes(output.pdfLengthBytes)}</small></div><div><dt>PDF pages</dt><dd>${output.pdfPageCount ?? 0}</dd><small>${output.frontPageCount ?? 0} front · ${output.puzzlePageCount ?? 0} puzzle · ${output.backPageCount ?? 0} back</small></div><div><dt>Answers</dt><dd>${output.answerCount ?? 0} JPG</dd><small>${formatBytes(output.answerLengthBytes)} · quality 85</small></div><div><dt>Print raster</dt><dd>2588 × 3375</dd><small>300 pixels / inch</small></div></dl>${output.reasonCode ? `<p class="output-stale-reason">${escapeHtml(output.reasonCode)}</p>` : ""}${processingFeedback ? `<p class="book-process-feedback" data-state="${escapeHtml(processingFeedback.tone)}">${escapeHtml(processingFeedback.message)}</p>` : ""}<div class="action-row"><button class="button-primary" data-action="process-book" data-book-id="${escapeHtml(selected.id)}" data-brand-id="${escapeHtml(selectedBrand)}" ${processAllowed ? "" : "disabled"}>${processingActive ? "Processing…" : "Process"}</button><button class="button-secondary" data-action="open-book-output" data-book-id="${escapeHtml(selected.id)}" ${output.status === "Ready" || output.status === "Stale" ? "" : "disabled"}>Open folder</button></div></div>`;
  const detail = state.selectedBookTab === "output" ? outputPanel : overview;
  return `<div class="book-workspace"><section class="panel book-list-panel"><div class="book-panel-header"><div><h3>Books</h3><p data-book-result-count>${filteredBooks.length} of ${books.length} shown</p></div><button class="button-secondary" data-action="refresh">Refresh</button></div><label class="book-search"><span class="sr-only">Search books</span><input type="search" data-action="search-books" value="${escapeHtml(state.bookSearchQuery)}" placeholder="Search folder name…" autocomplete="off"></label><div class="book-list-scroll" data-book-list>${bookRowsMarkup(filteredBooks, selected.id)}</div></section><section class="panel book-detail-panel"><div class="book-detail-header"><div><p class="eyebrow">Book detail</p><h3>${escapeHtml(selected.id)}</h3></div><div class="badge-row">${badge(dataPresentation.label, dataPresentation.tone)}${badge(outputPresentation.label, outputPresentation.tone)}</div></div><div class="book-tabs" role="tablist"><button type="button" role="tab" data-action="select-book-tab" data-tab="overview" aria-selected="${state.selectedBookTab === "overview"}">Overview</button><button type="button" role="tab" data-action="select-book-tab" data-tab="output" aria-selected="${state.selectedBookTab === "output"}">Output</button></div><div class="book-detail-scroll">${detail}</div></section></div>`;
}

function renderTasks() {
  if (!state.tasks.length) return `<div class="empty-panel"><p class="empty-panel-title">No task history</p><p class="empty-panel-copy">Workspace refresh, validation, processing, and settings work will appear here.</p></div>`;
  const rows = state.tasks.map(task => {
    const active = activeTaskStates.has(task.state);
    const tone = task.state === "Completed" ? "good" : task.state === "Failed" ? "bad" : active ? "warn" : "neutral";
    return `<tr><td>${escapeHtml(task.kind)}</td><td>${escapeHtml(task.subject ?? "—")}</td><td>${badge(task.state, tone)}</td><td>${escapeHtml(task.step ?? "—")}</td><td>${escapeHtml(task.errorMessage ?? "—")}</td><td>${active ? `<button class="button-link" data-action="cancel-task" data-task-id="${escapeHtml(task.taskId)}">Cancel</button>` : ""}</td></tr>`;
  }).join("");
  return `<section class="panel"><div class="panel-header"><div><h3>Task history</h3><p>Latest activity first</p></div><button class="button-secondary" data-action="list-tasks">Refresh</button></div><div class="table-scroll"><table><thead><tr><th>Kind</th><th>Subject</th><th>State</th><th>Step</th><th>Error</th><th></th></tr></thead><tbody>${rows}</tbody></table></div></section>`;
}

function settingInput(name, label, value, type = "number", extra = "") {
  return `<label class="setting-field"><span>${label}</span><input name="${name}" type="${type}" value="${escapeHtml(value)}" ${extra} required></label>`;
}

function alignmentInput(name, value) {
  const options = ["Left", "Center", "Right"].map(option => `<option value="${option}" ${option === value ? "selected" : ""}>${option}</option>`).join("");
  return `<label class="setting-field"><span>Alignment</span><select name="${name}" required>${options}</select></label>`;
}

function fontInputs(name, font) {
  return `${settingInput(`${name}.fontName`, "Font", font.name, "text")}${settingInput(`${name}.fontSize`, "Font size", font.size, "number", "min=\"0.1\" step=\"0.1\"")}${settingInput(`${name}.fontColor`, "Font color", font.color, "text", "pattern=\"#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?\"")}`;
}

function boardGameEditor(region, answerLine) {
  const rectangle = region.rectangle;
  return `<fieldset class="settings-group brand-region-card"><legend>Board game</legend><div class="brand-region-fields">${settingInput("boardGame.x", "X", rectangle.x)}${settingInput("boardGame.y", "Y", rectangle.y)}${settingInput("boardGame.width", "Width", rectangle.width, "number", "min=\"1\"")}${settingInput("boardGame.height", "Height", rectangle.height, "number", "min=\"1\"")}${fontInputs("boardGame", region.font)}</div><div class="board-answer-settings"><p>Answer styling</p><div class="brand-region-fields">${settingInput("answerLine.width", "Line width", answerLine.width, "number", "min=\"0.1\" step=\"0.1\"")}${settingInput("answerLine.color", "Line color", answerLine.color, "text", "pattern=\"#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?\"")}</div></div></fieldset>`;
}

function anchoredTextEditor(name, label, region) {
  return `<fieldset class="settings-group brand-region-card"><legend>${label}</legend><div class="brand-region-fields">${settingInput(`${name}.x`, "Anchor X", region.x)}${settingInput(`${name}.y`, "Top Y", region.y)}${alignmentInput(`${name}.alignment`, region.alignment)}${fontInputs(name, region.font)}</div></fieldset>`;
}

function keywordListEditor(region) {
  const columnXInputs = region.columns.map((column, index) =>
    settingInput(`keywordList.column${index + 1}X`, `Column ${index + 1} X`, column.x)).join("");
  const columnY = region.columns[0]?.y ?? 0;
  return `<fieldset class="settings-group brand-region-card"><legend>Keyword list</legend><p class="brand-region-help">20 keywords fill top-to-bottom: 5 words per column with one shared starting Y.</p><div class="brand-region-fields">${columnXInputs}${settingInput("keywordList.columnY", "Column Y", columnY)}${settingInput("keywordList.stepY", "Vertical step", region.stepY, "number", "min=\"1\"")}${alignmentInput("keywordList.alignment", region.alignment)}${settingInput("keywordList.fontName", "Font", region.font.name, "text")}${settingInput("keywordList.fontSize", "Font size", region.font.size, "number", "min=\"0.1\" step=\"0.1\"")}${settingInput("keywordList.fontColor", "Font color", region.font.color, "text", "pattern=\"#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?\"")}</div></fieldset>`;
}

function brandRowsMarkup(brands, selectedBrandId) {
  if (!brands.length) return `<div class="brand-list-empty"><strong>No matching brands</strong><p>Try a different brand name.</p></div>`;
  return brands.map(brand => {
    const validation = brandValidationPresentation(brand.validation);
    return `<button class="brand-row ${brand.id === selectedBrandId ? "brand-row-active" : ""}" type="button" data-action="select-brand" data-brand-id="${escapeHtml(brand.id)}"><span><strong>${escapeHtml(brand.id)}</strong><small>${brand.settings ? "Settings ready" : "Settings need attention"}</small></span><span class="brand-row-badges">${brand.settings ? badge("Settings", "good") : badge("Settings", "bad")}${badge(validation.label, validation.tone)}</span></button>`;
  }).join("");
}

function brandLayoutPanels(brand) {
  const validation = brand.validation ?? { status: "NotValidated" };
  const presentation = brandValidationPresentation(validation);
  const active = isBrandValidationActive(brand.id);
  const previewActive = isBrandPreviewActive(brand.id);
  const guarded = state.brandDirty || state.brandSaving;
  const feedback = state.brandValidationFeedback?.brandId === brand.id ? state.brandValidationFeedback.failures ?? [] : [];
  const failures = feedback.length
    ? `<ul class="brand-validation-failures">${feedback.map(failure => `<li>${escapeHtml(failure.message)}</li>`).join("")}</ul>`
    : "";
  const reason = !feedback.length && validation.reasonCode
    ? `<p class="brand-validation-reason">${escapeHtml(validation.reasonCode)}</p>`
    : "";
  const validatedAt = validation.validatedAtUtc ? new Date(validation.validatedAtUtc).toLocaleString() : "Never";
  const guardMessage = guarded ? "Save or discard settings changes before validating or drawing a demo." : "";
  const previewFeedback = state.brandPreviewFeedback?.brandId === brand.id ? state.brandPreviewFeedback : null;
  const previewFeedbackMarkup = `<p class="page-preview-feedback ${previewFeedback ? "" : "hidden"}" data-brand-preview-feedback data-state="${escapeHtml(previewFeedback?.tone ?? "neutral")}" role="status">${escapeHtml(previewFeedback?.message ?? "")}</p>`;
  const layoutPanel = `<fieldset class="settings-group brand-region-card page-layout-card"><legend>Page layout</legend><div class="page-layout-card-heading"><strong>page_layout.png</strong>${badge(presentation.label, presentation.tone)}</div><dl class="page-layout-facts"><div><dt>Required size</dt><dd>2588 × 3375 px</dd></div><div><dt>Last validated</dt><dd>${escapeHtml(validatedAt)}</dd></div><div><dt>Fingerprint</dt><dd title="${escapeHtml(validation.fingerprint ?? "")}">${escapeHtml(shortFingerprint(validation.fingerprint))}</dd></div></dl>${reason}${failures}<div class="page-layout-actions"><button class="button-secondary" type="button" data-action="validate-brand" data-brand-id="${escapeHtml(brand.id)}" data-brand-validation-button ${active || guarded ? "disabled" : ""}>${active ? "Validating…" : "Validate brand"}</button><span data-brand-validation-guard>${escapeHtml(guardMessage)}</span></div></fieldset>`;
  const previewPanel = `<fieldset class="settings-group brand-region-card page-preview-panel"><legend>Page layout preview</legend><p>Draws a fixed 20-word sample with the saved brand settings.</p><code>page_layout.preview.png</code><div class="page-preview-actions"><button class="button-primary" type="button" data-action="draw-brand-preview" data-brand-id="${escapeHtml(brand.id)}" data-brand-preview-button ${brandPreviewActionDisabled(state.brandDirty, state.brandSaving, previewActive) ? "disabled" : ""}>${previewActive ? "Drawing…" : "Draw demo"}</button><button class="button-secondary" type="button" data-action="open-brand-folder" data-brand-id="${escapeHtml(brand.id)}">Open folder</button></div>${previewFeedbackMarkup}</fieldset>`;
  return `${layoutPanel}${previewPanel}`;
}

function brandAssetFolderMarkup(folder, failures = []) {
  const files = folder?.files ?? [];
  const folderName = folder?.key === "back" ? "Back" : "Front";
  const relativePath = folder?.relativePath ?? folderName.toLocaleLowerCase();
  const failureByTarget = new Map(failures.map(failure => [String(failure.target ?? "").toLocaleLowerCase(), failure]));
  const rows = files.map(file => {
    const failure = failureByTarget.get(String(file.relativePath ?? "").toLocaleLowerCase());
    const presentation = failure
      ? { label: "Invalid", tone: "bad" }
      : brandValidationPresentation({ status: file.status });
    const detail = failure ? `<p>${escapeHtml(failure.message)}</p>` : "";
    return `<li class="brand-asset-row"><div><strong title="${escapeHtml(file.relativePath)}">${escapeHtml(file.name)}</strong><span>${escapeHtml(String(file.extension ?? "").replace(/^\./u, "").toLocaleUpperCase())}</span></div>${badge(presentation.label, presentation.tone)}${detail}</li>`;
  }).join("");
  const emptyMessage = folder?.exists === false ? "Folder not found — optional" : "No images — optional";
  const content = rows
    ? `<ul class="brand-asset-list">${rows}</ul>`
    : `<div class="brand-asset-empty"><strong>${emptyMessage}</strong><p>Add a PNG or JPG when this Brand needs a ${folderName.toLocaleLowerCase()} page.</p></div>`;
  return `<fieldset class="settings-group brand-region-card brand-asset-card"><legend>${folderName}</legend><div class="brand-asset-heading"><div><code>${escapeHtml(relativePath)}/</code><p>PNG/JPG · 2588 × 3375 px</p></div><div>${badge("Optional")}${badge(`${files.length} image${files.length === 1 ? "" : "s"}`, files.length ? "good" : "neutral")}</div></div>${content}</fieldset>`;
}

function brandAssetPanels(brand) {
  const folders = brand.assetFolders ?? [];
  const failures = state.brandValidationFeedback?.brandId === brand.id
    ? state.brandValidationFeedback.failures ?? []
    : [];
  const folderByKey = new Map(folders.map(folder => [folder.key, folder]));
  return ["front", "back"]
    .map(key => brandAssetFolderMarkup(folderByKey.get(key) ?? { key, relativePath: key, exists: false, files: [] }, failures))
    .join("");
}

function renderBrands() {
  if (!state.snapshot) return `<div class="empty-panel"><p class="empty-panel-title">Loading brands…</p></div>`;
  const brands = state.snapshot.brands ?? [];
  if (!brands.length) return `<div class="empty-panel"><p class="empty-panel-title">No brands found</p><p class="empty-panel-copy">Create a brand with default layout settings to get started.</p><button class="button-primary mt-5" type="button" data-action="open-create-brand">Create brand</button></div>`;
  if (!brands.some(brand => brand.id === state.selectedBrandId)) {
    state.selectedBrandId = brands.find(brand => brand.settings)?.id ?? brands[0].id;
  }
  const selected = brands.find(brand => brand.id === state.selectedBrandId);
  if (state.brandBaselineId !== selected.id || !state.brandDirty) {
    state.brandBaselineId = selected.id;
    state.brandBaseline = selected.settings ? canonicalBrandSettings(selected.settings) : null;
    state.brandDirty = false;
  }
  const filteredBrands = filterBrands(brands, state.brandSearchQuery);
  const rows = brandRowsMarkup(filteredBrands, selected.id);
  const list = `<section class="panel brand-list-panel"><div class="brand-panel-header"><div><h3>Brands</h3><p data-brand-result-count>${filteredBrands.length} of ${brands.length} shown</p></div><button class="button-secondary" type="button" data-action="open-create-brand">Create brand</button></div><label class="brand-search"><span class="sr-only">Search brands by name</span><input type="search" data-action="search-brands" value="${escapeHtml(state.brandSearchQuery)}" placeholder="Search brand name…" autocomplete="off"></label><div class="brand-list-scroll" data-brand-list>${rows}</div></section>`;
  if (!selected.settings) {
    return `<div class="brand-workspace">${list}<section class="panel brand-detail-panel"><div class="brand-panel-header"><div><p class="eyebrow">Selected brand</p><h3>${escapeHtml(selected.id)}</h3></div>${badge("Settings invalid", "bad")}</div><div class="brand-detail-scroll"><div class="brand-region-grid">${brandLayoutPanels(selected)}${brandAssetPanels(selected)}</div>${issueMarkup(selected.issue)}</div></section></div>`;
  }
  const settings = selected.settings;
  const detail = `<form class="panel brand-detail-panel" data-form="brand-settings" data-brand-id="${escapeHtml(selected.id)}"><div class="brand-panel-header"><div><p class="eyebrow">Selected brand</p><h3>${escapeHtml(selected.id)}</h3></div><div class="brand-save-actions"><span class="brand-save-status" data-brand-save-status data-state="saved">All changes saved</span><button class="button-primary" data-brand-save-button type="submit" disabled>Save brand</button></div></div><div class="brand-detail-scroll"><p class="brand-save-message hidden" data-brand-save-message role="status"></p><div class="brand-region-grid">${brandLayoutPanels(selected)}${anchoredTextEditor("topic", "Topic", settings.topic)}${anchoredTextEditor("pageNumber", "Page number", settings.pageNumber)}${keywordListEditor(settings.keywordList)}${boardGameEditor(settings.boardGame, settings.answerLine)}${brandAssetPanels(selected)}</div></div></form>`;
  return `<div class="brand-workspace">${list}${detail}</div>`;
}

function renderSettings() {
  if (!state.snapshot) return `<div class="empty-panel"><p class="empty-panel-title">Loading settings…</p></div>`;
  const global = state.snapshot.globalSettings;
  if (!global) return `<section class="panel">${issueMarkup(state.snapshot.globalSettingsIssue)}</section>`;
  const globalForm = `<form class="panel settings-form" data-form="global-settings"><div class="panel-header"><div><h3>Global settings</h3><p>CSV rules, board and fixed output page</p></div><button class="button-primary" type="submit">Save global</button></div><div class="fixed-page-card"><span>Output page</span><strong>${global.page.width} × ${global.page.height} px</strong><p>Fixed for page_layout.png compatibility.</p></div><div class="settings-grid mt-5">${settingInput("board.width", "Board width", global.board.width, "number", "min=\"1\"")}${settingInput("board.height", "Board height", global.board.height, "number", "min=\"1\"")}${settingInput("maximumKeywordLength", "Max Keyword characters", global.maximumKeywordLength, "number", "min=\"1\" max=\"100\"")}</div><p class="global-setting-help">This limit applies to the displayed Keyword, not Word Search Key. Whitespace is ignored. Changing it requires CSV validation again.</p></form>`;
  return `<div class="settings-stack">${globalForm}</div>`;
}

function numberValue(data, name) { return Number(data.get(name)); }
function regionValue(data, name) {
  return {
    rectangle: { x: numberValue(data, `${name}.x`), y: numberValue(data, `${name}.y`), width: numberValue(data, `${name}.width`), height: numberValue(data, `${name}.height`) },
    font: { name: String(data.get(`${name}.fontName`) ?? ""), size: numberValue(data, `${name}.fontSize`), color: String(data.get(`${name}.fontColor`) ?? "") }
  };
}
function anchoredTextValue(data, name) {
  return {
    x: numberValue(data, `${name}.x`), y: numberValue(data, `${name}.y`),
    font: { name: String(data.get(`${name}.fontName`) ?? ""), size: numberValue(data, `${name}.fontSize`), color: String(data.get(`${name}.fontColor`) ?? "") },
    alignment: String(data.get(`${name}.alignment`) ?? "")
  };
}
function keywordListValue(data) {
  const columnY = numberValue(data, "keywordList.columnY");
  return {
    columns: [1, 2, 3, 4].map(index => ({ x: numberValue(data, `keywordList.column${index}X`), y: columnY })),
    stepY: numberValue(data, "keywordList.stepY"),
    font: { name: String(data.get("keywordList.fontName") ?? ""), size: numberValue(data, "keywordList.fontSize"), color: String(data.get("keywordList.fontColor") ?? "") },
    alignment: String(data.get("keywordList.alignment") ?? "")
  };
}
function globalSettingsValue(data) {
  return { board: { width: numberValue(data, "board.width"), height: numberValue(data, "board.height") }, page: fixedPageSize, maximumKeywordLength: numberValue(data, "maximumKeywordLength") };
}
function brandSettingsValue(data) {
  return { topic: anchoredTextValue(data, "topic"), boardGame: regionValue(data, "boardGame"), keywordList: keywordListValue(data), pageNumber: anchoredTextValue(data, "pageNumber"), answerLine: { width: numberValue(data, "answerLine.width"), color: String(data.get("answerLine.color") ?? "") } };
}

function routeMarkup(routeName) {
  const route = routeDefinitions[routeName];
  const body = routeName === "books" ? renderBooks() : routeName === "brands" ? renderBrands() : routeName === "tasks" ? renderTasks() : renderSettings();
  return `<section class="route-page ${routeName === "brands" || routeName === "books" ? "route-page-fill" : ""}"><h2 class="page-heading">${route.heading}</h2><p class="page-copy">${route.copy}</p><div class="route-body">${body}</div></section>`;
}

function activateRoute(routeName, { contentElement, titleElement, navigationItems = [] }) {
  const selectedName = Object.hasOwn(routeDefinitions, routeName) ? routeName : "books";
  state.route = selectedName;
  titleElement.textContent = routeDefinitions[selectedName].title;
  contentElement.innerHTML = routeMarkup(selectedName);
  navigationItems.forEach(item => item.classList.toggle("nav-item-active", item.dataset.route === selectedName));
  return selectedName;
}

function renderStatus(statusElement, status, text) {
  statusElement.dataset.state = status;
  statusElement.textContent = text;
  const dot = globalThis.document?.querySelector?.("#status-dot");
  const pulse = globalThis.document?.querySelector?.("#status-pulse");
  if (!dot || !pulse) return;
  dot.className = `status-dot status-dot-${status}`;
  pulse.className = status === "connecting" ? "status-pulse" : "hidden";
}

function connectToDesktop({ webview, statusElement, createId = () => globalThis.crypto.randomUUID() }) {
  const requestId = createId();
  webview.addEventListener("message", event => {
    let response;
    try { response = parseBridgeData(event.data); }
    catch { renderStatus(statusElement, "error", "Desktop returned an invalid response"); return; }
    if (response?.id !== requestId) return;
    if (response.ok === true && response.type === "pong" && response.data?.status === "ready") {
      renderStatus(statusElement, "ready", `${response.data.name} ${response.data.version}`);
      return;
    }
    renderStatus(statusElement, "error", response?.error?.message ?? "Desktop bridge is unavailable");
  });
  webview.postMessage(JSON.stringify({ id: requestId, type: "ping" }));
  return requestId;
}

function createBridgeClient(webview, createId = () => globalThis.crypto.randomUUID()) {
  const pending = new Map();
  webview.addEventListener("message", event => {
    let response;
    try { response = parseBridgeData(event.data); } catch { return; }
    const callback = pending.get(response?.id);
    if (!callback) return;
    pending.delete(response.id);
    callback(response);
  });
  return {
    send(type, payload, callback) {
      const id = createId();
      pending.set(id, callback);
      webview.postMessage(JSON.stringify(payload === undefined ? { id, type } : { id, type, payload }));
      return id;
    }
  };
}

function initializeNavigation(documentRoot) {
  const contentElement = documentRoot.querySelector("#app-content");
  const titleElement = documentRoot.querySelector("#page-title");
  const navigationItems = [...documentRoot.querySelectorAll("[data-route]")];
  if (!contentElement || !titleElement) return null;
  const render = routeName => activateRoute(routeName, { contentElement, titleElement, navigationItems });
  navigationItems.forEach(item => item.addEventListener("click", () => {
    const routeName = item.dataset.route;
    if (routeName !== state.route) requestNavigation({ kind: "route", value: routeName }, documentRoot, render);
  }));
  render("books");
  return render;
}

function upsertTask(task) {
  state.tasks = [task, ...state.tasks.filter(candidate => candidate.taskId !== task.taskId)];
}

function refreshGlobalTaskStatus(documentRoot) {
  const element = documentRoot.querySelector("#global-task-status");
  if (!element) return;
  const active = state.tasks.find(task => activeTaskStates.has(task.state));
  element.dataset.state = active ? "active" : "idle";
  element.lastElementChild.textContent = active ? `${active.step ?? active.kind} · ${active.state}` : "No active tasks";
}

function refreshBrandFormState(documentRoot) {
  const validationButton = documentRoot.querySelector("[data-brand-validation-button]");
  const validationGuard = documentRoot.querySelector("[data-brand-validation-guard]");
  const validationActive = isBrandValidationActive(state.selectedBrandId);
  const previewActive = isBrandPreviewActive(state.selectedBrandId);
  if (validationButton) {
    validationButton.disabled = validationActive || state.brandDirty || state.brandSaving;
    validationButton.textContent = validationActive ? "Validating…" : "Validate brand";
  }
  if (validationGuard) {
    validationGuard.textContent = state.brandDirty || state.brandSaving
      ? "Save or discard settings changes before validating or drawing a demo."
      : "";
  }
  const previewButton = documentRoot.querySelector("[data-brand-preview-button]");
  if (previewButton) {
    previewButton.disabled = brandPreviewActionDisabled(state.brandDirty, state.brandSaving, previewActive);
    previewButton.textContent = previewActive ? "Drawing…" : "Draw demo";
  }
  const form = documentRoot.querySelector('[data-form="brand-settings"]');
  if (!form) return;
  const status = form.querySelector("[data-brand-save-status]");
  const button = form.querySelector("[data-brand-save-button]");
  const statusName = state.brandSaving ? "saving" : state.brandDirty ? "dirty" : "saved";
  if (status) {
    status.dataset.state = statusName;
    status.textContent = state.brandSaving ? "Saving in background…" : state.brandDirty ? "Unsaved changes" : "All changes saved";
  }
  if (button) {
    button.disabled = state.brandSaving || !state.brandDirty;
    button.textContent = state.brandSaving ? "Saving…" : "Save brand";
  }
  form.querySelectorAll("input").forEach(input => { input.disabled = state.brandSaving; });
  documentRoot.querySelectorAll('[data-action="open-create-brand"]').forEach(button => {
    button.disabled = state.brandDirty || state.brandSaving;
  });
}

function showBrandSaveMessage(documentRoot, message) {
  const element = documentRoot.querySelector("[data-brand-save-message]");
  if (!element) return;
  element.textContent = message ?? "";
  element.classList.toggle("hidden", !message);
}

function showBrandPreviewFeedback(documentRoot, feedback) {
  const element = documentRoot.querySelector("[data-brand-preview-feedback]");
  if (!element) return;
  element.textContent = feedback?.message ?? "";
  element.dataset.state = feedback?.tone ?? "neutral";
  element.classList.toggle("hidden", !feedback);
}

function initializeWorkspace(documentRoot, render) {
  let brandCreating = false;
  documentRoot.querySelector("#create-brand-dialog")?.addEventListener("cancel", event => {
    if (brandCreating) event.preventDefault();
  });
  const renderCurrentRoute = () => { render(state.route); refreshGlobalTaskStatus(documentRoot); };
  const applyTaskUpdate = snapshotChanged => {
    if (shouldRenderForTaskUpdate(state.route, snapshotChanged, state.brandDirty)) render(state.route);
    refreshGlobalTaskStatus(documentRoot);
  };
  const updateBrandSearch = createDebouncedAction(query => {
    state.brandSearchQuery = query;
    const brands = state.snapshot?.brands ?? [];
    const filteredBrands = filterBrands(brands, query);
    const list = documentRoot.querySelector("[data-brand-list]");
    const count = documentRoot.querySelector("[data-brand-result-count]");
    if (list) list.innerHTML = brandRowsMarkup(filteredBrands, state.selectedBrandId);
    if (count) count.textContent = `${filteredBrands.length} of ${brands.length} shown`;
  });
  const updateBookSearch = createDebouncedAction(query => {
    state.bookSearchQuery = query;
    const books = state.snapshot?.books ?? [];
    const filteredBooks = filterBooks(books, query);
    const list = documentRoot.querySelector("[data-book-list]");
    const count = documentRoot.querySelector("[data-book-result-count]");
    if (list) list.innerHTML = bookRowsMarkup(filteredBooks, state.selectedBookId);
    if (count) count.textContent = `${filteredBooks.length} of ${books.length} shown`;
  }, 200);
  const listTasks = () => state.client.send("task.list", undefined, response => {
    if (response.ok) state.tasks = response.data ?? [];
    applyTaskUpdate(false);
  });
  const pollTask = (taskId, onTerminal) => {
    state.client.send("task.get", { taskId }, response => {
      if (!response.ok) {
        onTerminal?.({ state: "Failed", errorMessage: response.error?.message ?? "Task status could not be read." });
        return;
      }
      const task = response.data.task;
      upsertTask(task);
      const snapshotChanged = Boolean(response.data.result);
      if (snapshotChanged) state.snapshot = response.data.result;
      if (activeTaskStates.has(task.state)) {
        applyTaskUpdate(false);
        state.pollTimers.set(taskId, globalThis.setTimeout(() => pollTask(taskId, onTerminal), 250));
      } else {
        state.pollTimers.delete(taskId);
        if (onTerminal) {
          refreshGlobalTaskStatus(documentRoot);
          onTerminal(task, response.data);
        } else {
          applyTaskUpdate(snapshotChanged);
        }
        listTasks();
      }
    });
  };
  const start = (type, payload, handlers = {}) => state.client.send(type, payload, response => {
    if (!response.ok) {
      handlers.onRejected?.(response.error);
      return;
    }
    upsertTask(response.data);
    handlers.onStarted?.(response.data);
    applyTaskUpdate(false);
    pollTask(response.data.taskId, handlers.onTerminal);
  });
  const finishBrandSaveWithError = message => {
    state.brandSaving = false;
    state.pendingNavigation = null;
    refreshBrandFormState(documentRoot);
    showBrandSaveMessage(documentRoot, message || "Brand settings could not be saved.");
  };
  const saveBrandForm = form => {
    if (state.brandSaving || !state.brandDirty) return;
    const settings = brandSettingsValue(new FormData(form));
    state.brandSaving = true;
    showBrandSaveMessage(documentRoot, null);
    refreshBrandFormState(documentRoot);
    start("settings.brand.save", { brandId: form.dataset.brandId, settings }, {
      onRejected: error => finishBrandSaveWithError(error?.message),
      onTerminal: task => {
        state.brandSaving = false;
        if (task.state !== "Completed") {
          finishBrandSaveWithError(task.errorMessage || `Brand save was ${String(task.state).toLocaleLowerCase()}.`);
          return;
        }

        state.brandDirty = false;
        state.brandBaseline = canonicalBrandSettings(settings);
        const destination = state.pendingNavigation;
        state.pendingNavigation = null;
        if (destination) applyNavigation(destination, render);
        else renderCurrentRoute();
      }
    });
  };
  const setCreateBrandState = (creating, errorMessage = null) => {
    brandCreating = creating;
    const dialog = documentRoot.querySelector("#create-brand-dialog");
    const input = dialog?.querySelector('input[name="brandId"]');
    const submit = dialog?.querySelector("[data-create-brand-submit]");
    const error = dialog?.querySelector("[data-create-brand-error]");
    if (input) input.disabled = creating;
    if (submit) {
      submit.disabled = creating;
      submit.textContent = creating ? "Creating…" : "Create brand";
    }
    if (error) {
      error.textContent = errorMessage ?? "";
      error.classList.toggle("hidden", !errorMessage);
    }
  };
  const createBrand = form => {
    const input = form.elements.namedItem("brandId");
    const brandId = String(input?.value ?? "").trim();
    const validationError = validateBrandFolderName(brandId);
    const duplicate = state.snapshot?.brands?.some(brand => brand.id.toLocaleLowerCase() === brandId.toLocaleLowerCase());
    if (validationError || duplicate) {
      setCreateBrandState(false, validationError ?? `Brand '${brandId}' already exists.`);
      return;
    }

    setCreateBrandState(true);
    start("brand.create", { brandId }, {
      onRejected: error => setCreateBrandState(false, error?.message ?? "Brand could not be created."),
      onTerminal: task => {
        if (task.state !== "Completed") {
          setCreateBrandState(false, task.errorMessage || `Brand creation was ${String(task.state).toLocaleLowerCase()}.`);
          return;
        }

        setCreateBrandState(false);
        documentRoot.querySelector("#create-brand-dialog")?.close();
        state.selectedBrandId = brandId;
        state.brandSearchQuery = "";
        state.brandValidationFeedback = null;
        state.brandPreviewFeedback = null;
        state.brandBaselineId = null;
        state.brandBaseline = null;
        state.brandDirty = false;
        renderCurrentRoute();
      }
    });
  };

  documentRoot.addEventListener("click", event => {
    const target = event.target.closest?.("[data-action]");
    if (!target) return;
    if (target.dataset.action === "select-book") {
      state.selectedBookId = target.dataset.bookId;
      state.bookValidationFeedback = null;
      state.bookProcessingFeedback = null;
      renderCurrentRoute();
    }
    if (target.dataset.action === "select-book-tab") {
      state.selectedBookTab = target.dataset.tab === "output" ? "output" : "overview";
      renderCurrentRoute();
    }
    if (target.dataset.action === "select-brand" && target.dataset.brandId !== state.selectedBrandId) {
      requestNavigation({ kind: "brand", value: target.dataset.brandId }, documentRoot, render);
    }
    if (target.dataset.action === "open-create-brand" && !state.brandDirty && !state.brandSaving) {
      const dialog = documentRoot.querySelector("#create-brand-dialog");
      const form = dialog?.querySelector('[data-form="create-brand"]');
      form?.reset();
      setCreateBrandState(false);
      dialog?.showModal();
      form?.elements.namedItem("brandId")?.focus();
    }
    if (target.dataset.action === "cancel-create-brand" && !brandCreating) {
      documentRoot.querySelector("#create-brand-dialog")?.close();
    }
    if (target.dataset.action === "dirty-cancel") {
      state.pendingNavigation = null;
      documentRoot.querySelector("#unsaved-brand-dialog")?.close();
    }
    if (target.dataset.action === "dirty-discard") {
      const destination = state.pendingNavigation;
      state.pendingNavigation = null;
      state.brandDirty = false;
      documentRoot.querySelector("#unsaved-brand-dialog")?.close();
      if (destination) applyNavigation(destination, render);
    }
    if (target.dataset.action === "dirty-save") {
      const form = documentRoot.querySelector('[data-form="brand-settings"]');
      const dialog = documentRoot.querySelector("#unsaved-brand-dialog");
      if (!form?.reportValidity()) {
        state.pendingNavigation = null;
        dialog?.close();
      } else {
        dialog?.close();
        saveBrandForm(form);
      }
    }
    if (target.dataset.action === "refresh") start("workspace.refresh");
    if (target.dataset.action === "list-tasks") listTasks();
    if (target.dataset.action === "validate-book-data") {
      const bookId = target.dataset.bookId;
      state.bookValidationFeedback = null;
      start("book.data.validate", { bookId }, {
        onStarted: () => renderCurrentRoute(),
        onRejected: error => {
          state.bookValidationFeedback = { bookId, failures: [{ message: error?.message ?? "CSV validation could not be started." }] };
          renderCurrentRoute();
        },
        onTerminal: (task, detail) => {
          const failures = detail?.bookDataValidationResult?.failures ?? [];
          state.bookValidationFeedback = {
            bookId,
            failures: task.state === "Completed"
              ? failures
              : [{ message: task.errorMessage || `CSV validation was ${String(task.state).toLocaleLowerCase()}.` }]
          };
          renderCurrentRoute();
        }
      });
    }
    if (target.dataset.action === "process-book") {
      const bookId = target.dataset.bookId;
      const brandId = target.dataset.brandId;
      state.bookProcessingFeedback = null;
      start("book.process", { bookId, brandId }, {
        onStarted: () => renderCurrentRoute(),
        onRejected: error => {
          state.bookProcessingFeedback = { bookId, tone: "error", message: error?.message ?? "Processing could not be started." };
          renderCurrentRoute();
        },
        onTerminal: (task, detail) => {
          const processing = detail?.bookProcessingResult;
          state.bookProcessingFeedback = {
            bookId,
            tone: task.state === "Completed" ? "success" : "error",
            message: task.state === "Completed"
              ? `Published ${processing?.pdfPageCount ?? 0} PDF pages and ${processing?.answers?.length ?? 0} Answer JPEGs.`
              : task.errorMessage || `Processing was ${String(task.state).toLocaleLowerCase()}.`
          };
          renderCurrentRoute();
        }
      });
    }
    if (target.dataset.action === "open-book-output") {
      const bookId = target.dataset.bookId;
      state.client.send("book.output.open", { bookId }, response => {
        state.bookProcessingFeedback = {
          bookId,
          tone: response.ok ? "success" : "error",
          message: response.ok ? "Output folder opened." : response.error?.message ?? "Output folder could not be opened."
        };
        renderCurrentRoute();
      });
    }
    if (target.dataset.action === "validate-brand" && !state.brandDirty && !state.brandSaving) {
      const brandId = target.dataset.brandId;
      state.brandValidationFeedback = null;
      start("brand.validate", { brandId }, {
        onStarted: () => renderCurrentRoute(),
        onRejected: error => {
          state.brandValidationFeedback = {
            brandId,
            failures: [{ message: error?.message ?? "Brand validation could not be started." }]
          };
          renderCurrentRoute();
        },
        onTerminal: (task, detail) => {
          const failures = detail?.brandValidationResult?.failures ?? [];
          state.brandValidationFeedback = {
            brandId,
            failures: task.state === "Completed"
              ? failures
              : [{ message: task.errorMessage || `Brand validation was ${String(task.state).toLocaleLowerCase()}.` }]
          };
          renderCurrentRoute();
        }
      });
    }
    if (target.dataset.action === "draw-brand-preview" &&
        !brandPreviewActionDisabled(state.brandDirty, state.brandSaving, isBrandPreviewActive(target.dataset.brandId))) {
      const brandId = target.dataset.brandId;
      state.brandPreviewFeedback = null;
      start("brand.preview.draw", { brandId }, {
        onStarted: () => renderCurrentRoute(),
        onRejected: error => {
          state.brandPreviewFeedback = {
            brandId,
            tone: "error",
            message: error?.message ?? "Page layout preview could not be started."
          };
          renderCurrentRoute();
        },
        onTerminal: (task, detail) => {
          const fileName = detail?.brandPagePreviewResult?.fileName ?? "page_layout.preview.png";
          state.brandPreviewFeedback = {
            brandId,
            tone: task.state === "Completed" ? "success" : "error",
            message: task.state === "Completed"
              ? `Demo written to ${fileName}.`
              : task.errorMessage || `Page layout preview was ${String(task.state).toLocaleLowerCase()}.`
          };
          renderCurrentRoute();
        }
      });
    }
    if (target.dataset.action === "open-brand-folder") {
      const brandId = target.dataset.brandId;
      state.brandPreviewFeedback = null;
      showBrandPreviewFeedback(documentRoot, null);
      state.client.send("brand.folder.open", { brandId }, response => {
        state.brandPreviewFeedback = {
          brandId,
          tone: response.ok ? "success" : "error",
          message: response.ok ? "Brand folder opened." : response.error?.message ?? "Brand folder could not be opened."
        };
        showBrandPreviewFeedback(documentRoot, state.brandPreviewFeedback);
      });
    }
    if (target.dataset.action === "cancel-task") start("task.cancel", { taskId: target.dataset.taskId });
  });
  documentRoot.addEventListener("input", event => {
    const target = event.target;
    if (target.dataset?.action === "search-brands") {
      updateBrandSearch(target.value);
      return;
    }
    if (target.dataset?.action === "search-books") {
      updateBookSearch(target.value);
      return;
    }
    const form = target.closest?.('[data-form="brand-settings"]');
    if (!form || state.brandSaving) return;
    state.brandDirty = hasBrandSettingsChanged(brandSettingsValue(new FormData(form)), state.brandBaseline);
    state.brandPreviewFeedback = null;
    showBrandSaveMessage(documentRoot, null);
    showBrandPreviewFeedback(documentRoot, null);
    refreshBrandFormState(documentRoot);
  });
  documentRoot.addEventListener("change", event => {
    const target = event.target;
    if (target.dataset?.action !== "assign-brand" || !target.value) return;
    start("book.brand.assign", { bookId: target.dataset.bookId, brandId: target.value });
  });
  documentRoot.addEventListener("submit", event => {
    const form = event.target;
    if (!form.dataset?.form) return;
    event.preventDefault();
    if (form.dataset.form === "create-brand") {
      createBrand(form);
      return;
    }
    const data = new FormData(form);
    if (form.dataset.form === "global-settings") start("settings.global.save", { settings: globalSettingsValue(data) });
    if (form.dataset.form === "brand-settings") saveBrandForm(form);
  });
  start("workspace.refresh");
  listTasks();
}

function initialize() {
  const render = initializeNavigation(document);
  const statusElement = document.querySelector("#desktop-status");
  const webview = globalThis.chrome?.webview;
  if (!statusElement || !render) return;
  if (!webview) { renderStatus(statusElement, "error", "Open from the desktop app"); return; }
  state.client = createBridgeClient(webview);
  state.client.send("ping", undefined, response => {
    if (response.ok && response.type === "pong" && response.data?.status === "ready") {
      renderStatus(statusElement, "ready", `${response.data.name} ${response.data.version}`);
      initializeWorkspace(document, render);
    } else {
      renderStatus(statusElement, "error", response?.error?.message ?? "Desktop bridge is unavailable");
    }
  });
}

if (typeof document !== "undefined") initialize();
const api = { activateRoute, bookDataValidationPresentation, bookOutputPresentation, bookRowsMarkup, bookValidationFailureContext, brandAssetFolderMarkup, brandNavigationDisposition, brandPreviewActionDisabled, brandSettingsValue, brandValidationPresentation, canGenerateWithBrand, connectToDesktop, createBridgeClient, createDebouncedAction, filterBooks, filterBrands, globalSettingsValue, hasBrandSettingsChanged, initializeNavigation, shouldRenderForTaskUpdate, validateBrandFolderName };
globalThis.WordSearchBookUi = api;
if (typeof module !== "undefined" && module.exports) module.exports = api;
