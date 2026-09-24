# Contributing

CodexPad is a Windows-only preview. Small, focused fixes and compatibility reports are welcome.

## Development

1. Install .NET SDK 10, PowerShell 7 and Python 3.13 on Windows x64.
2. Run `./build.ps1`.
3. Run `python -m unittest test_pad_protocol test_pad_status test_pad_backend` and `./artifacts/app/CodexPad.exe --logic-test`.
4. Run `./package.ps1` to create a portable package from an explicit allowlist.

Keep the application local-first. Do not add telemetry, upload chat data, or silently change hardware configuration. Unknown task states must remain unknown rather than being interpreted as successful completion.

The Python status adapter reads internal Codex formats. Keep this dependency isolated. UI Automation must identify the intended controls; never fall back to clicking an arbitrary screen position or moving the real pointer.

## Pull requests and reports

Describe the problem, the change, how it was tested and any remaining limitations. For hardware reports include the model and USB VID/PID, but remove device serial numbers. Avoid attaching configuration backups, chat logs, screenshots with private titles, or Codex databases.

There is no contributor license agreement. Contributions are accepted under the project's MIT License.
