### SignatureVerificationSupport

#### Verification Approach

`SignatureVerificationSupport` is verified with unit tests defined in `SignatureVerificationSupportTests.cs`. The
class is stateless and static, so nothing is mocked. The operating-system decision is a pure function of three
booleans (`IsSupportedPlatform`), which lets the tests cover every combination - including the platforms that the
test machine itself is not - without running on them. The public answer (`IsSupported`) is checked on the platforms
the project's continuous-integration matrix runs on (Windows, Linux and macOS), where it must be `true`; that test
is the evidence that the run-time probe of the platform's certificate-chain facility works on every supported
platform. The fixed explanation text is compared with its documented wording. The behavior of the *consumers* on an
unsupported platform (the provider's fail-closed refusal and the editor's disabled checkbox) is verified in the
`NuGetSourceProvider` and `SourceEditorViewModel` tests, which inject the answer.

The real capability probe is exercised, not only its decision logic: on the machine running the tests it builds a
certificate chain for the throw-away self-signed certificate offline and must report support, and its failure paths
are exercised deterministically through the chain evaluation with an already expired certificate and with no
certificate, both of which must report "not supported". Because the probe never uses the network or the machine's
trust stores, these tests are platform-independent in outcome and need no special setup. A platform whose certificate
facility genuinely cannot build a chain cannot be reproduced on the build machines; that case is covered by the
injected failing probe of the fail-closed tests rather than by a broken platform.

#### Test Environment

N/A - standard test environment. The test of `IsSupported` assumes it runs on Windows, Linux or macOS, which are the
platforms of the project's build matrix.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- The operating-system decision is `true` for exactly Windows, Linux and macOS and `false` for any other combination.
- `IsSupported` is `true` on the platforms of the build matrix.
- The explanation text is exactly "Package signature verification is not supported on this platform".
- A capability probe that returns `false` or fails in any way yields "not supported" without throwing.
- The real probe builds a one-element certificate chain for a throw-away self-signed certificate offline and
  reports support on the build platforms; an expired or missing certificate is "not supported".

#### Test Scenarios

**SignatureVerificationSupport_PlatformSupport_WindowsLinuxAndMacOsOnly**: Each of Windows, Linux and macOS alone
is supported, and no operating system flagged at all is not; on the machine running the tests `IsSupported` is
`true`. This scenario is tested by `SignatureVerificationSupport_IsSupportedPlatform_WindowsLinuxAndMacOsOnly` and
`SignatureVerificationSupport_IsSupported_OnTheCiPlatforms_IsTrue`, covering
`AgentControl-SignatureVerificationSupport-PlatformSupport`.

**SignatureVerificationSupport_UnsupportedExplanation_IsTheDocumentedText**: The explanation constant equals the
documented sentence. This scenario is tested by
`SignatureVerificationSupport_UnsupportedMessage_IsTheDocumentedExplanation`, covering
`AgentControl-SignatureVerificationSupport-UnsupportedExplanation`.

**SignatureVerificationSupport_FailClosedProbe_AnyFailureMeansUnsupported**: A probe that returns `false`, or that
throws a cryptographic error, a missing-native-library error, an unsupported-operation error or an unforeseen
error, is "not supported" and never throws; the whole decision is supported only for a supported operating system
*and* a successful probe, and the probe is not run on an unsupported operating system. This scenario is tested by
`SignatureVerificationSupport_RunProbe_ProbeAnswers_ReturnsTheAnswer`,
`SignatureVerificationSupport_RunProbe_ProbeFails_IsNotSupportedAndNeverThrows` and
`SignatureVerificationSupport_Evaluate_NeedsPlatformAndASuccessfulProbe`, covering
`AgentControl-SignatureVerificationSupport-FailClosedProbe`.

**SignatureVerificationSupport_RealChainProbe_BuildsAChainOfflineOrIsNotSupported**: The probe, run on this
platform, builds a chain of exactly one element for the throw-away certificate with no network access and
answers supported; evaluating a certificate that has expired (the build reports a status) answers "not
supported"; evaluating no certificate answers "not supported". This scenario is tested by
`SignatureVerificationSupport_ProbeCertificateChains_OnThisPlatform_BuildsAChainOfflineAndIsSupported`,
`SignatureVerificationSupport_ProbeChain_ExpiredCertificate_IsNotSupported` and
`SignatureVerificationSupport_ProbeChain_NullCertificate_IsNotSupported`, covering
`AgentControl-SignatureVerificationSupport-RealChainProbe`.
