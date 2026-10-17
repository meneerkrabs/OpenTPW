## The camera panel loses its invented view-cycle button (October 17, 2026)

- **What the original has.** The HUD camera button (`b_camera`, 474, arm child `0x27`) only slides out the panel of table `0x751720`, which holds exactly two buttons: the camcorder
  (`b_1person`, 99) and the postcard (`b_postcard`, 100). No other window control changes the camera's zoom (`0x7909ec`) or yaw (`0x790a38`): the object windows (`0x755150`, `0x755908`,
  `0x755e80`) carry only `b_ride it!` (the ride view, UI-094), and the visitor and employee windows (`0x7575e0`, `0x756f48`) carry `b_locate`. Every locate and map jump
  (`0x4867b0`, `0x486860`, `0x4868d0`, the minimap click `0x5f1050`) calls the focus setter `0x42aab0` with the zoom and yaw arguments null, so it moves only the target. The zoom
  changes only through the mouse wheel and keys (`0x42a760`, `0x42b1c0`, clamped to 70..130) and `0x42a130` (Home), and it is saved with the park as `gf_CameraZoom` and `gf_YRotation` (`0x42cec0`).
- **What changed.** `[EXT:camera-view-cycle]` is retired by removal: the third round button next to the camcorder and postcard buttons, `ParkCameraMode.CycleView`, the
  `ParkCameraView` enum and the 70/110/130 stops are gone, and the panel's background is the table's width. A test pins the original's rule that a focus changes only the target.
- **Left open.** Nothing about this control; UI-042's remaining parts (panel end position, camcorder walk controls, postcard photo) are unchanged.
