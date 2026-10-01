import assert from "node:assert/strict";
import { createRequire } from "node:module";
import test from "node:test";

const require = createRequire(import.meta.url);
const { activateRoute, bookDataValidationPresentation, bookOutputPresentation, bookRowsMarkup, bookValidationFailureContext, brandAssetFolderMarkup, brandLayoutPanels, brandNavigationDisposition, brandPreviewActionDisabled, brandSettingsValue, brandValidationPresentation, canGenerateWithBrand, connectToDesktop, createDebouncedAction, filterBooks, filterBrands, globalSettingsValue, hasBrandSettingsChanged, shouldRenderForTaskUpdate, validateBrandFolderName } = require("./js/app.js");

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
  assert.match(contentElement.innerHTML, /route-page route-page-fill/);
  assert.deepEqual(navigationItems.map(item => item.active), [false, false, true, false]);
});

test("builds a typed global settings payload from form values", () => {
  const values = new Map([
    ["board.width", "20"], ["board.height", "20"], ["maximumKeywordLength", "13"], ["maximumProcessingConcurrency", "4"]
  ]);

  assert.deepEqual(globalSettingsValue(values), {
    board: { width: 20, height: 20 },
    page: { width: 2588, height: 3375 },
    maximumKeywordLength: 13,
    maximumProcessingConcurrency: 4
  });
});

test("builds a complete typed brand settings payload", () => {
  const values = new Map();
  const addFont = (name, font) => {
    values.set(`${name}.fontName`, font);
    values.set(`${name}.fontSize`, "24.5");
    values.set(`${name}.fontColor`, "#112233");
  };
  const addAnchor = (name, start, font, alignment) => {
    values.set(`${name}.x`, String(start));
    values.set(`${name}.y`, String(start + 1));
    values.set(`${name}.alignment`, alignment);
    addFont(name, font);
  };
  const addRegion = (name, start, font) => {
    values.set(`${name}.x`, String(start));
    values.set(`${name}.y`, String(start + 1));
    values.set(`${name}.width`, String(start + 2));
    values.set(`${name}.height`, String(start + 3));
    addFont(name, font);
  };
  addAnchor("topic", 1, "Arial", "Center");
  addRegion("boardGame", 11, "Calibri");
  [1, 2, 3, 4].forEach(index => {
    values.set(`keywordList.column${index}X`, String(index * 100));
  });
  values.set("keywordList.columnY", "2100");
  values.set("keywordList.stepY", "80");
  values.set("keywordList.alignment", "Right");
  addFont("keywordList", "Verdana");
  addAnchor("pageNumber", 31, "Tahoma", "Left");
  values.set("answerLine.width", "2.5");
  values.set("answerLine.color", "#AABBCC");

  const settings = brandSettingsValue(values);

  assert.deepEqual(settings.topic, {
    x: 1, y: 2,
    font: { name: "Arial", size: 24.5, color: "#112233" },
    alignment: "Center"
  });
  assert.deepEqual(settings.boardGame.rectangle, { x: 11, y: 12, width: 13, height: 14 });
  assert.deepEqual(settings.keywordList.columns, [
    { x: 100, y: 2100 }, { x: 200, y: 2100 }, { x: 300, y: 2100 }, { x: 400, y: 2100 }
  ]);
  assert.equal(settings.keywordList.stepY, 80);
  assert.equal(settings.keywordList.alignment, "Right");
  assert.deepEqual(settings.pageNumber, {
    x: 31, y: 32,
    font: { name: "Tahoma", size: 24.5, color: "#112233" },
    alignment: "Left"
  });
  assert.deepEqual(settings.answerLine, { width: 2.5, color: "#AABBCC" });
});

test("filters brands by a trimmed case-insensitive name fragment", () => {
  const brands = [{ id: "Classic-Orange" }, { id: "Modern-Blue" }, { id: "Minimal" }];

  assert.deepEqual(filterBrands(brands, "  ORANGE ").map(brand => brand.id), ["Classic-Orange"]);
  assert.deepEqual(filterBrands(brands, "m").map(brand => brand.id), ["Modern-Blue", "Minimal"]);
  assert.equal(filterBrands(brands, "missing").length, 0);
});

test("filters books by folder name and renders certified status", () => {
  const books = [
    { id: "Animal-Puzzles", topicCount: 10, dataValidation: { status: "Validated" } },
    { id: "Ocean-Life", topicCount: 8, dataValidation: { status: "NeedsValidation" } }
  ];

  assert.deepEqual(filterBooks(books, " ocean ").map(book => book.id), ["Ocean-Life"]);
  const markup = bookRowsMarkup(books, "Animal-Puzzles");
  assert.match(markup, /Animal-Puzzles/);
  assert.match(markup, /Validated/);
  assert.match(markup, /Needs validation/);
  assert.match(markup, /book-row-active/);
});

test("maps CSV and output lifecycle states to stable badges", () => {
  assert.deepEqual(bookDataValidationPresentation({ status: "Invalid" }), { label: "Invalid", tone: "bad" });
  assert.deepEqual(bookDataValidationPresentation({ status: "Validated" }), { label: "Validated", tone: "good" });
  assert.deepEqual(bookOutputPresentation({ status: "Ready" }), { label: "Ready", tone: "good" });
  assert.deepEqual(bookOutputPresentation({ status: "Stale" }), { label: "Stale", tone: "warn" });
});

test("formats CSV validation context with row and topic", () => {
  assert.equal(bookValidationFailureContext({ sourceRow: 12, topic: "BREATHING" }), "Row 12 · BREATHING");
  assert.equal(bookValidationFailureContext({ topic: "BREATHING" }), "BREATHING");
  assert.equal(bookValidationFailureContext({}), "CSV");
});

test("debounces brand search and applies only the latest query", () => {
  const scheduled = new Map();
  let nextId = 0;
  const timers = {
    setTimeout(callback, delay) { const id = ++nextId; scheduled.set(id, { callback, delay }); return id; },
    clearTimeout(id) { scheduled.delete(id); }
  };
  const values = [];
  const search = createDebouncedAction(value => values.push(value), 250, timers);

  search("a");
  search("ab");
  search("abc");

  assert.equal(scheduled.size, 1);
  const pending = [...scheduled.values()][0];
  assert.equal(pending.delay, 250);
  pending.callback();
  assert.deepEqual(values, ["abc"]);
});

test("does not redraw the Brands route for task-only polling updates", () => {
  assert.equal(shouldRenderForTaskUpdate("brands", false), false);
  assert.equal(shouldRenderForTaskUpdate("brands", true), true);
  assert.equal(shouldRenderForTaskUpdate("brands", true, true), false);
  assert.equal(shouldRenderForTaskUpdate("tasks", false), true);
  assert.equal(shouldRenderForTaskUpdate("books", false), false);
  assert.equal(shouldRenderForTaskUpdate("books", true), true);
});

test("detects a changed brand draft against its saved baseline", () => {
  const settings = {
    topic: { rectangle: { x: 1, y: 2, width: 3, height: 4 }, font: { name: "Arial", size: 12, color: "#000000" } },
    boardGame: {}, keywordList: {}, pageNumber: {}, answerLine: { width: 2, color: "#FF0000" }
  };
  const baseline = JSON.stringify(settings);

  assert.equal(hasBrandSettingsChanged(settings, baseline), false);
  assert.equal(hasBrandSettingsChanged({ ...settings, answerLine: { ...settings.answerLine, width: 3 } }, baseline), true);
});

test("guards navigation while a brand is dirty or saving", () => {
  assert.equal(brandNavigationDisposition("brands", true, false), "prompt");
  assert.equal(brandNavigationDisposition("brands", true, true), "blocked");
  assert.equal(brandNavigationDisposition("brands", false, false), "apply");
  assert.equal(brandNavigationDisposition("books", true, false), "apply");
});

test("validates brand names as safe Windows folder names", () => {
  assert.equal(validateBrandFolderName("new-brand"), null);
  assert.equal(validateBrandFolderName("Brand 2026"), null);
  assert.match(validateBrandFolderName("../escape"), /valid Windows folder name/);
  assert.match(validateBrandFolderName("CON"), /reserved/);
  assert.match(validateBrandFolderName("trailing."), /valid Windows folder name/);
  assert.match(validateBrandFolderName(""), /Enter/);
});

test("maps all brand validation states to stable UI badges", () => {
  assert.deepEqual(brandValidationPresentation({ status: "NotValidated" }), { label: "Not validated", tone: "neutral" });
  assert.deepEqual(brandValidationPresentation({ status: "Validated" }), { label: "Validated", tone: "good" });
  assert.deepEqual(brandValidationPresentation({ status: "NeedsValidation" }), { label: "Needs validation", tone: "warn" });
});

test("allows generation only for a certified brand", () => {
  assert.equal(canGenerateWithBrand({ validation: { status: "Validated" } }), true);
  assert.equal(canGenerateWithBrand({ validation: { status: "NeedsValidation" } }), false);
  assert.equal(canGenerateWithBrand({ validation: { status: "NotValidated" } }), false);
  assert.equal(canGenerateWithBrand(null), false);
});

test("renders optional brand folders with empty and per-file validation states", () => {
  const empty = brandAssetFolderMarkup({ key: "front", relativePath: "front", exists: true, files: [] });
  assert.match(empty, /Front PDF pages/);
  assert.match(empty, /No images — optional/);
  assert.match(empty, /0 images/);

  const populated = brandAssetFolderMarkup({
    key: "back",
    relativePath: "back",
    exists: true,
    files: [
      { name: "closing.jpg", relativePath: "back/closing.jpg", extension: ".jpg", status: "NeedsValidation" },
      { name: "final.png", relativePath: "back/final.png", extension: ".png", status: "Validated" }
    ]
  }, [{ target: "back/closing.jpg", message: "Wrong size" }]);
  assert.match(populated, /Back PDF pages/);
  assert.match(populated, /2 images/);
  assert.match(populated, /closing\.jpg/);
  assert.match(populated, /Invalid/);
  assert.match(populated, /Wrong size/);
  assert.match(populated, /final\.png/);
  assert.match(populated, /Validated/);
});

test("describes both required page layout layers and puzzle-only foreground", () => {
  const markup = brandLayoutPanels({
    id: "demo",
    validation: { status: "Validated", fingerprint: "sha256:abcdef", validatedAtUtc: "2026-10-01T00:00:00Z" }
  });

  assert.match(markup, /page_layout\.png/);
  assert.match(markup, /front_layout\.png/);
  assert.match(markup, /Foreground · required/);
  assert.match(markup, /transparent foreground last/);
  assert.match(markup, /Answer pages do not use it/);
});

test("disables preview drawing for unsaved, saving, or active brand state", () => {
  assert.equal(brandPreviewActionDisabled(false, false, false), false);
  assert.equal(brandPreviewActionDisabled(true, false, false), true);
  assert.equal(brandPreviewActionDisabled(false, true, false), true);
  assert.equal(brandPreviewActionDisabled(false, false, true), true);
});
