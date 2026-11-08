### SignatureVerificationSupport

![AgentPackageManagement Structure](AgentPackageManagementView.svg)

#### Purpose

`SignatureVerificationSupport` tells whether the current platform can verify NuGet package signatures, so that the
option to require signed packages is only offered where it can be honored and a source that requires them is never
used where they cannot be checked. It satisfies `AgentControl-SignatureVerificationSupport-PlatformSupport`,
`AgentControl-SignatureVerificationSupport-FailClosedProbe`,
`AgentControl-SignatureVerificationSupport-RealChainProbe` and
`AgentControl-SignatureVerificationSupport-UnsupportedExplanation`.

Requiring signatures is only meaningful if verification really happens. The NuGet caching OTS item verifies
signatures explicitly on Windows, Linux and macOS (NuGet's own verification is skipped on Linux and macOS unless an
environment variable opts in, which is why the library does not rely on it), and the project's continuous-integration
matrix runs on exactly those three platforms, so those are the platforms for which the behavior is evidenced.
Anywhere else the answer is "not supported". Support is decided in one place because two consumers must agree: the
source editor disables its "Require signed packages" checkbox where verification is unsupported, and
`NuGetSourceProvider` refuses to list or acquire packages for a source that requires signatures there (a settings
file edited by hand can enable the option regardless of the editor) — it fails closed rather than silently skipping
verification.

#### Data Model

**UnsupportedMessage**: `const string` — "Package signature verification is not supported on this platform", the
single explanation shown as the checkbox tooltip and used in the refusal message.

**ProbeResult**: `Lazy<bool>` — the run-time probe result, computed once on first use.

The unit is a stateless static class and is thread-safe.

#### Key Methods

**IsSupported**: `true` when the operating system is Windows, Linux or macOS (`IsSupportedPlatform`) *and* the
certificate-chain probe succeeds, evaluated through `Evaluate`
(`AgentControl-SignatureVerificationSupport-PlatformSupport`).

**IsSupportedPlatform(isWindows, isLinux, isMacOS)**: The operating-system decision alone, with the three
answers as parameters so every combination can be tested on any machine. `true` for Windows, Linux or macOS;
`false` otherwise.

**Evaluate(isWindows, isLinux, isMacOS, probe)**: The whole decision with the platform and the probe as
parameters, so a probe that fails can be exercised deterministically: supported only when the platform is
supported and `RunProbe` of the probe says so; the probe is not run on an unsupported platform.

**RunProbe(probe)**: Runs a probe so that it can only answer "supported" by returning `true`. A probe that returns
`false` **or fails in any way** - a cryptographic error, a native library that cannot be loaded, an unsupported
operation, anything else - is "not supported"; the failure is never propagated, so a broken crypto stack can only
make the platform unsupported (fail closed), never throw into the editor or the provider
(`AgentControl-SignatureVerificationSupport-FailClosedProbe`).

**ProbeCertificateChains** (internal, so tests can run it): The platform probe. It creates a throw-away, in-memory self-signed
certificate and asks the platform's certificate-chain facility to build a chain for it **offline**:
the chain policy trusts only that certificate as the root (custom root trust), revocation checking is
disabled and downloading of missing certificates is disabled, so the result depends on neither the
machine's trust stores nor the network. It answers `true` only when the build succeeds with no
unexpected chain status and the chain has exactly one element (the certificate itself); any other
outcome - the build failing, an unexpected status, a different chain length, or an exception - is
"not supported". This is the same facility NuGet's signature verification builds on, so a platform
that cannot do it cannot verify signatures; a chain facility that merely exists and constructs
(`new X509Chain()` always succeeds) proves nothing and is not enough
(`AgentControl-SignatureVerificationSupport-RealChainProbe`). Exceptions escape to `RunProbe`, which
turns them into "not supported".

**ProbeChain(certificate)**: The chain build and its evaluation for a given certificate,
separated from the creation of the throw-away certificate so the failure paths can be tested on any
platform: a `null` certificate and a certificate that has expired (which makes the build report a
status) are both "not supported".

#### Error Handling

No method throws for an unsupported platform or a failing probe; the answer is a value. A failure of the probe,
of whatever kind, is turned into "not supported" (fail closed) rather than propagated.

#### Dependencies

- **.NET BCL** — `System.OperatingSystem` and `System.Security.Cryptography.X509Certificates`.

#### Callers

- **NuGetSourceProvider** — refuses to list or acquire for a source that requires signatures when unsupported
  (`AgentControl-NuGetSourceProvider-FailClosedWithoutVerification`).
- **SourceEditorViewModel** (`LauncherUI`) — disables the "Require signed packages" checkbox and shows the
  explanation.
