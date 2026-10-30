namespace OpenTPW;

/// <summary>
/// How the original maps a sprite frame to the world: one pixel of a frame, in the original's world units and in engine units, for the camera-facing
/// sprites (kids, staff, balloons, thoughts, heads, the upgrade balloon) and for the flat ground decals (litter). Pure rules: no assets, no GPU.
/// </summary>
public static class SpriteScale
{
	/// <summary>Sprite units per frame pixel count (the frame header's two unit words, 128 in every shipped frame): a frame spans <c>pixels / 128</c> sprite units.</summary>
	public const float PixelsPerSpriteUnit = 128f;
	/// <summary>How many extent units one sprite unit at scale 1 spans (tp.exe data <c>0x0070200C</c>, read by the quad placement <c>0x005890E0</c> and its twin <c>0x00589410</c>).</summary>
	// [BIN:TPW-EXE:0x005890E0 sprite placement] a quad's x extents are originX / A and (width + originX) / A times sx and its y extents originY / B and (height + originY) / B times sy; both default to the sprite's scale (+0xA4, 1.0 at creation, 0x004758F0) times 20.0 (0x0070200C), sx also times the display's height / width (DAT_008BCBCC, 0x00582EB0)
	public const float ExtentPerSpriteUnit = 20f;
	/// <summary>The half field of view of the original's camera (0x0042AA2A pushes 90 degrees to the projection builder 0x00578BE0): 45 degrees.</summary>
	public const float HalfFieldOfView = MathF.PI / 4;
	/// <summary>
	/// The camera-facing sprite's extents are added to the clip-space position, and the original's projection has x and y clip = view × cos(half fov) with w = z × sin(half fov)
	/// (0x00578BE0: the matrix words +0x00 and +0x14 are cos(a), +0x2C is sin(a)), so one extent unit is 1 / cos(45 degrees) view-space units.
	/// </summary>
	// [BIN:TPW-EXE:0x00578BE0 projection] m00 = m11 = cos(fov / 2), m23 = sin(fov / 2): a clip-space extent e is e / cos(fov / 2) = e × √2 world units at 90 degrees (the relation the particle quads use, docs/PARTICLES.md)
	public static readonly float ClipToView = 1f / MathF.Cos( HalfFieldOfView );
	/// <summary>World units (the original's, ten per map cell) of one pixel of a camera-facing sprite at scale 1.</summary>
	public static readonly float BillboardPixel = ExtentPerSpriteUnit / PixelsPerSpriteUnit * ClipToView;
	/// <summary>Engine units of one pixel of a camera-facing sprite at scale 1 (the original's world units times <see cref="ObjectPlacement.ModelScale"/>).</summary>
	public static readonly float EngineUnitsPerPixel = BillboardPixel * ObjectPlacement.ModelScale;

	/// <summary>The near and far planes the park camera gives the projection builder (<c>0x0042A990</c> to <c>0x00578BE0</c>), in the original's world units.</summary>
	public const float ProjectionNear = 0.1f;
	/// <inheritdoc cref="ProjectionNear"/>
	public const float ProjectionFar = 1000f;

	/// <summary>
	/// The share of its view depth a visitor's or a staff member's sprite is drawn at. The original pulls these sprites towards the camera in depth once
	/// their quad is placed: the quad keeps its place and size on screen, and the ground in front of the hotspot, which a sprite's feet hang over when the
	/// camera looks down, no longer covers them. <paramref name="viewDepth"/> is the hotspot's view depth in the original's world units; at the park view's
	/// depths (40 to 250 units) the pull is 3.5 to 3.9 units (0.7 to 0.8 engine units), which keeps soles up to 16 pixels below the hotspot clear of the ground at
	/// the lowest camera pitch, 45 degrees.
	/// </summary>
	// [BIN:TPW-EXE:0x00589B50 sprite depth pull] after 0x005890E0 places a camera-facing quad, a sprite flagged 0x200 (the pickable ones: 0x005890E0's pick tests that flag, and it is how the original
	// selects visitors and staff, UI-930; the shadow copies 0x00475CF0 and 0x00475E10 clear it) has the clip z of all four vertices moved to z - 0.2 / (z + 1), unless engine flag 0x2000 is set
	// (only the front end's 3D scenes set it, through 0x005D9680); x, y and w stay. The projection 0x00578BE0 gives z = a (v - n) and w = s v (s = sin 45 degrees, a = s / (1 - n / f),
	// the screen matrix 0x0087B0D0 leaves both), so the pulled depth is that of the view depth v / (1 + 0.2 / (n a (a (v - n) + 1)))
	public static float PulledDepthShare( float viewDepth )
	{
		if ( !(viewDepth > ProjectionNear) )
			return 1f;
		var a = MathF.Sin( HalfFieldOfView ) / (1f - ProjectionNear / ProjectionFar);
		return 1f / (1f + 0.2f / (ProjectionNear * a * (a * (viewDepth - ProjectionNear) + 1f)));
	}

	/// <summary>
	/// The display's height over its width, which the flat quad's width takes (DAT_008BCBCC, 0x00582EB0): 0.75 in every 4:3 mode the original offers (640 × 480, 800 × 600, 1024 × 768).
	/// OpenTPW draws the flat decals with that value whatever the window.
	/// </summary>
	public const float FlatDisplayAspect = 0.75f;
	/// <summary>World units (ten per cell) of one pixel across a flat ground decal at scale 1.</summary>
	public static float FlatPixelX => ExtentPerSpriteUnit / PixelsPerSpriteUnit * FlatDisplayAspect;
	/// <summary>World units of one pixel down a flat ground decal at scale 1 (along the map's y axis).</summary>
	public static float FlatPixelZ => ExtentPerSpriteUnit / PixelsPerSpriteUnit;
	/// <summary>How far above the ground height a flat decal lies, in world units (the display's pixel offset word +0x924 = 0.5, DAT_008BD4EC, added to every vertex's height at <c>0x00589352</c>).</summary>
	public const float FlatLift = 0.5f;

	/// <summary>A flat decal frame's four edges around its hotspot in world units: left and right along the map's x axis, top and bottom along its y axis (the top edge is the lower y).</summary>
	// [BIN:TPW-EXE:0x00589320 flat sprite] flag 0x80000: the quad's y extents go to the vertices' z (the map's y axis) instead of their height, v0 and v1 (the frame's top) take originY / B, v2 and v3 (the bottom) (height + originY) / B, v0 and v3 (the left) originX / A, v1 and v2 (the right) (width + originX) / A
	public static (float Left, float Right, float Top, float Bottom) FlatEdges( GuestAtlasFrame frame, float scale = 1f )
	{
		var pixelX = ExtentPerSpriteUnit * FlatDisplayAspect * scale / PixelsPerSpriteUnit;
		var pixelZ = ExtentPerSpriteUnit * scale / PixelsPerSpriteUnit;
		return (frame.OriginX * pixelX, (frame.Width + frame.OriginX) * pixelX, frame.OriginY * pixelZ, (frame.Height + frame.OriginY) * pixelZ);
	}
}
