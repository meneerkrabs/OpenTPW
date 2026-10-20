## Restore saved research groups before selected projects (October 19, 2026)

OpenTPW saves now restore retained research group limits before validating selected
projects. A player can select a project in a group retained from an imported lab;
previously that selection could make OpenTPW's own save fail to reload because the
loader checked it against recomputed groups first.

Synthetic serialize/deserialize/restore coverage exercises this round trip and keeps
unknown, completed, prerequisite-blocked and unsaved closed-group cursors invalid.
The original-save import policy is unchanged; this fixes OpenTPW's own save format,
not an additional original-game fidelity claim.
