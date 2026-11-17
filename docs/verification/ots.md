# OTS Software Verification

This section provides verification evidence for all Off-The-Shelf (OTS) software items
used by the Agent Control.

## Scope

Each OTS item is verified by a combination of:

- The OTS tool's own self-validation suite (where the tool supports `--validate`)
- Pipeline output assertions performed by FileAssert on documents produced by the tool

Internal OTS tool design is out of scope; only integration and usage evidence is documented.

## OTS Items

The following OTS items have verification evidence in this section:

- Avalonia (*avalonia.md*) — cross-platform UI framework
- BuildMark (*buildmark.md*) — build-notes documentation tool
- FileAssert (*fileassert.md*) — document assertion tool
- FlaUI (*flaui.md*) — Windows UI Automation testing library
- Pandoc (*pandoc.md*) — Markdown-to-HTML conversion tool
- NuGetCaching (*nuget-caching.md*) — NuGet folder-feed and server listing, authentication and package caching library
- ReqStream (*reqstream.md*) — requirements traceability tool
- ReviewMark (*reviewmark.md*) — file review enforcement tool
- SarifMark (*sarifmark.md*) — SARIF report conversion tool
- Serilog (*serilog.md*) — structured-logging library
- SonarMark (*sonarmark.md*) — SonarCloud quality report tool
- SysML2Tools (*sysml2tools.md*) — architecture model validation and diagram rendering tool
- VersionMark (*versionmark.md*) — tool-version documentation tool
- WeasyPrint (*weasyprint.md*) — HTML-to-PDF conversion tool
- WiX Toolset (*wixtoolset.md*) — Windows MSI installer packaging tool
- xUnit (*xunit.md*) — unit-testing framework
