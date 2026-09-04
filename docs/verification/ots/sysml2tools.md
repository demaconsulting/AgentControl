## SysML2Tools Verification

This document provides the verification evidence for the `SysML2Tools` OTS software item.
Requirements for this OTS item are defined in the SysML2Tools OTS Software Requirements document.

### Required Functionality

DemaConsulting.SysML2Tools validates the SysML2 architecture model under `docs/sysml2/` for syntax
and reference errors, and renders each view declared in `docs/sysml2/views/design-views.sysml` to
an SVG diagram embedded in the design documentation. Both behaviors run in the same CI pipeline
that produces the compiled Design document, so a successful pipeline run is evidence that
SysML2Tools executed without error.

### Verification Approach

SysML2Tools is verified by a combination of authored self-validation evidence and direct FileAssert
assertions on its actual pipeline output:

- **Self-validation evidence**: `dotnet sysml2tools --validate --results <trx-file>` runs the
  tool's built-in self-test suite, which exercises `lint` and `render --format svg` against known
  model fixtures and writes a TRX file consumed by `reqstream --enforce`
  (`SysML2Tools_LintSelfTest`, `SysML2Tools_RenderSvgSelfTest`). This proves the tool's `lint` and
  `render` commands work correctly in isolation, but does not prove they were actually invoked
  successfully against this project's real model.
- **Lint pipeline evidence**: `lint.ps1` runs `dotnet sysml2tools lint 'docs/sysml2/**/*.sysml'`
  against the actual TemplateDotNetTool model and fails the build on any syntax or reference error.
  A build failure here is direct evidence that lint did not pass against the real model; a
  successful build is direct evidence that it did.
- **Render pipeline evidence**: The build-docs job runs `dotnet sysml2tools render` to produce one
  SVG file per declared view under `docs/design/generated/`, immediately before Pandoc compiles
  `docs/design/*.md` (which embed those SVG files by filename) and WeasyPrint renders the result to
  PDF. A missing SVG does **not** fail the Pandoc/WeasyPrint build - the compiled HTML/PDF simply
  omits the diagram silently - so a successful Pandoc/WeasyPrint build is not sufficient evidence
  that render succeeded. Instead, `SysML2Tools_DesignDiagramsSvg` is a FileAssert test that runs
  immediately after the render step and directly asserts that each expected SVG file
  (`SoftwareStructureView.svg`, `TemplateDotNetToolView.svg`, `CliView.svg`, `SelfTestView.svg`,
  `UtilitiesView.svg`) exists in `docs/design/generated/`, has a non-trivial size, and is
  well-formed XML with an `<svg>` root element. This is the only evidence that render actually
  produced the required diagrams against the real model.

### Test Scenarios

#### SysML2Tools_LintSelfTest

**Scenario**: SysML2Tools is invoked with `--validate`, which exercises `lint` against a known-good
and a known-bad model fixture as part of its built-in self-test suite.

**Expected**: Exits 0 with no reported syntax or reference errors for the valid fixture, and
correctly reports an error for the invalid fixture.

**Requirement coverage**: `Template-OTS-SysML2Tools-Lint`.

#### SysML2Tools_RenderSvgSelfTest

**Scenario**: SysML2Tools is invoked with `--validate`, which exercises `render --format svg`
against a known-good model fixture as part of its built-in self-test suite.

**Expected**: Exits 0 and produces a non-empty SVG file for the fixture's declared view.

**Requirement coverage**: `Template-OTS-SysML2Tools-Render`.

#### SysML2Tools_DesignDiagramsSvg

**Scenario**: FileAssert asserts, immediately after the real `dotnet sysml2tools render` step runs
against this project's actual model, that each of the five expected SVG files exists in
`docs/design/generated/`, has a non-trivial size, and is well-formed XML with an `<svg>` root
element.

**Expected**: FileAssert exits 0, proving render produced every declared view's diagram against the
real model - not just the self-test's fixture.

**Requirement coverage**: `Template-OTS-SysML2Tools-Render`.

### Acceptance Criteria

N/A - Acceptance criteria are managed at the system integration level. This OTS item is considered
verified when the integration test scenarios that exercise its functionality pass in the CI
pipeline.
