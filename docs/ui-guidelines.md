# Desktop UI guidelines

## Design viewport

- Design and review the WebView UI against a `1600x900` content baseline.
- `MainWindow` starts at and cannot be resized below `1610x910`. The additional space keeps the `1600x900` UI from touching or being hidden by the window boundary.
- Do not reduce `Width`, `Height`, `MinWidth`, or `MinHeight` below `1610x910` unless the complete desktop layout is redesigned and verified at the new baseline.
- Keep primary navigation on the left and the selected workspace detail on the right.
- Long-running and file-system operations must continue to use the background task queue so rendering and navigation remain responsive.

## Brands workspace

- Use a `40/60` master-detail grid: Brand List on the left and Brand Detail on the right.
- Keep the search box fixed above the Brand List. The list owns its vertical scrollbar and must not increase the page height.
- Filter brand names case-insensitively after a `250ms` debounce. Update only the list rows; never replace the complete route while the user is typing.
- Arrange Topic, Board game, Keyword list, and Page number editors in a `2x2` grid. Keep Answer styling in a collapsed advanced section.
- Brand Detail may scroll independently when needed. Keep its brand name, save state, and Save button visible.
- Do not redraw a dirty Brand Detail during task polling. Save through the background queue and retain the draft when saving fails.
- Before changing brand or leaving the Brands route with unsaved edits, require an explicit Save, Discard, or Cancel choice.
