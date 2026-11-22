package AgentControl {
    private import SoftwareItemMetadata::*;

    part def RepoRefreshQueue {
        doc /* The one queue every lazy read of a repo's state goes through - the cards' cheap facts and working-tree checks and the folder checks of an add - running at most two reads at a time across all of them, joining a request to a read of the same kind for the same repo that is still waiting, starting user actions before cards on screen before background work (a waiting read can be promoted), counting a wave of reads for the global progress strip (posted to the UI thread), giving up a waiting read nobody wants, and stopping everything (a running git included) when the window closes; RepoRefreshKind, RepoRefreshPriority, RepoRefreshKey and RepoRefreshProgress are its small types. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/LauncherUI/RepoRefreshQueue.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoRefreshQueueTests.cs");
            designFiles = ("docs/design/agent-control/launcher-ui/repo-refresh-queue.md");
            verificationFiles = ("docs/verification/agent-control/launcher-ui/repo-refresh-queue.md");
            requirementFiles = ("docs/reqstream/agent-control/launcher-ui/repo-refresh-queue.yaml");
        }
    }
}
