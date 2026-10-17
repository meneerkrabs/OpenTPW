## Water slide riders follow the flume (October 17, 2026)

A rider lying in a flume used the nearest of the swim set's 8 sprite directions, 45° apart. Where the flume ran between two of them, the body lay up to 22° across the narrow trough and stuck out over its walls. The swimmers' renderer now turns the sliding slot's quad in its own plane, around the hotspot, by the angle left over after that snap (`GuestRenderer.RotatedSlots`, `PoolSwimSlots.Rotated`, `GuestRenderer.ResidualRotation`).
- **Angle:** the turn uses the camera's own projection, clamped to 0.6 rad.
- **Picker:** it turns the hit box the same way.
- **Scope:** upright poses never turn. Only the sliding slot is listed.
- **Effect:** presentation only; no pin moves.
