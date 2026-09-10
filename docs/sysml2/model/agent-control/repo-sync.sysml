package AgentControl {
    part def RepoSyncSubsystem {
        doc
        /*
         * Syncs a repo's managed agent-file folders with a package zip's contents
         * (blind delete-replace), backs the release-notes dialog shown after a sync, and
         * proactively ensures the repo's .gitignore covers the four managed folders
         * immediately after a successful sync. ReleaseNotesViewerView (.axaml) has no
         * dedicated test file and is folded into this subsystem-level description.
         */

        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/RepoSync/ */
        comment designRef /* Design: docs/design/agent-control/repo-sync.md */
        comment verificationRef /* Verification: docs/verification/agent-control/repo-sync.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/repo-sync.yaml */

        part packageZipExtractor : PackageZipExtractor;
        part releaseNotesViewerViewModel : ReleaseNotesViewerViewModel;
        part gitIgnoreEnsurer : GitIgnoreEnsurer;
    }
}
