namespace OpenTPW;

/// <summary>
/// The build cursor of the coaster tool as the renderer draws it: the hovered cell, whether a pylon there passes the validator, and the tool's height
/// and loop mode. The track is drawn as if a pylon stood there (<see cref="CoasterTrack.BuildPreviewChain"/>).
/// </summary>
public readonly record struct CoasterPreview( int X, int Y, bool Valid, float Height, bool Loop );
