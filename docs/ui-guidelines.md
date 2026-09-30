# Desktop UI guidelines

## Design viewport

- Design and review the WebView UI against a `1600x900` content baseline.
- `MainWindow` starts at and cannot be resized below `1610x910`. The additional space keeps the `1600x900` UI from touching or being hidden by the window boundary.
- Do not reduce `Width`, `Height`, `MinWidth`, or `MinHeight` below `1610x910` unless the complete desktop layout is redesigned and verified at the new baseline.
- Keep primary navigation on the left and the selected workspace detail on the right.
- Long-running and file-system operations must continue to use the background task queue so rendering and navigation remain responsive.
- Always use one modal dialog with confirmation and loading states when the user closes the application. After confirmation, show an indeterminate progress indicator for at least 500ms so the closing state is perceptible.
- Cancel all active tasks and close automatically when they stop or after five seconds. Do not allow the dialog to be dismissed while cancellation is in progress.

## Brands workspace

- Use a `3/9` master-detail grid: Brand List takes 25% on the left and Brand Detail takes 75% on the right.
- Keep the search box fixed above the Brand List. The list owns its vertical scrollbar and must not increase the page height.
- Filter brand names case-insensitively after a `250ms` debounce. Update only the list rows; never replace the complete route while the user is typing.
- Use one two-column panel grid in this order: Page layout/Page layout preview, Topic/Page number, then Keyword list/Board game. Keep every panel background white.
- Keep Answer styling inside the Board game panel below its placement and font settings.
- Show the output page as fixed `2588x3375`; do not expose editable page width or height controls.
- Each brand owns `brands/{brand}/page_layout.png`. It must be a PNG exactly `2588x3375`; Create Brand also creates a white default layout.
- Creating a valid default PNG does not certify it. Show `Not validated`, `Validated`, or `Needs validation` separately from settings health.
- Deep PNG validation runs only when the user selects **Validate layout**. Workspace load and refresh may read `brand.validation.json` and file metadata but must not decode the PNG.
- Keep the Page Layout card visible even when Brand or global settings are invalid. Disable validation while the current Brand form is dirty or saving.
- Keep a **Page layout preview** panel inside the Page Layout card. **Draw demo** uses only saved settings and writes `page_layout.preview.png` beside the source layout through the background queue.
- Disable **Draw demo** while the selected Brand has unsaved edits, is saving, or already has an active preview task. **Open folder** remains available and must use the desktop-derived Brand path rather than a path supplied by the frontend.
- Preview generation is independent from the validation certificate and must never modify `page_layout.png` or `brand.validation.json`.
- Books may list any Brand with readable settings, but **Generate pages** stays disabled until that Brand's layout state is `Validated`.
- Topic and Page number editors use X/Y anchors plus `Left`, `Center`, or `Right` alignment. X is the selected horizontal anchor; Y is always the text top edge.
- Board game remains a rectangle editor because the pre-rendered board is placed at its X/Y without scaling.
- Keyword list exposes four independent X anchors, one shared Column Y, and one shared vertical step. Arrange the two-column form as X1/X2, X3/X4, Column Y/Vertical step, Alignment/Font, then Font size/Font color. The 20 keywords fill column-major with five rows per column; alignment applies to every keyword.
- Brand Detail may scroll independently when needed. Keep its brand name, save state, and Save button visible.
- Do not redraw a dirty Brand Detail during task polling. Save through the background queue and retain the draft when saving fails.
- Before changing brand or leaving the Brands route with unsaved edits, require an explicit Save, Discard, or Cancel choice.
- Create Brand accepts one valid Windows folder name, creates default settings through the background queue, then reloads and selects the new brand.
