## Selected-mesh fitting for original UI controls (October 19, 2026)

UI controls retain the selected mesh's stored fit endpoints separately from
geometric bounds. Drawing and inverse shape picking share this fit, preserving
stored padding while authored layout and direct-origin submission keep their
existing meanings. Invalid metadata has an explicit geometric fallback.

The [bounded UI-003 scope](../proposals/ui-003-trace-ui-child-transforms.md) records
static evidence, small current-corpus effects and remaining hierarchy, control
rotation and state-selection gaps. UI-003 stays open and UI-004 is unchanged.
Synthetic CPU regressions were prepared for padding, picking, reflected/sheared
transforms, fallback and state stability. Local .NET execution and rendered
comparison were unavailable; exact-head CI and independent review remain gates.
