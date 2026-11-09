package AgentControl {
    private import SoftwareItemMetadata::*;

    part def SignatureVerificationSupport {
        doc /* Tells whether the current platform can verify NuGet package signatures (Windows, Linux and macOS when the certificate-chain facility works), so the option to require signed packages is only offered where it can be honored and a requiring source fails closed elsewhere. */

        metadata info : SoftwareInfo {
            description = "Tells whether the current platform can verify NuGet package signatures (Windows, Linux and macOS when the certificate-chain facility works), so the option to require signed packages is only offered where it can be honored and a requiring source fails closed elsewhere.";
            sourceFiles = ("src/DemaConsulting.AgentControl/AgentPackageManagement/SignatureVerificationSupport.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/SignatureVerificationSupportTests.cs");
            designFiles = ("docs/design/agent-control/agent-package-management/signature-verification-support.md");
            verificationFiles = ("docs/verification/agent-control/agent-package-management/signature-verification-support.md");
            requirementFiles = ("docs/reqstream/agent-control/agent-package-management/signature-verification-support.yaml");
        }
    }
}
