package AgentControl_BlockingCallGuard {
    private import SoftwareItemMetadata::*;

    part def BlockingCallGuard {
        doc /* Test-enabled guard against blocking calls on the UI thread: the code that is about to start a git process or read or write the disk asks it first, and on a thread a test has guarded (a scope around synchronous code, or the headless UI thread for its lifetime) the call is recorded and refused with BlockingCallOnGuardedThreadException; per-thread state, so nothing is guarded unless a test asks and the shipped application is unaffected. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/Utilities/BlockingCallGuard.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/Utilities/BlockingCallGuardTests.cs");
            designFiles = ("docs/design/agent-control/utilities/blocking-call-guard.md");
            verificationFiles = ("docs/verification/agent-control/utilities/blocking-call-guard.md");
            requirementFiles = ("docs/reqstream/agent-control/utilities/blocking-call-guard.yaml");
        }
    }
}
