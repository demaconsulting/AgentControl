package AgentControl {
    part def GitRepositoryAddress {
        doc /* Validates the address of a Git repository (https, ssh or an absolute local path), refuses every form that could run code, be read as an option or carry a credential, and derives the one transport git may use and the key of the repository cache folder. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/AgentPackageManagement/GitRepositoryAddress.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/GitRepositoryAddressTests.cs */
        comment designRef /* Design: docs/design/agent-control/agent-package-management/git-repository-address.md */
        comment verificationRef /* Verification: docs/verification/agent-control/agent-package-management/git-repository-address.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/agent-package-management/git-repository-address.yaml */
    }
}
