package AgentControl {
    private import SoftwareItemMetadata::*;

    part def RepoImageLocator {
        doc /* Finds and decodes the picture a repo offers for its card (icon.png, else logo.png, in the repo's root): never a file that is not a small genuine PNG, a link, outside the repo root or of absurd declared size, and any failure means no picture. */

        metadata info : SoftwareInfo {
            description = "Finds and decodes the picture a repo offers for its card (icon.png, else logo.png, in the repo's root): never a file that is not a small genuine PNG, a link, outside the repo root or of absurd declared size, and any failure means no picture.";
            sourceFiles = ("src/DemaConsulting.AgentControl/LauncherUI/RepoImageLocator.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoImageLocatorTests.cs");
            designFiles = ("docs/design/agent-control/launcher-ui/repo-image-locator.md");
            verificationFiles = ("docs/verification/agent-control/launcher-ui/repo-image-locator.md");
            requirementFiles = ("docs/reqstream/agent-control/launcher-ui/repo-image-locator.yaml");
        }
    }
}
