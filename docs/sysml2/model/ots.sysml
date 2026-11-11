package AgentControl {
    private import SoftwareItemMetadata::*;

    doc
    /*
     * Off-the-shelf (OTS) software parts for third-party libraries that ship as part of the
     * AgentControl application itself (as opposed to build/CI/quality tooling, which is
     * documented as OTS requirements only, without a SysML2 part - see
     * docs/reqstream/ots/flaui.yaml and docs/reqstream/ots/wixtoolset.yaml).
     */

    part def Avalonia {
        doc /* Cross-platform UI framework rendering AgentControl's windows and controls. */

        metadata info : SoftwareInfo {
            designFiles = ("docs/design/ots/avalonia.md");
            verificationFiles = ("docs/verification/ots/avalonia.md");
            requirementFiles = ("docs/reqstream/ots/avalonia.yaml");
        }
    }

    part def Serilog {
        doc /* Structured-logging library backing the Logging subsystem's rolling diagnostic log file. */

        metadata info : SoftwareInfo {
            designFiles = ("docs/design/ots/serilog.md");
            verificationFiles = ("docs/verification/ots/serilog.md");
            requirementFiles = ("docs/reqstream/ots/serilog.yaml");
        }
    }

    part def NuGetCaching {
        doc /* DemaConsulting.NuGet.Caching library backing the NuGet package source provider: lists package versions on a folder feed or NuGet V3 server, authenticates through a per-call credential callback, and unpacks a chosen version into a package cache. */

        metadata info : SoftwareInfo {
            designFiles = ("docs/design/ots/nuget-caching.md");
            verificationFiles = ("docs/verification/ots/nuget-caching.md");
            requirementFiles = ("docs/reqstream/ots/nuget-caching.yaml");
        }
    }
}