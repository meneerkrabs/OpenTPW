## Remove the dead path-oracle workflow (October 30, 2026)

The dispatch-only workflow `.github/workflows/path-render-oracle-artifact.yml` was a bootstrap for a diagnostic whose files (`tools/web-path-render`, `tools/PathOracleAudit`) never reached main: its first step requires them and fails. The diagnostic itself was dropped from the path-art refresh work (#883), so the workflow is removed. Nothing else referenced it; no ordinary CI gate changes.
