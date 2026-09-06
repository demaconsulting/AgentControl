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

        comment reqRef /* Requirements: docs/reqstream/ots/avalonia.yaml */
    }

    part def Serilog {
        doc /* Structured-logging library backing the Logging subsystem's rolling diagnostic log file. */

        comment reqRef /* Requirements: docs/reqstream/ots/serilog.yaml */
    }
}
