namespace OpenTPW;

/// <summary>
/// The build cursor of the coaster tool as the renderer draws it: the hovered cell, whether a pylon there passes the validator, and the tool's height
/// and loop mode (with whether the pylon the cursor stands for is the first of the loop pair). The track is drawn as if a pylon stood there (<see cref="CoasterTrack.BuildPreviewChain"/>).
/// </summary>
public readonly record struct CoasterPreview( int X, int Y, bool Valid, float Height, bool Loop, bool FirstOfPair = false );

/// <summary>
/// The links the coaster tool marks while a range is shown: the nodes <see cref="Low"/> up to but excluding <see cref="High"/> carry node flag <c>0x1000000</c>, which the mesh builder draws
/// with the marker texture variant <see cref="Variant"/> (<c>[0x7925d0]</c>: 1 = m_erase for the demolish range, 3 = blue for a moved pylon's links).
/// </summary>
// [BIN:TPW-EXE:0x444090 range marks] 0x444090 sets 0x1000000 on every node from the lower end up to the higher one (exclusive, in chain order); 0x444040 clears the flag (with 0x4000) on the same range; 0x43f4e0 draws a marked link with the marker variant [0x7925d0] (red when the node is also marked 0x4000)
public readonly record struct CoasterRangeMark( int Low, int High, int Variant );
