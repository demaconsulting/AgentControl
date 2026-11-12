package AgentControl {
    private import SoftwareItemMetadata::*;

    part def AgentPackageManagementSubsystem {
        doc /* Package source configuration model, per-source providers (zip folder, NuGet folder and server, Git repository), alias-to-provider resolution, version parsing/comparison, per-source version caching, a Git repository source (tags as versions, hardened shallow fetch of one tag), per-source signed-package enforcement with an optional publisher pin, the content identity of delivered packages, and the platform check for signature verification. */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/");
            designFiles = ("docs/design/agent-control/agent-package-management.md");
            verificationFiles = ("docs/verification/agent-control/agent-package-management.md");
            requirementFiles = ("docs/reqstream/agent-control/agent-package-management.yaml");
        }

        part packageSource : PackageSource;
        part packageVersion : PackageVersion;
        part packageVersionCache : PackageVersionCache;
        part packageSourceRegistry : PackageSourceRegistry;
        part zipFolderSourceProvider : ZipFolderSourceProvider;
        part nuGetSourceProvider : NuGetSourceProvider;
        part signatureVerificationSupport : SignatureVerificationSupport;
        part packageContentIdentity : PackageContentIdentity;
        part gitSourceProvider : GitSourceProvider;
        part gitRepositoryAddress : GitRepositoryAddress;
        part gitTagPattern : GitTagPattern;
        part nuGetCaching : NuGetCaching;

        // Relationships of the Git repository source (the other edges are in the design's architecture diagram):
        // the registry creates the provider; the provider uses the address, the tag pattern and the content identity
        connect packageSourceRegistry to gitSourceProvider;
        connect gitSourceProvider to gitRepositoryAddress;
        connect gitSourceProvider to gitTagPattern;
        connect gitSourceProvider to packageContentIdentity;
    }
}
