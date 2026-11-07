package AgentControl {
    doc
    /*
     * Off-the-shelf (OTS) software parts for third-party libraries that ship as part of the
     * AgentControl application itself (as opposed to build/CI/quality tooling, which is
     * documented as OTS requirements only, without a SysML2 part - see
     * docs/reqstream/ots/flaui.yaml and docs/reqstream/ots/wixtoolset.yaml).
     */

    part def Avalonia {
        doc /* Cross-platform UI framework rendering AgentControl's windows and controls. */

        comment designRef /* Design: docs/design/ots/avalonia.md */
        comment verificationRef /* Verification: docs/verification/ots/avalonia.md */
        comment reqRef /* Requirements: docs/reqstream/ots/avalonia.yaml */
    }

    part def Serilog {
        doc /* Structured-logging library backing the Logging subsystem's rolling diagnostic log file. */

        comment designRef /* Design: docs/design/ots/serilog.md */
        comment verificationRef /* Verification: docs/verification/ots/serilog.md */
        comment reqRef /* Requirements: docs/reqstream/ots/serilog.yaml */
    }

    part def NuGetCaching {
        doc /* DemaConsulting.NuGet.Caching library backing the NuGet package folder source provider: lists package versions and unpacks a chosen version into a package cache. */

        comment designRef /* Design: docs/design/ots/nuget-caching.md */
        comment verificationRef /* Verification: docs/verification/ots/nuget-caching.md */
        comment reqRef /* Requirements: docs/reqstream/ots/nuget-caching.yaml */
    }
}