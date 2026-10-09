using System.Numerics;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Plays original MD2 animation clips on an object's node hierarchy in several channels
/// (<see cref="OriginalObjectEffects"/> uses one main channel for TRIGANIM/WAITANIM/LOOPANIM/... and the
/// <c>_CH</c> opcodes' channel numbers; <c>UsageInfo.NumSimultAnims</c> allows up to 4). When several playing clips animate the same node, the
/// most recently started one wins; a finished clip holds its last pose until its channel is replaced or
/// flushed. Channel mixing, holding and the tick rate are OpenTPW choices (docs/OBJECTS.md).
/// Quantised vertex tracks of the winning clip give this instance its own mesh positions
/// (<see cref="GetVertexPositions"/>); the shared <see cref="ModelFile"/> is never written.
/// </summary>
public sealed class ObjectAnimator
{
	/// <summary>Sandbox choice shared with the Totem prototype; the original tick rate is not verified.</summary>
	// [APPROX:RIDES-001] Animation clips play at 30 ticks/s — evidence needed: original tick rate (binary or timed capture of a ride cycle)
	public const float TicksPerSecond = 30f;

	private sealed class Channel
	{
		public required ModelAnimationPlayer Player { get; init; }
		public required bool[] Animates { get; init; }
		/// <summary>Per node: the clip's vertex track when it can be played, else null.</summary>
		public required ModelVertexAnimation?[] Vertices { get; init; }
		public required string Clip { get; init; }
		public bool Loop { get; init; }
		public long Serial { get; init; }
		public double Elapsed { get; set; }
		public bool Finished { get; set; }
	}

	private readonly ModelFile model;
	private readonly Dictionary<int, Channel> channels = new();
	private readonly Matrix4x4[] rest;
	private readonly Matrix4x4[] world;
	private readonly int[] order;
	private readonly int[] meshByNode;
	private readonly NVector3[]?[] vertexPositions;
	private readonly int[] vertexVersions;
	private readonly (long Serial, float Tick)[] vertexSources;
	private readonly SortedSet<string> vertexLimitations = new( StringComparer.Ordinal );
	private long serial;

	public ObjectAnimator( ModelFile model )
	{
		ArgumentNullException.ThrowIfNull( model );
		if ( model.Kind != ModelFileKind.Geometry )
			throw new ArgumentException( "Animations play against a geometry model.", nameof( model ) );
		this.model = model;
		rest = ModelAnimationPlayer.ComputeRestTransforms( model );
		world = (Matrix4x4[])rest.Clone();
		var sorted = new List<int>( model.Nodes.Count );
		var pending = new Stack<int>();
		if ( model.RootNodeIndex >= 0 )
			pending.Push( model.RootNodeIndex );
		var children = model.Nodes.ToLookup( node => node.ParentIndex, node => node.Index );
		while ( pending.Count > 0 )
		{
			var node = pending.Pop();
			sorted.Add( node );
			foreach ( var child in children[node] )
				pending.Push( child );
		}
		// Unreachable nodes (none in the corpus) keep their rest matrices.
		order = sorted.ToArray();
		meshByNode = new int[model.Nodes.Count];
		Array.Fill( meshByNode, -1 );
		for ( var mesh = 0; mesh < model.Meshes.Count; mesh++ )
		{
			if ( model.Meshes[mesh].NodeIndex >= 0 && model.Meshes[mesh].NodeIndex < meshByNode.Length )
				meshByNode[model.Meshes[mesh].NodeIndex] = mesh;
		}
		vertexPositions = new NVector3[]?[model.Meshes.Count];
		vertexVersions = new int[model.Meshes.Count];
		vertexSources = new (long, float)[model.Meshes.Count];
	}

	public IReadOnlyList<Matrix4x4> RestTransforms => rest;
	/// <summary>Model-space node matrices of the current pose.</summary>
	public IReadOnlyList<Matrix4x4> NodeTransforms => world;
	public bool IsPlaying => channels.Values.Any( channel => !channel.Finished );
	public int ActiveChannels => channels.Count;

	/// <summary>
	/// Vertex tracks that started clips carried but that play as the stored mesh, with the reason (the
	/// 12-byte layout, relative-animation models, blocks that do not list every position once, groups
	/// that end before the clip).
	/// </summary>
	public IReadOnlyCollection<string> VertexLimitations => vertexLimitations;

	/// <summary>
	/// This instance's positions of <see cref="ModelFile.Meshes"/>[<paramref name="mesh"/>] (MD2 axes,
	/// indexed like <see cref="ModelFile.Mesh.Positions"/>), or null while the mesh shows its stored positions.
	/// </summary>
	public IReadOnlyList<NVector3>? GetVertexPositions( int mesh ) => vertexSources[mesh].Serial != 0 ? vertexPositions[mesh] : null;

	/// <summary>Changes whenever <see cref="GetVertexPositions"/> of the mesh changes.</summary>
	public int GetVertexVersion( int mesh ) => vertexVersions[mesh];

	public bool IsChannelPlaying( int channel ) => channels.TryGetValue( channel, out var state ) && !state.Finished;

	public bool IsChannelLooping( int channel, string clip ) =>
		channels.TryGetValue( channel, out var state ) && state.Loop && string.Equals( state.Clip, clip, StringComparison.OrdinalIgnoreCase );

	/// <summary>Current tick of a channel's clip, or 0.</summary>
	public float GetTick( int channel ) => channels.TryGetValue( channel, out var state ) ? state.Player.Tick : 0;

	public string? GetClip( int channel ) => channels.TryGetValue( channel, out var state ) ? state.Clip : null;

	/// <summary>Starts <paramref name="clip"/> on a channel and returns its length in seconds.</summary>
	public double Play( int channel, ModelAnimation clip, string clipName, bool loop )
	{
		var player = new ModelAnimationPlayer( model, clip, TicksPerSecond ) { Loop = loop };
		var animates = new bool[model.Nodes.Count];
		var vertices = new ModelVertexAnimation?[model.Nodes.Count];
		foreach ( var track in clip.Tracks )
		{
			animates[track.NodeIndex] = true;
			if ( (track.Flags & ModelAnimationTrack.VertexAnimationFlag) != 0 && GetVertexLimitation( track, clip.Duration ) is { } limitation )
				vertexLimitations.Add( $"{clipName} node {track.NodeIndex}: {limitation}" );
			else
				vertices[track.NodeIndex] = track.VertexAnimation;
		}
		channels[channel] = new Channel { Player = player, Animates = animates, Vertices = vertices, Clip = clipName, Loop = loop, Serial = ++serial };
		Update();
		return clip.Duration / TicksPerSecond;
	}

	public void Stop( int channel )
	{
		if ( channels.Remove( channel ) )
			Update();
	}

	public void StopAll()
	{
		channels.Clear();
		Update();
	}

	public void Advance( double seconds )
	{
		if ( !double.IsFinite( seconds ) || seconds < 0 )
			throw new ArgumentOutOfRangeException( nameof( seconds ) );
		foreach ( var channel in channels.Values )
		{
			if ( channel.Finished )
				continue;
			channel.Elapsed += seconds;
			var tick = (float)(channel.Elapsed * TicksPerSecond);
			channel.Player.SetTick( tick );
			// [APPROX:RIDES-003] A finished non-looping clip holds its last pose until replaced/flushed — evidence needed: capture after a TRIGANIM clip ends
			if ( !channel.Loop && tick >= channel.Player.Animation.Duration )
				channel.Finished = true;
		}
		Update();
	}

	private string? GetVertexLimitation( ModelAnimationTrack track, int duration )
	{
		if ( track.VertexAnimation == null )
			return "the 12-byte vertex layout (record flag 0x4000) is not decoded";
		// The sampler adds instead of sets under this flag (0xa4a58/0xa4f68). The ride loader strips it unless
		// an untraced ride flag word allows it (0x58a3c), and what resets the summed positions was not traced.
		if ( (model.HeaderFlags & ModelFile.RelativeAnimationFlag) != 0 )
			return "relative-animation model (header flag 0x4) adds vertex keys; its reset is not traced";
		var mesh = meshByNode[track.NodeIndex];
		if ( mesh < 0 )
			return "the node has no mesh";
		return track.VertexAnimation!.GetPoseLimitation( model.Meshes[mesh].Positions.Length, duration );
	}

	private void Update()
	{
		// [APPROX:RIDES-002] When several channels animate a node, the most recently started clip wins — evidence needed: original channel mixing (binary or capture of a multi-channel sideshow/Totem)
		var active = channels.Values.OrderByDescending( channel => channel.Serial ).ToArray();
		foreach ( var node in order )
		{
			var local = model.Nodes[node].Transform;
			Channel? winner = null;
			foreach ( var channel in active )
			{
				if ( channel.Animates[node] )
				{
					winner = channel;
					local = channel.Player.LocalTransform( node );
					break;
				}
			}
			var parent = model.Nodes[node].ParentIndex;
			world[node] = parent < 0 ? local : local * world[parent];
			if ( meshByNode[node] >= 0 )
				UpdateVertices( meshByNode[node], winner, winner?.Vertices[node] );
		}
	}

	/// <summary>
	/// The winning clip's vertex track sets every position of the mesh (set mode, static group plus
	/// animated groups at the clip tick). Without one the mesh shows its stored positions, as the original
	/// copies them back from the base model when a clip that animated them is replaced (0xa5894).
	/// </summary>
	private void UpdateVertices( int mesh, Channel? channel, ModelVertexAnimation? animation )
	{
		var source = animation == null ? (0L, 0f) : (channel!.Serial, channel.Player.Tick);
		if ( source == vertexSources[mesh] )
			return;
		if ( animation != null )
		{
			var positions = vertexPositions[mesh] ??= new NVector3[model.Meshes[mesh].Positions.Length];
			animation.ApplyPose( channel!.Player.Tick, positions );
		}
		vertexSources[mesh] = source;
		vertexVersions[mesh]++;
	}
}
