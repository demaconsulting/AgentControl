// Views rendered into docs/design/generated/ for the Design document.
// Per SysML2Tools syntax, 'expose' is only valid inside a named 'view' usage
// (not a 'view def' definition), and it - not 'render' - is what scopes the
// rendered diagram content.
package AgentControlDesignViews {
    private import StandardViewDefinitions::*;
    private import AgentControl::*;

    view SoftwareStructureView : InterconnectionView {
        expose AgentControl::AgentControlSystem;
    }

    view AgentControlView {
        expose AgentControlSystem;
    }

    view LauncherUIView {
        expose LauncherUISubsystem;
    }

    view AgentPackageManagementView {
        expose AgentPackageManagementSubsystem;
    }

    view PackageCredentialsView {
        expose PackageCredentialsSubsystem;
    }

    view RepoSyncView {
        expose RepoSyncSubsystem;
    }

    view RepoConfigView {
        expose RepoConfigSubsystem;
    }

    view AgentToolLauncherView {
        expose AgentToolLauncherSubsystem;
    }

    view GitIntegrationView {
        expose GitIntegrationSubsystem;
    }

    view SettingsView {
        expose SettingsSubsystem;
    }

    view LoggingView {
        expose LoggingSubsystem;
    }

    view StartupView {
        expose StartupSubsystem;
    }

    view UtilitiesView {
        expose UtilitiesSubsystem;
    }
}
