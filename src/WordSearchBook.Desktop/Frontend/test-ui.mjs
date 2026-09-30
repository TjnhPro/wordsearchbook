import assert from "node:assert/strict";
import { createRequire } from "node:module";
import test from "node:test";

const require = createRequire(import.meta.url);
const { activateRoute, connectToDesktop, globalSettingsValue } = require("./js/app.js");

function createHarness() {
  let messageHandler;
  let postedMessage;
  const webview = {
    addEventListener(type, handler) {
      assert.equal(type, "message");
      messageHandler = handler;
    },
    postMessage(message) {
      postedMessage = message;
    }
  };
  const statusElement = { dataset: {}, textContent: "" };

  return {
    webview,
    statusElement,
    getPostedMessage: () => postedMessage,
    receive: data => messageHandler({ data })
  };
}

test("sends a typed ping and renders the ready response", () => {
  const harness = createHarness();

  connectToDesktop({
    webview: harness.webview,
    statusElement: harness.statusElement,
    createId: () => "request-1"
  });

  assert.deepEqual(JSON.parse(harness.getPostedMessage()), { id: "request-1", type: "ping" });

  harness.receive({
    id: "request-1",
    type: "pong",
    ok: true,
    data: { name: "Word Search Book", version: "0.1.0", status: "ready" }
  });

  assert.equal(harness.statusElement.dataset.state, "ready");
  assert.equal(harness.statusElement.textContent, "Word Search Book 0.1.0");
});

test("renders a bridge error response", () => {
  const harness = createHarness();

  connectToDesktop({
    webview: harness.webview,
    statusElement: harness.statusElement,
    createId: () => "request-2"
  });

  harness.receive({
    id: "request-2",
    type: "error",
    ok: false,
    error: { code: "unsupported_message", message: "Unsupported" }
  });

  assert.equal(harness.statusElement.dataset.state, "error");
  assert.equal(harness.statusElement.textContent, "Unsupported");
});

test("renders invalid serialized responses without throwing", () => {
  const harness = createHarness();

  connectToDesktop({
    webview: harness.webview,
    statusElement: harness.statusElement,
    createId: () => "request-3"
  });

  harness.receive("not-json");

  assert.equal(harness.statusElement.dataset.state, "error");
  assert.equal(harness.statusElement.textContent, "Desktop returned an invalid response");
});

test("activates sidebar routes and renders their detail shell", () => {
  const navigationItems = ["books", "brands", "tasks", "settings"].map(route => ({
    dataset: { route }, active: false,
    classList: { owner: null, toggle(_name, active) { this.owner.active = active; } }
  }));
  navigationItems.forEach(item => { item.classList.owner = item; });
  const contentElement = { innerHTML: "" };
  const titleElement = { textContent: "" };

  const route = activateRoute("tasks", { contentElement, titleElement, navigationItems });

  assert.equal(route, "tasks");
  assert.equal(titleElement.textContent, "Tasks");
  assert.match(contentElement.innerHTML, /Background tasks/);
  assert.deepEqual(navigationItems.map(item => item.active), [false, false, true, false]);
});

test("builds a typed global settings payload from form values", () => {
  const values = new Map([
    ["board.width", "20"], ["board.height", "20"],
    ["page.width", "2400"], ["page.height", "3000"]
  ]);

  assert.deepEqual(globalSettingsValue(values), {
    board: { width: 20, height: 20 },
    page: { width: 2400, height: 3000 }
  });
});
