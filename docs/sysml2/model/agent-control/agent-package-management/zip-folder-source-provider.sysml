package AgentControl {
    private import SoftwareItemMetadata::*;

    part def ZipFolderSourceProvider {
        doc /* Provider over a folder of name-semver zip packages, adapting the static PackageSource scan. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/AgentPackageManagement/ZipFolderSourceProvider.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/ZipFolderSourceProviderTests.cs");
            designFiles = ("docs/design/agent-control/agent-package-management/zip-folder-source-provider.md");
            verificationFiles = ("docs/verification/agent-control/agent-package-management/zip-folder-source-provider.md");
            requirementFiles = ("docs/reqstream/agent-control/agent-package-management/zip-folder-source-provider.yaml");
        }
    }
}
