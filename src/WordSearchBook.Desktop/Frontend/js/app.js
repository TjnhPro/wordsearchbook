const routes = {
  books: { title: "Books", heading: "Word-search books", copy: "Select a book to inspect its input, choose a brand, and start generation.", emptyTitle: "Workspace is waiting", emptyCopy: "Book discovery will be connected to the desktop background worker in the next phase." },
  tasks: { title: "Tasks", heading: "Background tasks", copy: "Track queued, running, completed, cancelled, and failed work without blocking the interface.", emptyTitle: "No task history", emptyCopy: "Background task activity will appear here after the desktop task manager is connected." },
  settings: { title: "Settings", heading: "Workspace settings", copy: "Review global page settings and edit the existing brand layouts used for generation.", emptyTitle: "Settings are not loaded", emptyCopy: "Settings will be loaded from the executable root through a background worker." }
};

function parseBridgeData(data) { return typeof data === "string" ? JSON.parse(data) : data; }
function routeMarkup(route) { return `<section><h2 class="page-heading">${route.heading}</h2><p class="page-copy">${route.copy}</p><div class="empty-panel"><p class="empty-panel-title">${route.emptyTitle}</p><p class="empty-panel-copy">${route.emptyCopy}</p></div></section>`; }

function activateRoute(routeName, { contentElement, titleElement, navigationItems = [] }) {
  const selectedName = Object.hasOwn(routes, routeName) ? routeName : "books";
  const route = routes[selectedName];
  titleElement.textContent = route.title;
  contentElement.innerHTML = routeMarkup(route);
  navigationItems.forEach(item => item.classList.toggle("nav-item-active", item.dataset.route === selectedName));
  return selectedName;
}

function renderStatus(statusElement, state, text) {
  statusElement.dataset.state = state;
  statusElement.textContent = text;
  const dot = globalThis.document?.querySelector?.("#status-dot");
  const pulse = globalThis.document?.querySelector?.("#status-pulse");
  if (!dot || !pulse) return;
  dot.className = `status-dot status-dot-${state}`;
  pulse.className = state === "connecting" ? "status-pulse" : "hidden";
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

function initializeNavigation(documentRoot) {
  const contentElement = documentRoot.querySelector("#app-content");
  const titleElement = documentRoot.querySelector("#page-title");
  const navigationItems = [...documentRoot.querySelectorAll("[data-route]")];
  if (!contentElement || !titleElement) return;
  const render = routeName => activateRoute(routeName, { contentElement, titleElement, navigationItems });
  navigationItems.forEach(item => item.addEventListener("click", () => render(item.dataset.route)));
  render("books");
}

function initialize() {
  initializeNavigation(document);
  const statusElement = document.querySelector("#desktop-status");
  if (!statusElement) return;
  const webview = globalThis.chrome?.webview;
  if (!webview) { renderStatus(statusElement, "error", "Open from the desktop app"); return; }
  try { connectToDesktop({ webview, statusElement }); }
  catch { renderStatus(statusElement, "error", "Desktop bridge could not be initialized"); }
}

if (typeof document !== "undefined") initialize();
const api = { activateRoute, connectToDesktop, initializeNavigation };
globalThis.WordSearchBookUi = api;
if (typeof module !== "undefined" && module.exports) module.exports = api;
