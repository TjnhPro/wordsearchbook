const routeDefinitions = {
  books: { title: "Books", heading: "Word-search books", copy: "Inspect input, choose a brand, and generate board caches." },
  brands: { title: "Brands", heading: "Brand layouts", copy: "Review and edit the layout settings for an existing brand." },
  tasks: { title: "Tasks", heading: "Background tasks", copy: "Queued and running work stays isolated from the desktop UI thread." },
  settings: { title: "Settings", heading: "Workspace settings", copy: "Review and edit global board and page settings." }
};
const activeTaskStates = new Set(["Queued", "Running", "Cancelling"]);
const state = {
  route: "books", snapshot: null, tasks: [], selectedBookId: null, selectedBrandId: null,
  brandSearchQuery: "", brandBaselineId: null, brandBaseline: null, brandDirty: false,
  brandSaving: false, pendingNavigation: null, client: null, pollTimers: new Map()
};

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>'"]/g, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", "\"": "&quot;" })[character]);
}

function parseBridgeData(data) { return typeof data === "string" ? JSON.parse(data) : data; }
function issueMarkup(issue) { return issue ? `<p class="issue-text">${escapeHtml(issue.message)}</p>` : ""; }
function badge(label, tone = "neutral") { return `<span class="badge badge-${tone}">${escapeHtml(label)}</span>`; }
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
  return routeName !== "brands" || (snapshotChanged && !brandDirty);
}
function brandNavigationDisposition(routeName, brandDirty, brandSaving) {
  if (brandSaving) return "blocked";
  return routeName === "brands" && brandDirty ? "prompt" : "apply";
}
function applyNavigation(destination, render) {
  if (destination.kind === "brand") {
    state.selectedBrandId = destination.value;
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

function renderBooks() {
  if (!state.snapshot) return `<div class="empty-panel"><p class="empty-panel-title">Scanning workspace…</p><p class="empty-panel-copy">Books and brands are loaded by a background task.</p></div>`;
  const books = state.snapshot.books ?? [];
  const brands = state.snapshot.brands ?? [];
  if (!books.length) return `<div class="empty-panel"><p class="empty-panel-title">No books found</p><p class="empty-panel-copy">Add input/{book}/data.csv below ${escapeHtml(state.snapshot.rootPath)} and refresh.</p><button class="button-secondary mt-5" data-action="refresh">Refresh</button></div>`;

  if (!books.some(book => book.id === state.selectedBookId)) state.selectedBookId = books[0].id;
  const selected = books.find(book => book.id === state.selectedBookId);
  const validBrands = brands.filter(brand => !brand.issue);
  const selectedBrand = selected.selectedBrandId ?? "";
  const generationActive = state.tasks.some(task => task.kind === "BookGeneration" && task.subject === selected.id && activeTaskStates.has(task.state));
  const options = [`<option value="">Choose a brand</option>`, ...validBrands.map(brand => `<option value="${escapeHtml(brand.id)}" ${brand.id === selectedBrand ? "selected" : ""}>${escapeHtml(brand.id)}</option>`)].join("");
  const cached = (selected.cachedBrandIds ?? []).length ? selected.cachedBrandIds.map(id => badge(`Cached: ${id}`, "good")).join("") : badge("Not generated");
  const list = books.map(book => `<button class="book-row ${book.id === selected.id ? "book-row-active" : ""}" data-action="select-book" data-book-id="${escapeHtml(book.id)}"><span><strong>${escapeHtml(book.id)}</strong><small>${book.issue ? "Input needs attention" : `${book.topicCount} topic${book.topicCount === 1 ? "" : "s"}`}</small></span>${book.issue ? badge("Invalid", "bad") : badge("Ready", "good")}</button>`).join("");

  return `<div class="master-detail"><section class="panel list-panel"><div class="panel-header"><div><h3>Books</h3><p>${books.length} discovered</p></div><button class="button-secondary" data-action="refresh">Refresh</button></div><div class="book-list">${list}</div></section><section class="panel detail-panel"><div class="detail-heading"><div><p class="eyebrow">Selected book</p><h3>${escapeHtml(selected.id)}</h3></div>${selected.issue ? badge("Invalid input", "bad") : badge("Ready", "good")}</div>${issueMarkup(selected.issue)}<dl class="summary-grid"><div><dt>Topics</dt><dd>${selected.topicCount}</dd></div><div><dt>Cache</dt><dd class="badge-row">${cached}</dd></div></dl><label class="field"><span>Brand</span><select data-action="assign-brand" data-book-id="${escapeHtml(selected.id)}" ${selected.issue ? "disabled" : ""}>${options}</select></label><div class="action-row"><button class="button-primary" data-action="generate" data-book-id="${escapeHtml(selected.id)}" data-brand-id="${escapeHtml(selectedBrand)}" ${selected.issue || !selectedBrand || generationActive ? "disabled" : ""}>${generationActive ? "Generating…" : "Generate boards"}</button></div><div class="preview-placeholder"><strong>Preview</strong><p>Puzzle and answer preview will be designed in a later UI phase.</p></div></section></div>`;
}

function renderTasks() {
  if (!state.tasks.length) return `<div class="empty-panel"><p class="empty-panel-title">No task history</p><p class="empty-panel-copy">Workspace refresh, generation, and settings work will appear here.</p></div>`;
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

function regionEditor(name, label, region) {
  const rectangle = region.rectangle;
  const font = region.font;
  return `<fieldset class="settings-group brand-region-card"><legend>${label}</legend><div class="brand-region-fields">${settingInput(`${name}.x`, "X", rectangle.x)}${settingInput(`${name}.y`, "Y", rectangle.y)}${settingInput(`${name}.width`, "Width", rectangle.width, "number", "min=\"1\"")}${settingInput(`${name}.height`, "Height", rectangle.height, "number", "min=\"1\"")}${settingInput(`${name}.fontName`, "Font", font.name, "text")}${settingInput(`${name}.fontSize`, "Font size", font.size, "number", "min=\"0.1\" step=\"0.1\"")}${settingInput(`${name}.fontColor`, "Font color", font.color, "text", "pattern=\"#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?\"")}</div></fieldset>`;
}

function brandRowsMarkup(brands, selectedBrandId) {
  if (!brands.length) return `<div class="brand-list-empty"><strong>No matching brands</strong><p>Try a different brand name.</p></div>`;
  return brands.map(brand => `<button class="brand-row ${brand.id === selectedBrandId ? "brand-row-active" : ""}" type="button" data-action="select-brand" data-brand-id="${escapeHtml(brand.id)}"><span><strong>${escapeHtml(brand.id)}</strong><small>${brand.settings ? "Layout available" : "Settings need attention"}</small></span>${brand.settings ? badge("Ready", "good") : badge("Invalid", "bad")}</button>`).join("");
}

function renderBrands() {
  if (!state.snapshot) return `<div class="empty-panel"><p class="empty-panel-title">Loading brands…</p></div>`;
  const brands = state.snapshot.brands ?? [];
  if (!brands.length) return `<div class="empty-panel"><p class="empty-panel-title">No brands found</p><p class="empty-panel-copy">Add brands/{brand}/settings.json below the application root and refresh the workspace.</p></div>`;
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
  const list = `<section class="panel brand-list-panel"><div class="brand-panel-header"><div><h3>Brands</h3><p data-brand-result-count>${filteredBrands.length} of ${brands.length} shown</p></div></div><label class="brand-search"><span class="sr-only">Search brands by name</span><input type="search" data-action="search-brands" value="${escapeHtml(state.brandSearchQuery)}" placeholder="Search brand name…" autocomplete="off"></label><div class="brand-list-scroll" data-brand-list>${rows}</div></section>`;
  if (!selected.settings) {
    return `<div class="brand-workspace">${list}<section class="panel brand-detail-panel"><div class="brand-panel-header"><div><p class="eyebrow">Selected brand</p><h3>${escapeHtml(selected.id)}</h3></div>${badge("Invalid", "bad")}</div><div class="brand-detail-scroll">${issueMarkup(selected.issue)}</div></section></div>`;
  }
  const settings = selected.settings;
  const detail = `<form class="panel brand-detail-panel" data-form="brand-settings" data-brand-id="${escapeHtml(selected.id)}"><div class="brand-panel-header"><div><p class="eyebrow">Selected brand</p><h3>${escapeHtml(selected.id)}</h3></div><div class="brand-save-actions"><span class="brand-save-status" data-brand-save-status data-state="saved">All changes saved</span><button class="button-primary" data-brand-save-button type="submit" disabled>Save brand</button></div></div><div class="brand-detail-scroll"><p class="brand-save-message hidden" data-brand-save-message role="status"></p><div class="brand-region-grid">${regionEditor("topic", "Topic", settings.topic)}${regionEditor("boardGame", "Board game", settings.boardGame)}${regionEditor("keywordList", "Keyword list", settings.keywordList)}${regionEditor("pageNumber", "Page number", settings.pageNumber)}</div><details class="answer-styling"><summary>Answer styling</summary><div class="settings-grid mt-5">${settingInput("answerLine.width", "Line width", settings.answerLine.width, "number", "min=\"0.1\" step=\"0.1\"")}${settingInput("answerLine.color", "Line color", settings.answerLine.color, "text", "pattern=\"#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?\"")}</div></details></div></form>`;
  return `<div class="brand-workspace">${list}${detail}</div>`;
}

function renderSettings() {
  if (!state.snapshot) return `<div class="empty-panel"><p class="empty-panel-title">Loading settings…</p></div>`;
  const global = state.snapshot.globalSettings;
  if (!global) return `<section class="panel">${issueMarkup(state.snapshot.globalSettingsIssue)}</section>`;
  const globalForm = `<form class="panel settings-form" data-form="global-settings"><div class="panel-header"><div><h3>Global settings</h3><p>Board and output page</p></div><button class="button-primary" type="submit">Save global</button></div><div class="settings-grid mt-5">${settingInput("board.width", "Board width", global.board.width, "number", "min=\"1\"")}${settingInput("board.height", "Board height", global.board.height, "number", "min=\"1\"")}${settingInput("page.width", "Page width", global.page.width, "number", "min=\"1\"")}${settingInput("page.height", "Page height", global.page.height, "number", "min=\"1\"")}</div></form>`;
  return `<div class="settings-stack">${globalForm}</div>`;
}

function numberValue(data, name) { return Number(data.get(name)); }
function regionValue(data, name) {
  return {
    rectangle: { x: numberValue(data, `${name}.x`), y: numberValue(data, `${name}.y`), width: numberValue(data, `${name}.width`), height: numberValue(data, `${name}.height`) },
    font: { name: String(data.get(`${name}.fontName`) ?? ""), size: numberValue(data, `${name}.fontSize`), color: String(data.get(`${name}.fontColor`) ?? "") }
  };
}
function globalSettingsValue(data) {
  return { board: { width: numberValue(data, "board.width"), height: numberValue(data, "board.height") }, page: { width: numberValue(data, "page.width"), height: numberValue(data, "page.height") } };
}
function brandSettingsValue(data) {
  return { topic: regionValue(data, "topic"), boardGame: regionValue(data, "boardGame"), keywordList: regionValue(data, "keywordList"), pageNumber: regionValue(data, "pageNumber"), answerLine: { width: numberValue(data, "answerLine.width"), color: String(data.get("answerLine.color") ?? "") } };
}

function routeMarkup(routeName) {
  const route = routeDefinitions[routeName];
  const body = routeName === "books" ? renderBooks() : routeName === "brands" ? renderBrands() : routeName === "tasks" ? renderTasks() : renderSettings();
  return `<section class="route-page ${routeName === "brands" ? "route-page-fill" : ""}"><h2 class="page-heading">${route.heading}</h2><p class="page-copy">${route.copy}</p><div class="route-body">${body}</div></section>`;
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
}

function showBrandSaveMessage(documentRoot, message) {
  const element = documentRoot.querySelector("[data-brand-save-message]");
  if (!element) return;
  element.textContent = message ?? "";
  element.classList.toggle("hidden", !message);
}

function initializeWorkspace(documentRoot, render) {
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
          onTerminal(task);
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

  documentRoot.addEventListener("click", event => {
    const target = event.target.closest?.("[data-action]");
    if (!target) return;
    if (target.dataset.action === "select-book") { state.selectedBookId = target.dataset.bookId; renderCurrentRoute(); }
    if (target.dataset.action === "select-brand" && target.dataset.brandId !== state.selectedBrandId) {
      requestNavigation({ kind: "brand", value: target.dataset.brandId }, documentRoot, render);
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
    if (target.dataset.action === "generate") start("book.generate", { bookId: target.dataset.bookId, brandId: target.dataset.brandId });
    if (target.dataset.action === "cancel-task") start("task.cancel", { taskId: target.dataset.taskId });
  });
  documentRoot.addEventListener("input", event => {
    const target = event.target;
    if (target.dataset?.action === "search-brands") {
      updateBrandSearch(target.value);
      return;
    }
    const form = target.closest?.('[data-form="brand-settings"]');
    if (!form || state.brandSaving) return;
    state.brandDirty = hasBrandSettingsChanged(brandSettingsValue(new FormData(form)), state.brandBaseline);
    showBrandSaveMessage(documentRoot, null);
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
const api = { activateRoute, brandNavigationDisposition, brandSettingsValue, connectToDesktop, createBridgeClient, createDebouncedAction, filterBrands, globalSettingsValue, hasBrandSettingsChanged, initializeNavigation, shouldRenderForTaskUpdate };
globalThis.WordSearchBookUi = api;
if (typeof module !== "undefined" && module.exports) module.exports = api;
