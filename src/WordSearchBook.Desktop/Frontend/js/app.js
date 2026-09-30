function parseBridgeData(data) {
  if (typeof data === "string") {
    return JSON.parse(data);
  }

  return data;
}

function renderStatus(statusElement, state, text) {
  statusElement.dataset.state = state;
  statusElement.textContent = text;

  const dot = globalThis.document?.querySelector?.("#status-dot");
  const pulse = globalThis.document?.querySelector?.("#status-pulse");
  if (!dot || !pulse) return;

  const isReady = state === "ready";
  dot.className = `relative inline-flex h-3 w-3 rounded-full ${isReady ? "bg-emerald-500" : "bg-rose-500"}`;
  pulse.className = isReady
    ? "absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-75"
    : "hidden";
}

function connectToDesktop({ webview, statusElement, createId = () => globalThis.crypto.randomUUID() }) {
  const requestId = createId();

  webview.addEventListener("message", event => {
    let response;
    try {
      response = parseBridgeData(event.data);
    } catch {
      renderStatus(statusElement, "error", "Desktop returned an invalid response");
      return;
    }

    if (response?.id !== requestId) return;

    if (response.ok === true && response.type === "pong" && response.data?.status === "ready") {
      renderStatus(statusElement, "ready", `${response.data.name} ${response.data.version} is ready`);
      return;
    }

    renderStatus(statusElement, "error", response?.error?.message ?? "Desktop bridge is unavailable");
  });

  webview.postMessage(JSON.stringify({ id: requestId, type: "ping" }));
  return requestId;
}

function initialize() {
  const statusElement = document.querySelector("#desktop-status");
  if (!statusElement) return;

  const webview = globalThis.chrome?.webview;
  if (!webview) {
    renderStatus(statusElement, "error", "Open this interface from the desktop application");
    return;
  }

  try {
    connectToDesktop({ webview, statusElement });
  } catch {
    renderStatus(statusElement, "error", "Desktop bridge could not be initialized");
  }
}

if (typeof document !== "undefined") {
  initialize();
}

const api = { connectToDesktop };
globalThis.WordSearchBookUi = api;

if (typeof module !== "undefined" && module.exports) {
  module.exports = api;
}
