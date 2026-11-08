package AgentControl {
    part def RepoImageLocator {
        doc /* Finds and decodes the picture a repo offers for its card (icon.png, else logo.png, in the repo's root): never a file that is not a small genuine PNG, a link, outside the repo root or of absurd declared size, and any failure means no picture. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/LauncherUI/RepoImageLocator.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoImageLocatorTests.cs */
        comment designRef /* Design: docs/design/agent-control/launcher-ui/repo-image-locator.md */
        comment verificationRef /* Verification: docs/verification/agent-control/launcher-ui/repo-image-locator.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/launcher-ui/repo-image-locator.yaml */
    }
}
