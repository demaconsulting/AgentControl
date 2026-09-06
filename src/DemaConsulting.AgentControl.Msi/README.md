# DemaConsulting.AgentControl.Msi

WiX Toolset v5 packaging project that produces the Windows installer for
`DemaConsulting.AgentControl`.

## Single-file installer

The build produces exactly one output file:

```text
src/DemaConsulting.AgentControl.Msi/bin/x64/Release/DemaConsulting.AgentControl.Msi.msi
```

`Package.wxs` sets `<MediaTemplate EmbedCab="yes" CompressionLevel="high" />`,
which embeds the generated cabinet(s) directly inside the `.msi` at build
time. No `cab1.cab`, `cab2.cab`, or any other external `.cab` file is
produced alongside the `.msi`, and none is required to be shipped or copied
separately for installation to succeed - copying/distributing the single
`.msi` file is sufficient.

## Runtime prerequisite

The packaged payload is a **self-contained, single-file** `dotnet publish`
output. It bundles its own copy of the .NET 10 runtime, so the target
machine has **no pre-installed runtime prerequisite** - the MSI installs
everything the application needs to run. Most managed and native
dependencies (including Avalonia's native graphics-rendering libraries)
are bundled directly into the single `DemaConsulting.AgentControl.exe` via
`IncludeNativeLibrariesForSelfExtract`. `HarvestDirectory` needs no
structural change for this, since it harvests whatever files are
physically present in the published output directory.

## Build implementation notes

- The main `DemaConsulting.AgentControl.csproj` is referenced via a plain
  `ProjectReference` (no `Publish="true"`). A dedicated MSBuild target,
  `PublishAgentControlForHarvest`, instead runs a fully independent
  `dotnet publish` of that project into its own isolated
  `obj/wixpublish/obj`, `obj/wixpublish/bin`, and `obj/wixpublish/publish`
  folders, only after the solution's normal build of that same project has
  already completed. This avoids a file-lock race that previously occurred
  when WiX's own `Publish="true"` nested publish wrote to the exact same
  default `obj`/`bin` output folders that the solution's own concurrent
  build of the same project was using.
- `HarvestDirectory` and an explicit `BindPath` both point at that isolated
  publish output directory, so every published file - including
  publish-only native runtime assets - harvests and resolves correctly.
- See the inline comments in `DemaConsulting.AgentControl.Msi.wixproj` and `Package.wxs`
  for the full root-cause explanation and rationale.
