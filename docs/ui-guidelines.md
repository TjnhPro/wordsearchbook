# Desktop UI guidelines

## Design viewport

- Design and review the WebView UI against a `1600x900` content baseline.
- `MainWindow` starts at and cannot be resized below `1610x910`. The additional space keeps the `1600x900` UI from touching or being hidden by the window boundary.
- Do not reduce `Width`, `Height`, `MinWidth`, or `MinHeight` below `1610x910` unless the complete desktop layout is redesigned and verified at the new baseline.
- Keep primary navigation on the left and the selected workspace detail on the right.
- Long-running and file-system operations must continue to use the background task queue so rendering and navigation remain responsive.
- Close immediately without a dialog when no background task is active. When work is active, use one modal dialog with confirmation and a compact loading state.
- After confirmation, keep the loading dialog visible for at least 500ms and until active tasks stop or the shared five-second timeout expires. Close the owner window and its dialog together, and do not allow the dialog to be dismissed while cancellation is in progress.

## Brands workspace

- Use a `3/9` master-detail grid: Brand List takes 25% on the left and Brand Detail takes 75% on the right.
- Keep the search box fixed above the Brand List. The list owns its vertical scrollbar and must not increase the page height.
- Filter brand names case-insensitively after a `250ms` debounce. Update only the list rows; never replace the complete route while the user is typing.
- Use one two-column panel grid beginning with Page layouts/Puzzle page preview, followed by Topic/Quote and the remaining text, board, and asset panels. Keep every panel background white.
- Keep Answer styling inside the Board game panel below its placement and font settings.
- Show the output page as fixed `2588x3375`; do not expose editable page width or height controls.
- Each brand owns required `page_layout.png` and `front_layout.png` files. Both must be PNG exactly `2588x3375`; Create Brand creates a white base and a fully transparent 300-DPI foreground.
- Puzzle pages draw base, board, text and then foreground in that order. Answer pages never use `front_layout.png`; all layers stay pixel-for-pixel without resizing.
- Creating a valid default PNG does not certify it. Show `Not validated`, `Validated`, or `Needs validation` separately from settings health.
- Create Brand also creates empty `front/` and `back/` folders. Existing Brands may omit either folder; missing and empty folders are valid optional states.
- Front PDF pages and Back PDF pages panels list only direct `.png`, `.jpg`, or `.jpeg` files, use their own bounded list scroll, and show per-file certificate/failure status. Nested and unsupported files remain invisible.
- Deep image validation runs only when the user selects **Validate brand**. Workspace load and refresh may enumerate tracked files, read `brand.validation.json` and compare metadata, but must not decode images.
- Keep the Page Layout card visible even when Brand or global settings are invalid. Disable validation while the current Brand form is dirty or saving.
- Keep a **Puzzle page preview** panel beside the Page Layouts card. **Draw demo** uses both required layouts plus saved settings and writes `page_layout.preview.png` beside the source layouts through the background queue.
- Disable **Draw demo** while the selected Brand has unsaved edits, is saving, or already has an active preview task. **Open folder** remains available and must use the desktop-derived Brand path rather than a path supplied by the frontend.
- Preview generation is independent from the validation certificate and must never modify either layout source or `brand.validation.json`.
- Books may list any Brand with readable settings, but **Process** stays disabled until both the CSV certificate and shared Brand certificate for required layouts and optional Front/Back PDF pages are `Validated`, and the Brand has saved Quote settings.
- Topic and Page number editors use X/Y anchors plus `Left`, `Center`, or `Right` alignment. X is the selected horizontal anchor; Y is always the text top edge.
- Quote uses X/Y/Width/Height plus font name, size, and color. It is always centered horizontally and vertically, preserves casing/punctuation/Unicode, and wraps at spaces only to a maximum of two lines.
- Board game remains a rectangle editor because the pre-rendered board is placed at its X/Y without scaling.
- Keyword list exposes four independent X anchors, one shared Column Y, and one shared vertical step. Arrange the two-column form as X1/X2, X3/X4, Column Y/Vertical step, Alignment/Font, then Font size/Font color. The 20 keywords fill column-major with five rows per column; alignment applies to every keyword.
- Brand Detail may scroll independently when needed. Keep its brand name, save state, and Save button visible.
- Do not redraw a dirty Brand Detail during task polling. Save through the background queue and retain the draft when saving fails.
- Before changing brand or leaving the Brands route with unsaved edits, require an explicit Save, Discard, or Cancel choice.
- Create Brand accepts one valid Windows folder name, creates default settings through the background queue, then reloads and selects the new brand.

## Books workspace

- Use a `4/8` master-detail grid. Book List and Book Detail own independent vertical scrollbars.
- Keep search fixed above the list, filter folder names case-insensitively after a `200ms` debounce, and update only list rows while typing. Render at most the first 250 matches and ask for a narrower query beyond that window.
- Book Detail has exactly two tabs for this phase: **Overview** and **Output**. Preserve the selected tab and book across task refreshes.
- Overview shows CSV certificate status/hash/time, topic and keyword totals, per-topic `20/20` status, row-aware validation failures and **Validate CSV**. CSV requires one consistent non-empty Quote repeated across every row of a Topic.
- Output shows Brand selection, ready/stale status, PDF/Answer counts and sizes, fixed `2588x3375` at 300 PPI, **Process**, and **Open folder**.
- Disable Process unless `data.csv` and the selected Brand are both currently `Validated`. Keep Open folder available for ready or stale published output.
- Validation and processing run through the background queue. Avoid full Books-route redraws during task-only polling so search focus is stable; redraw after a returned workspace snapshot.
