# Resources

Put the app icon here as `hdpt.ico`.

- **Window and taskbar icon:** the file is copied next to the exe (`Resources\hdpt.ico`) and loaded at startup, so it can also be replaced in a deployed copy without rebuilding.
- **Exe icon (Explorer, shortcuts):** baked in at build time when `Resources\hdpt.ico` exists in the repo.

If the file is missing or isn't a valid `.ico`, the app uses the built-in icon in `Assets\`.

A good `.ico` includes 16, 24, 32, 48 and 256 px sizes.
