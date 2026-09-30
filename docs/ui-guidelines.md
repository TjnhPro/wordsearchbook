# Desktop UI guidelines

## Design viewport

- Design and review the WebView UI against a `1600x900` content baseline.
- `MainWindow` starts at and cannot be resized below `1610x910`. The additional space keeps the `1600x900` UI from touching or being hidden by the window boundary.
- Do not reduce `Width`, `Height`, `MinWidth`, or `MinHeight` below `1610x910` unless the complete desktop layout is redesigned and verified at the new baseline.
- Keep primary navigation on the left and the selected workspace detail on the right.
- Long-running and file-system operations must continue to use the background task queue so rendering and navigation remain responsive.

## Brands workspace

- Use a `3/9` master-detail grid: Brand List takes 25% on the left and Brand Detail takes 75% on the right.
- Keep the search box fixed above the Brand List. The list owns its vertical scrollbar and must not increase the page height.
- Filter brand names case-insensitively after a `250ms` debounce. Update only the list rows; never replace the complete route while the user is typing.
- Arrange Topic, Board game, Keyword list, and Page number editors in a `2x2` grid. Keep Answer styling in a collapsed advanced section.
- Show the output page as fixed `2588x3375`; do not expose editable page width or height controls.
- Each brand owns `brands/{brand}/page_layout.png`. It must be a PNG exactly `2588x3375`; Create Brand also creates a white default layout.
- Creating a valid default PNG does not certify it. Show `Not validated`, `Validated`, or `Needs validation` separately from settings health.
- Deep PNG validation runs only when the user selects **Validate layout**. Workspace load and refresh may read `brand.validation.json` and file metadata but must not decode the PNG.
- Keep the Page Layout card visible even when Brand or global settings are invalid. Disable validation while the current Brand form is dirty or saving.
- Books may list any Brand with readable settings, but **Generate pages** stays disabled until that Brand's layout state is `Validated`.
- Topic and Page number editors use X/Y anchors plus `Left`, `Center`, or `Right` alignment. X is the selected horizontal anchor; Y is always the text top edge.
- Board game remains a rectangle editor because the pre-rendered board is placed at its X/Y without scaling.
- Keyword list exposes four independent X/Y anchors and one shared vertical step. The 20 keywords fill column-major with five rows per column; alignment applies to every keyword.
- Brand Detail may scroll independently when needed. Keep its brand name, save state, and Save button visible.
- Do not redraw a dirty Brand Detail during task polling. Save through the background queue and retain the draft when saving fails.
- Before changing brand or leaving the Brands route with unsaved edits, require an explicit Save, Discard, or Cancel choice.
- Create Brand accepts one valid Windows folder name, creates default settings through the background queue, then reloads and selects the new brand.
