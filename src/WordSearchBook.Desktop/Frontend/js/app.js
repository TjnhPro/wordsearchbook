const routeDefinitions = {
  books: { title: "Books", heading: "Word-search books", copy: "Inspect input, choose a brand, and generate board caches." },
  tasks: { title: "Tasks", heading: "Background tasks", copy: "Queued and running work stays isolated from the desktop UI thread." },
  settings: { title: "Settings", heading: "Workspace settings", copy: "Review and edit global and brand layout settings." }
};
const activeTaskStates = new Set(["Queued", "Running", "Cancelling"]);
const state = { route: "books", snapshot: null, tasks: [], selectedBookId: null, client: null, pollTimers: new Map() };

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>'"]/g, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", "\"": "&quot;" })[character]);
}

function parseBridgeData(data) { return typeof data === "string" ? JSON.parse(data) : data; }
function issueMarkup(issue) { return issue ? `<p class="issue-text">${escapeHtml(issue.message)}</p>` : ""; }
function badge(label, tone = "neutral") { return `<span class="badge badge-${tone}">${escapeHtml(label)}</span>`; }

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

function renderSettings() {
  return `<div class="empty-panel"><p class="empty-panel-title">Settings editor is next</p><p class="empty-panel-copy">The workspace snapshot already validates global and existing brand settings. Editing and atomic save are added in Phase 4.</p></div>`;
}

function routeMarkup(routeName) {
  const route = routeDefinitions[routeName];
  const body = routeName === "books" ? renderBooks() : routeName === "tasks" ? renderTasks() : renderSettings();
  return `<section><h2 class="page-heading">${route.heading}</h2><p class="page-copy">${route.copy}</p><div class="route-body">${body}</div></section>`;
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
  navigationItems.forEach(item => item.addEventListener("click", () => render(item.dataset.route)));
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

function initializeWorkspace(documentRoot, render) {
  const applyAndRender = () => { render(state.route); refreshGlobalTaskStatus(documentRoot); };
  const listTasks = () => state.client.send("task.list", undefined, response => {
    if (response.ok) state.tasks = response.data ?? [];
    applyAndRender();
  });
  const pollTask = taskId => {
    state.client.send("task.get", { taskId }, response => {
      if (!response.ok) return;
      const task = response.data.task;
      upsertTask(task);
      if (response.data.result) state.snapshot = response.data.result;
      applyAndRender();
      if (activeTaskStates.has(task.state)) {
        state.pollTimers.set(taskId, globalThis.setTimeout(() => pollTask(taskId), 250));
      } else {
        state.pollTimers.delete(taskId);
        listTasks();
      }
    });
  };
  const start = (type, payload) => state.client.send(type, payload, response => {
    if (!response.ok) return;
    upsertTask(response.data);
    applyAndRender();
    pollTask(response.data.taskId);
  });

  documentRoot.addEventListener("click", event => {
    const target = event.target.closest?.("[data-action]");
    if (!target) return;
    if (target.dataset.action === "select-book") { state.selectedBookId = target.dataset.bookId; applyAndRender(); }
    if (target.dataset.action === "refresh") start("workspace.refresh");
    if (target.dataset.action === "list-tasks") listTasks();
    if (target.dataset.action === "generate") start("book.generate", { bookId: target.dataset.bookId, brandId: target.dataset.brandId });
    if (target.dataset.action === "cancel-task") start("task.cancel", { taskId: target.dataset.taskId });
  });
  documentRoot.addEventListener("change", event => {
    const target = event.target;
    if (target.dataset?.action !== "assign-brand" || !target.value) return;
    start("book.brand.assign", { bookId: target.dataset.bookId, brandId: target.value });
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
const api = { activateRoute, connectToDesktop, createBridgeClient, initializeNavigation };
globalThis.WordSearchBookUi = api;
if (typeof module !== "undefined" && module.exports) module.exports = api;
