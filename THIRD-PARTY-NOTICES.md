# Third-party components

CodexPad application source and original icons are licensed under the MIT License in LICENSE.

The portable Windows package includes these separately licensed components:

- **Microsoft .NET and Windows Desktop Runtime**. The runtime's license and third-party notices are included in `licenses/dotnet-LICENSE.txt`, `licenses/dotnet-THIRD-PARTY-NOTICES.txt`, and `licenses/windowsdesktop-LICENSE.txt`. Sources: https://github.com/dotnet/runtime and https://github.com/dotnet/wpf.
- **CPython 3.13.15, Windows x64 embeddable package**, obtained from https://www.python.org/ftp/python/3.13.15/python-3.13.15-embed-amd64.zip. Its license and included component notices are retained in `runtime/python/LICENSE.txt`. SHA-256: `d1f04d990aee1253d8569e8e5104e30fa9f5fa830899f14843448872d936a2cf`.
- **Windows system fonts and APIs** are used from the user's system, not redistributed as separate assets.

The Python executable's Authenticode signature was checked as valid and signed by the Python Software Foundation when this package was prepared. The `_pth` file is adjusted to include the application directory for local helper imports. No third-party Python packages are installed.

Hardware protocol references are credited in README.md and the relevant source files; those projects are not bundled. Codex/Work and OpenAI services are not bundled. This is an independent community project, not an official OpenAI product.
