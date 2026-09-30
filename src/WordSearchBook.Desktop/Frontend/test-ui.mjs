import assert from "node:assert/strict";
import { createRequire } from "node:module";
import test from "node:test";

const require = createRequire(import.meta.url);
const { connectToDesktop } = require("./js/app.js");

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
  assert.equal(harness.statusElement.textContent, "Word Search Book 0.1.0 is ready");
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
