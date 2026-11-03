namespace OpenTPW;

/// <summary>
/// The animation clip of a queue model: its <c>m</c> member of the theme's queue archive (<c>queendm.md2</c>, group 5 of the original's letter table), with the frames of every texture slot its texture-frame
/// tracks drive decoded once. Presentation only.
/// </summary>
internal sealed class QueueClip
{
	/// <summary>The animation group the idle restart of an instance starts (<see cref="QueueModelAnimation.IdleGroup"/>), the letter M.</summary>
	public const int Group = QueueModelAnimation.IdleGroup;

	public required ModelAnimation Animation { get; init; }

	/// <summary>Per model texture slot a texture-frame track addresses: every frame's texture, null where no searched directory has the art.</summary>
	public required Dictionary<int, Texture?[]> Frames { get; init; }

	/// <summary>
	/// Whether this port plays the clip: its tracks only write texture coordinates and texture frames (every clip of the shipped queue archives: the torches' frame tracks, Space's scrolling coordinates). A clip with
	/// a node, vertex, path or node-flag track is not played (none ships).
	/// </summary>
	public static bool Supported( ModelAnimation clip ) => clip.NodeList.Count == 0 && clip.Tracks.All( track =>
		track.Position == null && track.Rotations.Count == 0 && track.Scales.Count == 0 && track.VertexAnimation == null && track.VertexPose == null
		&& track.PathParameters == null && track.NodeFlagToggles.Count == 0 );
}

/// <summary>The vertices of one mesh of an animated queue instance, in the instance's vertex array.</summary>
internal sealed record QueueUvMesh( int MeshIndex, int First, int Count, bool[] Composite );

/// <summary>
/// The channel of one queue instance and its idle restart: no graphics, so the cycle can be tested on its own (<see cref="QueueModelAnimation"/> draws what it samples).
/// </summary>
internal sealed class QueueClipPlayer
{
	private readonly ModelAnimation clip;

	public QueueClipPlayer( ModelFile model, ModelAnimation clip )
	{
		this.clip = clip;
		Animator = new ObjectAnimator( model );
	}

	public ObjectAnimator Animator { get; }

	/// <summary>One frame: the clock moves on by <paramref name="scaledSeconds"/>, then an idle channel (never started, or the clip past its duration) restarts the clip at the frame's clock.</summary>
	public void Step( double scaledSeconds )
	{
		Animator.Advance( scaledSeconds );
		var state = Animator.GetAnimation( 0 );
		if ( state is -1 or ObjectAnimator.NoAnimation )
			Animator.Play( 0, clip, "m", loop: false, animationId: QueueClip.Group );
	}
}

/// <summary>
/// One queue cell whose model has an animation clip, drawn as a part of its own: tp.exe gives every queue instance one channel and its idle restart (<c>0x473C70</c> from the list pass and the render callback of
/// every frame) starts the clip of group 5 whenever the channel is idle, at that frame's clock, so the clip plays over and over from the first frame after the instance's creation (the torches' fire0 to fire04 frames,
/// Space's scrolling coordinates). Each instance has its own animator, so cells made at different times are in different phases.
/// </summary>
// [BIN:TPW-EXE:0x00473C8A idle restart] 0x473C70 asks 0x473490 whether every channel is idle (the none animation 12, or its frame past the clip's duration, +0x1C) and then starts group 5 variant 0 on channel 0 with 0x4732A0(instance, 5, 0, flags, 1.0, 0) at the frame's clock (0x473C9A..0x473CAB, up to three tries); the instance flag 0x8004 (set by the creation flags 0x1 and 0x80, which 0x33A of the queue models has neither of) skips it; group 5 is the letter M (the table 0x6FE6BC), the clip file <model>m.md2 (the loader's twelve-letter pass, 0x4623A2, 0x4623D1)
// [BIN:TPW-EXE:0x004731D6 channel reset] the creator 0x463060 calls 0x473E30 (0x4631D6), which gives the instance's one channel the none animation 12 and, with its clips, sets +0xA8; an instance without any clip (+0xA8 = 0) never restarts
internal sealed class QueueModelAnimation
{
	/// <summary>The animation group of the idle restart (<c>0x473CA8</c> pushes 5): the letter M of the table <c>0x6FE6BC</c>.</summary>
	public const int IdleGroup = 5;

	private readonly QueueClipPlayer player;
	private readonly ObjectAnimator animator;
	private readonly Model drawn;
	private readonly Vertex[] vertices;
	private readonly IReadOnlyList<QueueUvMesh> uvMeshes;
	private readonly ProceduralTerrainAtlas? atlas;
	private readonly int tile;
	private readonly (int Slot, Texture?[] Frames, int[] BatchSlots)[] bindings;
	private readonly Material material;
	private readonly int[] shown;
	private readonly int[] uvVersions;
	private int textureVersion = -1;
	private bool animating = true;

	/// <summary>The instance's drawing parts over a player that outlives them: a rebuild of the queue models (a path edit) keeps the channel and its phase, as the original's instances stay while the others change.</summary>
	public QueueModelAnimation( QueueClipPlayer player, QueueClip clip, Model drawn, Vertex[] vertices, Material material, IReadOnlyList<QueueUvMesh> uvMeshes,
		(int Slot, Texture?[] Frames, int[] BatchSlots)[] bindings, ProceduralTerrainAtlas? atlas, int tile )
	{
		this.drawn = drawn;
		this.vertices = vertices;
		this.material = material;
		this.uvMeshes = uvMeshes;
		this.bindings = bindings;
		this.atlas = atlas;
		this.tile = tile;
		this.player = player;
		animator = player.Animator;
		foreach ( var (slot, frames, _) in bindings )
			animator.SetTextureFrameAvailability( slot, frames.Select( frame => frame != null ).ToArray() );
		shown = new int[bindings.Length];
		uvVersions = new int[uvMeshes.Count];
		Array.Fill( uvVersions, -1 );
	}

	/// <summary>The animator the instance plays its clip on (tests).</summary>
	internal ObjectAnimator Animator => animator;

	/// <summary>One frame: the clock moves on by the scene clock's gap, an idle channel restarts the clip at the frame's clock, and the textures and coordinates it shows are brought up to date.</summary>
	public void Step( double scaledSeconds )
	{
		player.Step( scaledSeconds );
		UpdateTextures();
		UpdateCoordinates();
	}

	/// <summary>Brings the drawn frames of the texture-frame slots up to date (the same rebinding of a material's array element an object's frame tracks use).</summary>
	private void UpdateTextures()
	{
		var enabled = OriginalObject.TextureAnimationEnabled;
		var version = animator.TextureFrameVersion;
		if ( version == textureVersion && enabled == animating )
			return;
		textureVersion = version;
		animating = enabled;
		for ( var index = 0; index < bindings.Length; index++ )
		{
			var (slot, frames, batchSlots) = bindings[index];
			var frame = OriginalObject.TextureFrameToShow( animator.GetTextureFrame( slot ), enabled );
			if ( frame == shown[index] )
				continue;
			shown[index] = frame;
			if ( frame >= frames.Length || frames[frame] is not { } texture )
				continue;
			foreach ( var batchSlot in batchSlots )
				material.SetTextureArrayElement( "Color", batchSlot, texture );
		}
	}

	/// <summary>Writes the sampled texture coordinates of the animated meshes into the instance's vertices and hands them to the GPU copy.</summary>
	private void UpdateCoordinates()
	{
		var changed = false;
		for ( var index = 0; index < uvMeshes.Count; index++ )
		{
			var uv = uvMeshes[index];
			var version = animator.GetVertexVersion( uv.MeshIndex );
			if ( version == uvVersions[index] )
				continue;
			uvVersions[index] = version;
			var coordinates = animator.GetTexCoords( uv.MeshIndex );
			if ( coordinates == null || coordinates.Count < uv.Count )
				continue;
			for ( var corner = 0; corner < uv.Count; corner++ )
			{
				var value = new Vector2( coordinates[corner].X, coordinates[corner].Y );
				vertices[uv.First + corner].TexCoords = uv.Composite[corner] && atlas != null ? atlas.PageCoordinates( tile, value ) : value;
			}
			changed = true;
		}
		if ( changed )
			drawn.UpdateVertices( vertices );
	}
}
