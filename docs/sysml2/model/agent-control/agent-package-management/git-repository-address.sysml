package AgentControl {
    private import SoftwareItemMetadata::*;

    part def GitRepositoryAddress {
        doc /* Validates the address of a Git repository (https, ssh or an absolute local path), refuses every form that could run code, be read as an option or carry a credential, and derives the one transport git may use and the key of the repository cache folder. */

        metadata info : SoftwareInfo {
            description = "Validates the address of a Git repository (https, ssh or an absolute local path), refuses every form that could run code, be read as an option or carry a credential, and derives the one transport git may use and the key of the repository cache folder.";
            sourceFiles = ("src/DemaConsulting.AgentControl/AgentPackageManagement/GitRepositoryAddress.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/GitRepositoryAddressTests.cs");
            designFiles = ("docs/design/agent-control/agent-package-management/git-repository-address.md");
            verificationFiles = ("docs/verification/agent-control/agent-package-management/git-repository-address.md");
            requirementFiles = ("docs/reqstream/agent-control/agent-package-management/git-repository-address.yaml");
        }
    }
}
