package AgentControl {
    part def SignatureVerificationSupport {
        doc /* Tells whether the current platform can verify NuGet package signatures (Windows, Linux and macOS when the certificate-chain facility works), so the option to require signed packages is only offered where it can be honored and a requiring source fails closed elsewhere. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/AgentPackageManagement/SignatureVerificationSupport.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/SignatureVerificationSupportTests.cs */
        comment designRef /* Design: docs/design/agent-control/agent-package-management/signature-verification-support.md */
        comment verificationRef /* Verification: docs/verification/agent-control/agent-package-management/signature-verification-support.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/agent-package-management/signature-verification-support.yaml */
    }
}
