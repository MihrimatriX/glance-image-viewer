# ADR 0001 — Phase 0 stack adjustments

Status: proposed · 2026-10-06

Small deviations from the decided stack, each in service of cold start or package size.

1. **Windows App SDK component packages, not the metapackage.**
   `Microsoft.WindowsAppSDK` 2.5.1 pulls AI, ML, Widgets and Search. We reference only
   `Microsoft.WindowsAppSDK.WinUI` 2.3.9 + `Microsoft.WindowsAppSDK.InteractiveExperiences` 2.1.9
   (Foundation/Base come transitively). Builds and runs self-contained, x64 and ARM64.
   Revisit if a needed API (e.g. AppInstance activation redirection in Phase 1) lives elsewhere.

2. **`Microsoft.Extensions.DependencyInjection` without the Generic Host.**
   Hosting adds configuration/logging/lifetime plumbing we don't use and costs startup time
   against the 400 ms cold-start budget. Add Hosting only if we need its lifetime/logging pipeline.

3. **Tests run on Microsoft.Testing.Platform.** xunit.v3 4.x no longer supports VSTest under the .NET 10 SDK;
   `global.json` opts `dotnet test` into MTP. Run with `dotnet test --solution Glance.slnx`.

4. **Product codename is `Glance`** (spec placeholder was `Glimpse`), set in `Directory.Build.props`.

5. **Native AOT spike (optional) not run in Phase 0.** Win2D and Magick.NET both rely on runtime
   interop that needs trimming validation; deferred until Phase 1 cold-start numbers show whether it's needed.
