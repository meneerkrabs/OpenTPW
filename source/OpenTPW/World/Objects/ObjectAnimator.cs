using System.Numerics;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Plays original MD2 animation clips on an object's node hierarchy in several channels
/// (<see cref="OriginalObjectEffects"/> uses one main channel for TRIGANIM/WAITANIM/LOOPANIM/... and the
/// <c>_CH</c> opcodes' channel numbers; <c>UsageInfo.NumSimultAnims</c> allows up to 4). When several playing clips animate the same node, the
/// most recently started one wins; a finished clip holds its last pose until its channel is replaced or
/// flushed. Channel mixing, holding and the tick rate are OpenTPW choices (docs/OBJECTS.md).
/// Clip time follows the original channel clock (0xa6484/0xa6398/0xa67d8 in the Feral Mac build): a clip
/// starts at a whole millisecond of this animator's clock, its frame is <c>30 × elapsed ms / 1000</c> in
/// single precision, and a looping clip replays only once that frame is strictly past the duration,
/// restarting from the carry capped at the duration and truncated to whole milliseconds (one replay per
/// <see cref="Advance"/>). This is the normal object update (0xa7960 with r4 ≠ 0), whose replay rebinds
/// the clip; the object-list update (0x4d354, r4 = 0) replays without a bind and is not supported.
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
		/// <summary>StartAnimTime (channel +16): animator clock in whole milliseconds.</summary>
		public long StartMilliseconds { get; set; }
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
	private double clockMilliseconds;

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
			var node = model.Meshes[mesh].NodeIndex;
			if ( node < 0 || node >= meshByNode.Length )
				continue;
			// The sampler's node state is the header's mesh record, so a track drives exactly one mesh; a
			// parsed model gives mesh i node i. Two meshes on one node would leave one without its track.
			if ( meshByNode[node] >= 0 )
				throw new ArgumentException( $"Meshes {meshByNode[node]} and {mesh} share node {node}; vertex tracks bind one mesh per node.", nameof( model ) );
			meshByNode[node] = mesh;
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
		// The channel wraps the frame itself; the player only clamps.
		var player = new ModelAnimationPlayer( model, clip, TicksPerSecond ) { Loop = false };
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
		channels[channel] = new Channel
		{
			Player = player, Animates = animates, Vertices = vertices, Clip = clipName, Loop = loop, Serial = ++serial,
			StartMilliseconds = NowMilliseconds
		};
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

	/// <summary>One object update: advances the clock, replays or finishes clips past their end, resamples.</summary>
	public void Advance( double seconds )
	{
		if ( !double.IsFinite( seconds ) || seconds < 0 )
			throw new ArgumentOutOfRangeException( nameof( seconds ) );
		clockMilliseconds += seconds * 1000.0;
		var now = NowMilliseconds;
		foreach ( var channel in channels.Values )
		{
			if ( channel.Finished )
				continue;
			var frame = GetFrame( now - channel.StartMilliseconds );
			float duration = channel.Player.Animation.Duration;
			// 0xa7360: past the end only when strictly greater, so a frame equal to the duration samples the last key.
			if ( frame > duration )
			{
				if ( channel.Loop )
				{
					channel.StartMilliseconds = now - GetCarryMilliseconds( frame - duration, duration );
					frame = GetFrame( now - channel.StartMilliseconds );
				}
				// [APPROX:RIDES-003] A finished non-looping clip holds its last pose until replaced/flushed — evidence needed: capture after a TRIGANIM clip ends
				else
					channel.Finished = true;
			}
			channel.Player.SetTick( frame );
		}
		Update();
	}

	/// <summary>
	/// The original's integer scene clock (0xa6f70 stores whole milliseconds); the epsilon absorbs binary
	/// sums of decimal step sizes. How the scene clock itself rounds is a clock-lane dependency.
	/// </summary>
	private long NowMilliseconds => (long)Math.Floor( clockMilliseconds + 1e-6 );

	/// <summary>0xa6484 at speed 1.0: <c>30 × (now − start) / 1000</c>, each step in single precision.</summary>
	private static float GetFrame( long milliseconds )
	{
		var elapsed = (float)milliseconds;
		var ticks = TicksPerSecond * elapsed;
		return ticks / 1000f;
	}

	/// <summary>
	/// 0xa67d8 → 0xa6398 at speed 1.0: the carry past the end, capped at the duration, becomes
	/// <c>1000 × carry / 30</c> milliseconds truncated by the unsigned conversion 0x1c3fbc.
	/// </summary>
	private static long GetCarryMilliseconds( float carry, float duration )
	{
		if ( carry > duration )
			carry = duration;
		var milliseconds = 1000f * carry;
		milliseconds /= TicksPerSecond;
		return (long)milliseconds;
	}

	private string? GetVertexLimitation( ModelAnimationTrack track, int duration )
	{
		if ( track.VertexAnimation == null )
			return "the 12-byte vertex layout (record flag 0x4000) is not decoded";
		// The sampler adds instead of sets under this flag (0xa4a58/0xa4f68); each update first restores the
		// clip's channels from the base model (0xa7960 -> 0xa5894 without a new clip) and afterwards
		// recomputes face normals (0xa772c). Not implemented; the ride loader strips the flag unless an
		// untraced ride flag word allows it (0x58a3c), and no catalog model has it.
		if ( (model.HeaderFlags & ModelFile.RelativeAnimationFlag) != 0 )
			return "relative-animation model (header flag 0x4) adds vertex keys to a per-update base copy; not implemented";
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
	/// animated groups at the clip tick). A fresh key search per sample equals the original's kept cursor
	/// because its loop replay rebinds the clip (0xa7190 -> 0xa67d8 -> 0xa5894), which resets the cursor;
	/// the object-list update (0x4d354, flag 8) replays without a bind and is not modelled.
	/// Without a track the mesh shows its stored positions (proposed register entry RIDES-030). The
	/// original copies them back on a clip change unless global option bit 0 is set or the object has flag
	/// 0x00100000 with 0x8 clear, and always for header flag 0x4 (0xa5894). The option is only ever stored
	/// as 0 (0xa7eec from 0x54c08); the object flags come from the creator's ride flags (0x594c8 -> 0x58a3c),
	/// and only the ride catalog loader 0x119328 sets 0x00100000 without 0x8, for descriptors whose +56 word
	/// is non-zero. Which catalog entries those are is not traced, so for them the last pose would stay.
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
