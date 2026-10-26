namespace OpenTPW;

/// <summary>
/// The park terrain's texture-coordinate clip: <c>basem.MD2</c> of the Jungle terrain archive (the other themes have none), AnimID 5 of <c>base.md2</c>, which scrolls the river and
/// waterfall meshes. It plays an <see cref="ObjectAnimator"/> over the terrain model and rewrites the vertices of the meshes the clip names; <see cref="OriginalTerrain"/> draws those
/// meshes as models of their own (dynamic vertices) and registers them here.
/// </summary>
// [BIN:TPW-EXE:0x54e2b0 terrain clip] the park loader 0x457a90 loads the terrain with 0x4504c0(0, …) and the state 9 of 0x54e360 registers the render callback 0x54e2b0 at renderer +0x78 (0x54ecb6); 0x576a00 (from 0x576ec0 ← 0x44e540 ← 0x54fb6c in state 10) calls it once per value of the frame counter 0x8bcc40, and it calls 0x44e510 → 0x473c70([0x79fcb4], 1, 4), which starts AnimID 5 through the idle restart (the instance has none of the flags 0x8004) and samples the UV records (0x4745c0) into the mesh records every rendered frame; the clock is the scaled clock 0x7b496c, which 0x473440 refreshes from the game timer 0x785970 (frozen by the game pause 0x4092a0, scaled by the game speed)
internal sealed class TerrainUvAnimator
{
	private readonly ModelFile terrain;
	private readonly ObjectAnimator animator;
	private readonly HashSet<int> animated = new();
	private readonly List<Target> targets = new();
	private readonly List<int> versions = new();
	private uint? lastMilliseconds;

	private readonly record struct Target( int Mesh, Vertex[] Vertices, Action<Vertex[]> Upload );

	private TerrainUvAnimator( ModelFile terrain, ModelAnimation clip, string name )
	{
		this.terrain = terrain;
		animator = new ObjectAnimator( terrain );
		animator.Trigger( 0, clip, name, animationId: 5, variant: 0, loop: true, speed: 1f );
		foreach ( var track in clip.Tracks )
			if ( track.UvAnimation != null && track.NodeIndex >= 0 && track.NodeIndex < terrain.Nodes.Count )
			{
				var mesh = terrain.Meshes.FindIndex( candidate => candidate.NodeIndex == track.NodeIndex );
				if ( mesh >= 0 && animator.GetTexCoords( mesh ) != null )
					animated.Add( mesh );
			}
	}

	/// <summary>The animator the clip plays on, for tests.</summary>
	internal ObjectAnimator Animator => animator;

	/// <summary>The terrain meshes the clip scrolls (indexes into <see cref="ModelFile.Meshes"/>).</summary>
	internal IReadOnlyCollection<int> AnimatedMeshes => animated;

	/// <summary>The animator for a terrain whose archive <paramref name="members"/> hold a <c>base</c> clip of AnimID 5 sub 0, or null.</summary>
	internal static TerrainUvAnimator? Create( ModelFile terrain, IEnumerable<string> members )
	{
		try
		{
			var files = members.Where( file => file.EndsWith( ".md2", StringComparison.OrdinalIgnoreCase ) ).ToArray();
			if ( ObjectAnimations.Resolve( ObjectAnimations.Find( "base", files ), 5, 0 ) is not { } file || new ModelFile( file.Path ).Clip is not { } clip )
				return null;
			var created = new TerrainUvAnimator( terrain, clip, file.Name );
			return created.animated.Count == 0 ? null : created;
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or NotSupportedException or InvalidOperationException )
		{
			Log?.Warning( $"Terrain clip: {exception.Message}" );
			return null;
		}
	}

	/// <summary>Whether the clip scrolls mesh <paramref name="mesh"/>.</summary>
	internal bool Animates( int mesh ) => animated.Contains( mesh );

	/// <summary>
	/// Registers the model drawn for mesh <paramref name="mesh"/>: its vertices (one per corner of the mesh, the array the model uploads) and the call that uploads them again.
	/// A mesh whose vertices are not one per corner keeps its stored coordinates.
	/// </summary>
	internal void Register( int mesh, Vertex[] vertices, Action<Vertex[]> upload )
	{
		var corners = terrain.Meshes[mesh].CornerPositionIndices.Length;
		if ( vertices.Length != corners )
		{
			Log?.Warning( $"Terrain clip: mesh {terrain.Meshes[mesh].Name} has {vertices.Length} vertices for {corners} corners; it keeps its stored coordinates." );
			return;
		}
		targets.Add( new Target( mesh, vertices, upload ) );
		versions.Add( -1 );
	}

	/// <summary>The meshes registered for drawing, for tests.</summary>
	internal int RegisteredCount => targets.Count;

	/// <summary>
	/// One frame on the scene clock (<paramref name="milliseconds"/>, the park's rate-scaled clock: it stands still while the park is paused); null reads
	/// <paramref name="fallbackSeconds"/> of frame time instead. Rewrites the vertices of every registered mesh whose coordinates changed.
	/// </summary>
	internal void Update( uint? milliseconds, double fallbackSeconds )
	{
		double seconds;
		if ( milliseconds is { } now )
		{
			seconds = lastMilliseconds is { } last ? unchecked( now - last ) / 1000.0 : 0.0;
			lastMilliseconds = now;
		}
		else
			seconds = fallbackSeconds;
		animator.Advance( seconds );
		for ( var index = 0; index < targets.Count; index++ )
		{
			var target = targets[index];
			var version = animator.GetVertexVersion( target.Mesh );
			if ( version == versions[index] )
				continue;
			ObjectRenderParts.WriteTexCoords( terrain.Meshes[target.Mesh], animator.GetTexCoords( target.Mesh ), target.Vertices );
			target.Upload( target.Vertices );
			versions[index] = version;
		}
	}
}
