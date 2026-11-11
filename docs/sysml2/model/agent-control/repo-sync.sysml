package AgentControl {
    private import SoftwareItemMetadata::*;

    part def RepoSyncSubsystem {
        doc
        /*
         * Syncs a repo's managed agent-file folders with a package's contents (zip or NuGet content folder)
         * (blind delete-replace), backs the release-notes dialog shown after a sync, and
         * proactively ensures the repo's .gitignore covers the four managed folders
         * immediately after a successful sync. ReleaseNotesViewerView (.axaml) has no
         * dedicated test file and is folded into this subsystem-level description.
         */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentControl.Tests/RepoSync/");
            designFiles = ("docs/design/agent-control/repo-sync.md");
            verificationFiles = ("docs/verification/agent-control/repo-sync.md");
            requirementFiles = ("docs/reqstream/agent-control/repo-sync.yaml");
        }

        part packageZipExtractor : PackageZipExtractor;
        part packageContentExtractor : PackageContentExtractor;
        part releaseNotesViewerViewModel : ReleaseNotesViewerViewModel;
        part gitIgnoreEnsurer : GitIgnoreEnsurer;
    }
}
