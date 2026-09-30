# Desktop UI guidelines

## Design viewport

- Design and review the WebView UI against a `1600x900` content baseline.
- `MainWindow` starts at and cannot be resized below `1610x910`. The additional space keeps the `1600x900` UI from touching or being hidden by the window boundary.
- Do not reduce `Width`, `Height`, `MinWidth`, or `MinHeight` below `1610x910` unless the complete desktop layout is redesigned and verified at the new baseline.
- Keep primary navigation on the left and the selected workspace detail on the right.
- Long-running and file-system operations must continue to use the background task queue so rendering and navigation remain responsive.
