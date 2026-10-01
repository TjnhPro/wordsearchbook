# Word Search Book v0.1.0

## Package

- Platform: Windows x64.
- Distribution: one self-contained `WordSearchBook.exe` plus an optional SHA-256 checksum file.
- .NET Desktop Runtime is included in the executable.
- Microsoft Edge WebView2 Runtime must be installed on Windows.

## Installation and updates

1. Create a writable folder such as `C:\WordSearchBook`.
2. Download `WordSearchBook.exe` into that folder.
3. Run the executable and add `brands/` and `input/` data beside it.
4. To update, close the application and replace only `WordSearchBook.exe`.

Book data, settings, workspace cache and generated output remain outside the executable and are not replaced during an update.

## MVP limitations

- Windows x64 only.
- No installer or automatic updater.
- The executable is not Authenticode-signed, so Windows SmartScreen may ask for confirmation.
- WebView2 Evergreen Runtime is not bundled.
