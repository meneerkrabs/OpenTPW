# Rider-head reference basis

The reference basis of the head roll, from static inspection of the PC TPW reference
(SHA-256 `5472f97b5eb28f58b5a49f3a15f6f66adffa6b2625a623f503202c9696d1b805`), and since round 11
the routine's arithmetic itself: `OriginalHeadMath` reproduces `0x44B510` operation by operation (single
operations in the order the x87 code runs them, the engine's LU matrix inverse `0x470040`, the product
`0x4702D0`, the frame walk through the sine table, `fpatan`) and `HeadPlacementGoldenTests` replays 511
cases recorded from the routine run in the Unicorn oracle (`tools/ppc-analysis/oracle_x86/head_golden.py`,
node and camera matrices synthetic, no original data) bit for bit: the frame and the roll. HEADS-004 is retired
on that evidence; the sections below are the earlier static reading the port started from.

## Camera-to-node matrix

After the existing mirror operation, `0x44B637` inverts the head node matrix.
`0x44B65C` multiplies camera world by that inverse. Its translation is the camera
position in node coordinates and supplies the existing frame-selection direction.
The pointer `0x790A98` is assigned to camera matrices at `0x42CCD7` / `0x42CCFA`;
`0x42CD18` separately inverts that matrix for rendering. It is not already a view.

The matrix helper `0x4702D0` mutates its second argument to first times second
(row-vector layout). For example, its first result at `0x470318` is
`a00*b00 + a01*b10 + a02*b20`. The general inverse at `0x470040` decomposes the
matrix and solves four unit-vector right-hand sides; it is not just a transpose.

## Selected art frame

Frame selection remains the greatest dot product in row-major order, with first
winner on a tie and column zero at the two poles. The native loop retains the
winning direction D at `0x44B718..0x44B72D`. For row r and column c, the mathematical
direction and pre-rendered up vector are:

- theta = r * pi/6, phi = c * pi/4;
- D = (-sin(theta) sin(phi), cos(theta), sin(theta) cos(phi));
- U = (sin(phi) cos(theta), sin(theta), -cos(phi) cos(theta)).

`0x44B82F..0x44B930` rotates U from (0, 0, -1). `0x44B932..0x44B969`
forms R = U cross D. `0x46FBB0` puts R, U, D in rows zero, one, two of an identity
matrix B. `0x44B99D` inverts B, and `0x44B9C2` forms:

`remaining = cameraWorld * inverse(nodeWorld) * inverse(B)`.

`0x46F200` returns `atan2(-remaining.M21, remaining.M22)` and `0x44BA04`
converts it to turns for sprite register 9. Taking the angle directly from the
node's world axes loses both camera orientation and the chosen art frame.

## Renderer conversion

OpenTPW's engine is Z-up and its System.Numerics view is right-handed. The
billboard right/up vectors are view columns zero/one; its forward vector is the
negative third column. `CameraFromEngineView` swaps world Y/Z on these three
vectors and the camera position, yielding native right/up/front rows in MD2
Y-up coordinates. It does not swap the camera's local axes or feed a view matrix
where camera world is required. The billboard and roll therefore use the same
camera basis, including camera bank.

## Verification boundary

Synthetic tests construct the reference basis independently as X then Y rotation,
cover all 7 by 8 frame inputs with three node transforms (including nonuniform
scale), mirrored and ordinary nodes, and five camera-bank values. Separate tests
exercise the actual engine-view conversion, position-equal/orientation-different
cameras, poles, hidden nodes and finite defensive fallback. The older world-bank
expectations were expectations of the removed approximation, not native captures.

The native loop uses iterated sine-table rotations and x87 intermediates; the port now
runs them the same way (see the top of this file). Only the finite fallback is the host's: a camera
offset that is not a finite direction leaves frame 0 and a roll of 0 where the original rolls by stack words it never wrote
([EXT:original-undefined-reads]). No rendered-image acceptance is claimed beyond the registers.
