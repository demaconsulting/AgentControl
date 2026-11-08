package AgentControl {
    part def DialogBehavior {
        doc /* Keyboard behavior every AgentControl dialog shares: Tab and Shift+Tab cycle inside the dialog, the first sensible control takes the visible keyboard focus when it opens, and Escape cancels a dialog that has a way to cancel. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/LauncherUI/DialogBehavior.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/LauncherUI/DialogBehaviorTests.cs */
        comment designRef /* Design: docs/design/agent-control/launcher-ui/dialog-behavior.md */
        comment verificationRef /* Verification: docs/verification/agent-control/launcher-ui/dialog-behavior.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/launcher-ui/dialog-behavior.yaml */
    }
}
