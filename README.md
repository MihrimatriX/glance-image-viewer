# Glance

**Install once, open everything — instantly.**

A free, open-source, fast image viewer for Windows 10 (1809+) and Windows 11, x64 and ARM64.
Every non-baseline format is decoded by libraries bundled in the app — no Microsoft Store codec extensions needed.
No telemetry, no network calls.

> Status: **Phase 0** (skeleton + spikes). See [`docs/adr`](docs/adr).

## Build

Requires the .NET 10 SDK on Windows.

```
dotnet build src/Glance.App            # builds for the machine's architecture
dotnet test --solution Glance.slnx
src/Glance.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/Glance.exe <image>
```

## Layout

| Path | What |
|---|---|
| `src/Glance.App` | WinUI 3 app (views, view models, Win2D canvas) |
| `src/Glance.Core` | No UI deps: viewport math, later navigation/cache/prefetch/settings |
| `src/Glance.Imaging` | Decoders, format sniffing, color management |
| `tests/` | xUnit v3 (Microsoft.Testing.Platform) |
| `docs/adr` | Architecture decision records |

## License

Glance is licensed under the **GNU General Public License v3.0 or later** — see [LICENSE](LICENSE).

The **Glance name and logo are not covered by this license** and may not be used to endorse or promote derived products without permission.

Third-party components: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
