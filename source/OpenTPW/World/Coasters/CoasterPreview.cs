namespace OpenTPW;

/// <summary>
/// The build cursor of the coaster tool as the renderer draws it: the hovered cell, whether a pylon there passes the validator, and the tool's height
/// and loop mode (with whether the pylon the cursor stands for is the first of the loop pair). The track is drawn as if a pylon stood there (<see cref="CoasterTrack.BuildPreviewChain"/>).
/// </summary>
public readonly record struct CoasterPreview( int X, int Y, bool Valid, float Height, bool Loop, bool FirstOfPair = false );
