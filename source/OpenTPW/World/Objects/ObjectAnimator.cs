using System.Numerics;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Plays original MD2 animation clips on an object's node hierarchy in several channels, after the channel
/// records of the Feral Mac build (<c>object +16 + channel × 56</c>, trigger 0xa6cc0, start 0xa67d8, channel
/// update 0xa7190, object update 0xa7960; docs/reverse/ANIM-plan.md). Each channel holds an animation id
/// (<see cref="NoAnimation"/> when empty), its clip, loop bit, speed, start time and a deferred clip.
/// Channels update in ascending index and each samples its clip in set mode, so for every node component
/// (translation, rotation, scale, the mesh's positions and texture coordinates) the highest channel that
/// writes it wins. A non-looping clip past its end becomes <see cref="EndedAnimation"/>: it stays frozen on
/// its last frame, keeps its pose and reports "ended" (<see cref="GetAnimation"/> = −1).
/// Clip time follows the channel clock (0xa6484/0xa6398/0xa67d8): a clip starts at a whole millisecond of
/// this animator's clock, its frame is <c>rate × elapsed ms / 1000 × speed</c> in single precision (the
/// original's rate is the constant 30, <see cref="DefaultTicksPerSecond"/>; other rates are an OpenTPW
/// extension), and a looping clip replays only once that frame is strictly past the duration, restarting from
/// the carry capped at the duration and truncated to whole milliseconds (one replay per <see cref="Advance"/>).
/// This is the normal object update (0xa7960 with r4 ≠ 0), whose replay rebinds the clip.
/// Quantised vertex tracks and texture-coordinate tracks give this instance its own mesh positions and
/// texture coordinates (<see cref="GetVertexPositions"/>, <see cref="GetTexCoords"/>); the shared
/// <see cref="ModelFile"/> is never written.
/// </summary>
// [APPROX:RIDES-032] Every instance takes the placement callback route (0xa7960 with r4 = 1) on every update; the object-list route (0x4d354, r4 = 0: clocks advance without posing unless object flag 0x00400000, loops replay without a bind and keep their vertex cursors) is not modelled — evidence needed: the engine's condition for calling the render-record callback installed at 0x5a3bc, and the setter of object flag 0x00400000
// [APPROX:RIDES-034] No idle restart: the original object update re-triggers AnimID 5, sub 0 on channel 0 while channel 0 has ended for objects whose +4 & 0x8004 is clear (0xa798c–0xa79c8) — evidence needed: which placed objects keep +4 & 0x8004 clear (placement-flag sources)
// [APPROX:RIDES-035] MD2 texture-frame tracks (trailer word 12) are decoded but not applied; the clip update 0xa56f8 applies them when trailer bit 0x2 is set and global option bit 0x8 is clear (its only store writes 0), but the consumer of the selected frame slot is not traced — evidence needed: the reader of the texture slot's current frame in the draw path
public sealed class ObjectAnimator
{
	/// <summary>Default rate shared with the Totem; 30 is proved for the Feral Mac channel formula, while Windows and runtime clock selection remain unverified.</summary>
	// [BIN:STP-PPC:0x100A70B4 channel frame arithmetic] the Feral Mac formula multiplies elapsed milliseconds by 30 and divides by 1000 before channel speed.
	// [APPROX:RIDES-001] Mac channel rate 30 is proved; Windows rate and selection/binding of the native scaled or unscaled channel clock remain unverified.
	public const float DefaultTicksPerSecond = 30f;

	/// <summary>Channel AnimID / deferred-slot sentinel for "no clip" (0xa6ca8).</summary>
	public const int NoAnimation = 12;
	/// <summary>AnimID a non-looping clip restarts as when it ends without a deferred clip (0xa7498).</summary>
	public const int EndedAnimation = 14;
	/// <summary>What the trigger reports for a clip that does not exist (0xa6f54) or a missing deferred clip (0xa6f04).</summary>
	public const int MissingClipMilliseconds = 1000;
	/// <summary>Remaining-time factor of 0xa65a0/0xa6e0c: <c>(total − frame) × 33.333332</c> ms.</summary>
	public const float RemainingMillisecondsPerTick = 33.333332f;

	private sealed class Clip
	{
		public required ModelAnimation Animation { get; init; }
		public required string Name { get; init; }
		public required int AnimationId { get; init; }
		public required int Variant { get; init; }
		public required bool Loop { get; init; }
		public required float Speed { get; init; }
	}

	private sealed class Channel
	{
		public required Clip Clip { get; set; }
		public required ModelAnimationPlayer Player { get; set; }
		/// <summary>Per node: the clip's record, or null.</summary>
		public required ModelAnimationTrack?[] Tracks { get; set; }
		/// <summary>Per node: the clip's vertex track when it can be played, else null.</summary>
		public required ModelVertexAnimation?[] Vertices { get; set; }
		/// <summary>Per node: the clip's texture-coordinate track when it fits the node's mesh, else null.</summary>
		public required ModelUvAnimation?[] Uvs { get; set; }
		/// <summary>Bumped at every start and replay; tells the vertex caches that the clip was rebound.</summary>
		public long Serial { get; set; }
		/// <summary>StartAnimTime (channel +16): animator clock in whole milliseconds.</summary>
		public long StartMilliseconds { get; set; }
		/// <summary>AnimID 14 semantics: frozen at the last frame (channel flags 0x14), still posing.</summary>
		public bool Ended { get; set; }
		/// <summary>DeferredAnimID/SubAnim/flags (+36/+40/+44) and speed; null = 12 (none).</summary>
		public Clip? Deferred { get; set; }
	}

	private readonly ModelFile model;
	private readonly SortedDictionary<int, Channel> channels = new();
	private readonly Matrix4x4[] rest;
	private readonly (NVector3 Scale, Quaternion Rotation, NVector3 Translation)[] restComponents;
	private readonly Matrix4x4[] world;
	private readonly int[] order;
	private readonly int[] meshByNode;
	private readonly NVector3[]?[] vertexPositions;
	private readonly NVector2[]?[] texCoords;
	private readonly int[] vertexVersions;
	private readonly (long Serial, float Tick)[] vertexSources;
	private readonly (long Serial, float Tick)[] uvSources;
	private readonly bool[] nodeStateBit;
	private readonly SortedSet<string> vertexLimitations = new( StringComparer.Ordinal );
	private readonly bool keepsPoseOnClipChange;
	private long serial;
	private double clockMilliseconds;
	private int hiddenVersion;

	/// <summary>Clip ticks per second used by every channel this animator plays.</summary>
	public float TicksPerSecond { get; }

	/// <param name="model">The object's geometry model, shared and never written.</param>
	/// <param name="ticksPerSecond">Clip ticks per second of every channel; the original's clock uses 30.</param>
	/// <param name="keepsPoseOnClipChange">
	/// True for fixed items (<c>Info.DontApplyOffset</c>): a mesh keeps its last vertex pose and texture
	/// coordinates when its track ends instead of showing its stored ones.
	/// </param>
	public ObjectAnimator( ModelFile model, float ticksPerSecond = DefaultTicksPerSecond, bool keepsPoseOnClipChange = false )
	{
		ArgumentNullException.ThrowIfNull( model );
		if ( model.Kind != ModelFileKind.Geometry )
			throw new ArgumentException( "Animations play against a geometry model.", nameof( model ) );
		if ( !float.IsFinite( ticksPerSecond ) || ticksPerSecond <= 0 )
			throw new ArgumentOutOfRangeException( nameof( ticksPerSecond ) );
		this.model = model;
		TicksPerSecond = ticksPerSecond;
		// The bind copies the stored mesh back always under header flag 0x4, but the ride loader strips that flag on the
		// default load path (see GetVertexLimitation), so the fixed-item gate decides for every model.
		this.keepsPoseOnClipChange = keepsPoseOnClipChange;
		rest = ModelAnimationPlayer.ComputeRestTransforms( model );
		world = (Matrix4x4[])rest.Clone();
		restComponents = new (NVector3, Quaternion, NVector3)[model.Nodes.Count];
		for ( var index = 0; index < restComponents.Length; index++ )
		{
			if ( !Matrix4x4.Decompose( model.Nodes[index].Transform, out var scale, out var rotation, out var translation ) )
			{
				scale = NVector3.One;
				rotation = Quaternion.Identity;
				translation = model.Nodes[index].Transform.Translation;
			}
			restComponents[index] = (scale, rotation, translation);
		}
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
		texCoords = new NVector2[]?[model.Meshes.Count];
		vertexVersions = new int[model.Meshes.Count];
		vertexSources = new (long, float)[model.Meshes.Count];
		uvSources = new (long, float)[model.Meshes.Count];
		// Node-state bit 0x10 starts as stored (clear in every corpus mesh record).
		nodeStateBit = model.Nodes.Select( node => (node.Type & ModelAnimationTrack.ToggledNodeStateBit) != 0 ).ToArray();
	}

	public IReadOnlyList<Matrix4x4> RestTransforms => rest;
	/// <summary>Model-space node matrices of the current pose.</summary>
	public IReadOnlyList<Matrix4x4> NodeTransforms => world;
	/// <summary>True while some channel plays a clip that has not ended.</summary>
	public bool IsPlaying => channels.Values.Any( channel => !channel.Ended );
	public int ActiveChannels => channels.Count;

	/// <summary>
	/// Vertex and texture-coordinate tracks that started clips carried but that play as the stored mesh, with
	/// the reason (the 12-byte block, blocks that do not list every position once, groups that end before the
	/// clip, UV blocks whose corner count differs from the mesh).
	/// </summary>
	public IReadOnlyCollection<string> VertexLimitations => vertexLimitations;

	/// <summary>
	/// This instance's positions of <see cref="ModelFile.Meshes"/>[<paramref name="mesh"/>] (MD2 axes,
	/// indexed like <see cref="ModelFile.Mesh.Positions"/>), or null while the mesh shows its stored positions.
	/// </summary>
	public IReadOnlyList<NVector3>? GetVertexPositions( int mesh ) => vertexSources[mesh].Serial != 0 ? vertexPositions[mesh] : null;

	/// <summary>
	/// This instance's texture coordinates of a mesh (indexed like <see cref="ModelFile.Mesh.TexCoords"/>), or
	/// null while the mesh shows its stored ones.
	/// </summary>
	public IReadOnlyList<NVector2>? GetTexCoords( int mesh ) => uvSources[mesh].Serial != 0 ? texCoords[mesh] : null;

	/// <summary>Changes whenever <see cref="GetVertexPositions"/> or <see cref="GetTexCoords"/> of the mesh changes.</summary>
	public int GetVertexVersion( int mesh ) => vertexVersions[mesh];

	/// <summary>
	/// Node-state bit 0x10 of a node: set by a clip's node list at its start and by toggle entries ≤ 0, cleared by
	/// entries &gt; 0 (0xa65b8, 0xa4f68).
	/// </summary>
	public bool GetNodeStateBit( int node ) => nodeStateBit[node];

	/// <summary>
	/// True when the mesh on <paramref name="node"/> is drawn hidden: its node-state bit 0x10 is set.
	/// </summary>
	// [APPROX:RIDES-030] Node-state bit 0x10 set (clip node lists at start, toggle entries ≤ 0) hides the node's mesh; the draw consumer of the bit was not found (the object save persists it, the advisor lists hats and accessories, Fireworks list their Destroyed mesh) — evidence needed: the reader of node bit 0x10 in the draw path, or captures
	public bool IsNodeHidden( int node ) => meshByNode[node] >= 0 && nodeStateBit[node];

	/// <summary>Changes whenever <see cref="IsNodeHidden"/> of some node changes.</summary>
	public int HiddenVersion => hiddenVersion;

	/// <summary>True while the channel's clip has not ended.</summary>
	public bool IsChannelPlaying( int channel ) => channels.TryGetValue( channel, out var state ) && !state.Ended;

	/// <summary>
	/// The channel's AnimID as the query 0xa7dcc reads it: <see cref="NoAnimation"/> without a clip,
	/// <see cref="EndedAnimation"/> once a non-looping clip ended, otherwise the triggered animation.
	/// </summary>
	public int GetAnimationId( int channel ) => !channels.TryGetValue( channel, out var state ) ? NoAnimation
		: state.Ended ? EndedAnimation : state.Clip.AnimationId;

	/// <summary>GETANIM / GETANIM_CH (0xaff80, 0xb0614): −1 once the channel's clip ended (flag 0x4), else its AnimID.</summary>
	public int GetAnimation( int channel ) => channels.TryGetValue( channel, out var state ) && state.Ended ? -1 : GetAnimationId( channel );

	/// <summary>The deferred AnimID of a channel (<see cref="NoAnimation"/> when none), as read by 0xa7e00.</summary>
	public int GetDeferredAnimationId( int channel ) => channels.TryGetValue( channel, out var state ) && state.Deferred != null ? state.Deferred.AnimationId : NoAnimation;

	/// <summary>Current tick of a channel's clip, or 0.</summary>
	public float GetTick( int channel ) => channels.TryGetValue( channel, out var state ) ? state.Player.Tick : 0;

	public string? GetClip( int channel ) => channels.TryGetValue( channel, out var state ) ? state.Clip.Name : null;

	public float GetSpeed( int channel ) => channels.TryGetValue( channel, out var state ) ? state.Clip.Speed : 0;

	/// <summary>Starts <paramref name="clip"/> on a channel at once and returns its length in seconds.</summary>
	public double Play( int channel, ModelAnimation clip, string clipName, bool loop, float speed = 1f, int animationId = -1, int variant = 0 )
	{
		Trigger( channel, clip, clipName, animationId, variant, loop, speed );
		return clip.Duration / TicksPerSecond;
	}

	/// <summary>
	/// The trigger 0xa6cc0 on one channel; returns the milliseconds it reports to the caller. With
	/// <paramref name="defer"/> (flag 0x2), a channel whose clip still runs (not ended, frame ≤ total) keeps it
	/// and queues this one: the result is the running clip's remaining time plus this clip's length. Otherwise
	/// the clip replaces the channel's clip at once and the result is its length (<see cref="StartLengthMilliseconds"/>,
	/// never scaled by <paramref name="speed"/>). A null <paramref name="clip"/> (no such member) leaves the
	/// channel unchanged and reports <see cref="MissingClipMilliseconds"/>.
	/// </summary>
	public int Trigger( int channel, ModelAnimation? clip, string clipName, int animationId, int variant, bool loop, float speed = 1f, bool defer = false )
	{
		if ( channel < 0 )
			throw new ArgumentOutOfRangeException( nameof( channel ) );
		if ( !float.IsFinite( speed ) || speed <= 0 )
			throw new ArgumentOutOfRangeException( nameof( speed ), "Channel speeds outside the positive finite domain are not traced." );
		var request = clip == null ? null : new Clip { Animation = clip, Name = clipName, AnimationId = animationId, Variant = variant, Loop = loop, Speed = speed };
		channels.TryGetValue( channel, out var current );
		if ( defer && current != null && !current.Ended && current.Player.Tick <= current.Clip.Animation.Duration
			&& animationId is not (NoAnimation + 1 or EndedAnimation) )
		{
			current.Deferred = request;
			var remaining = Fctiwz( RemainingMillisecondsPerTick * (current.Clip.Animation.Duration - current.Player.Tick) );
			return remaining + (clip == null ? MissingClipMilliseconds : StartLengthMilliseconds( clip ));
		}
		if ( current != null && defer )
			current.Deferred = null;
		if ( request == null )
			return MissingClipMilliseconds;
		Start( channel, request, NowMilliseconds );
		Update();
		return StartLengthMilliseconds( clip! );
	}

	/// <summary>
	/// 0xa67d8's returned length: <c>trunc(1000 × ticks / 30)</c> in single precision with ticks = trailer
	/// word 2 − word 1, this animator's rate in place of the constant 30. The speed does not enter it.
	/// </summary>
	public int StartLengthMilliseconds( ModelAnimation clip )
	{
		ArgumentNullException.ThrowIfNull( clip );
		var ticks = (float)(clip.Duration - clip.FirstTick);
		var milliseconds = 1000f * ticks;
		milliseconds /= TicksPerSecond;
		return Fctiwz( milliseconds );
	}

	/// <summary>FLUSHANIM / FLUSHANIM_CH (0xa6c90): clears only the channel's deferred clip; true when one was queued.</summary>
	public bool Flush( int channel )
	{
		if ( !channels.TryGetValue( channel, out var state ) || state.Deferred == null )
			return false;
		state.Deferred = null;
		return true;
	}

	/// <summary>Removes a channel and its clip (an OpenTPW reset; no script opcode does this).</summary>
	public void Stop( int channel )
	{
		if ( channels.Remove( channel ) )
			Update();
	}

	/// <summary>Removes every channel (object deletion).</summary>
	public void StopAll()
	{
		channels.Clear();
		Update();
	}

	/// <summary>One object update: advances the clock, replays, ends or follows on clips past their end, resamples.</summary>
	public void Advance( double seconds )
	{
		if ( !double.IsFinite( seconds ) || seconds < 0 )
			throw new ArgumentOutOfRangeException( nameof( seconds ) );
		clockMilliseconds += seconds * 1000.0;
		var now = NowMilliseconds;
		foreach ( var (index, channel) in channels.ToArray() )
		{
			if ( channel.Ended )
				continue;
			var frame = GetFrame( now - channel.StartMilliseconds, channel.Clip.Speed );
			float duration = channel.Clip.Animation.Duration;
			// 0xa7360: past the end only when strictly greater, so a frame equal to the duration samples the last key.
			if ( frame > duration )
			{
				if ( channel.Deferred is { } next )
				{
					channel.Deferred = null;
					Start( index, next, now );
					continue;
				}
				if ( channel.Clip.Loop )
				{
					channel.StartMilliseconds = now - GetCarryMilliseconds( frame - duration, duration, channel.Clip.Speed );
					channel.Serial = ++serial;
					ApplyNodeList( channel.Clip.Animation );
					frame = GetFrame( now - channel.StartMilliseconds, channel.Clip.Speed );
				}
				else
				{
					// [BIN:STP-PPC:0x100A7498 clip end] a non-looping clip with nothing deferred restarts as AnimID 14, which sets channel flags 0x14 (0x100A68AC) and branches past the bind (0x100A694C): frozen on the last frame, no copy-back, GETANIM -1
					// [APPROX:RIDES-003] A finished non-looping clip holds its last pose until replaced — evidence needed: capture after a TRIGANIM clip ends (the AnimID 14 path is traced; the object flags 0x8/0x10 that stop or hold instead come from untraced ride data)
					channel.Ended = true;
					frame = duration;
				}
			}
			channel.Player.SetTick( frame );
		}
		Update();
	}

	private void Start( int index, Clip clip, long now )
	{
		// The channel wraps the frame itself; the player only clamps.
		var player = new ModelAnimationPlayer( model, clip.Animation, TicksPerSecond ) { Loop = false };
		var tracks = new ModelAnimationTrack?[model.Nodes.Count];
		var vertices = new ModelVertexAnimation?[model.Nodes.Count];
		var uvs = new ModelUvAnimation?[model.Nodes.Count];
		foreach ( var track in clip.Animation.Tracks )
		{
			tracks[track.NodeIndex] = track;
			if ( (track.Flags & ModelAnimationTrack.VertexAnimationFlag) != 0 && GetVertexLimitation( track, clip.Animation.Duration ) is { } limitation )
				vertexLimitations.Add( $"{clip.Name} node {track.NodeIndex}: {limitation}" );
			else
				vertices[track.NodeIndex] = track.VertexAnimation;
			if ( track.UvAnimation != null )
			{
				var mesh = meshByNode[track.NodeIndex];
				if ( mesh < 0 )
					vertexLimitations.Add( $"{clip.Name} node {track.NodeIndex}: UV block on a node without a mesh" );
				else if ( track.UvAnimation.CornerCount != model.Meshes[mesh].TexCoords.Length )
					vertexLimitations.Add( $"{clip.Name} node {track.NodeIndex}: UV block animates {track.UvAnimation.CornerCount} corners, the mesh has {model.Meshes[mesh].TexCoords.Length}" );
				else
					// The sampler runs the UV pass when header flag 0x4 is set or node bit 0x4000 is clear (0xa56bc); nothing in OpenTPW sets 0x4000.
					uvs[track.NodeIndex] = track.UvAnimation;
			}
		}
		var state = new Channel
		{
			Clip = clip,
			Player = player,
			Tracks = tracks,
			Vertices = vertices,
			Uvs = uvs,
			Serial = ++serial,
			StartMilliseconds = now,
			// A start without flag 0x2 leaves the deferred slot (+36) as it was.
			Deferred = channels.TryGetValue( index, out var previous ) ? previous.Deferred : null
		};
		channels[index] = state;
		ApplyNodeList( clip.Animation );
	}

	/// <summary>
	/// 0xa67d8 → 0xa65b8 (start flag 0x8 clear, as for every script and advisor trigger): every listed node whose
	/// state bit 31 is clear gets node-state bit 0x10. Nodes are indexed directly (object-header bit 0x4 clear).
	/// </summary>
	private void ApplyNodeList( ModelAnimation clip )
	{
		foreach ( var node in clip.NodeList )
		{
			if ( node >= nodeStateBit.Length || (model.Nodes[node].Type & 0x80000000u) != 0 )
				continue;
			SetNodeStateBit( node, true );
		}
	}

	private void SetNodeStateBit( int node, bool value )
	{
		if ( nodeStateBit[node] == value )
			return;
		nodeStateBit[node] = value;
		if ( meshByNode[node] >= 0 )
			hiddenVersion++;
	}

	/// <summary>
	/// The original's integer scene clock (0xa6f70 stores whole milliseconds); the epsilon absorbs binary
	/// sums of decimal step sizes. How the scene clock itself rounds is a clock-lane dependency.
	/// </summary>
	private long NowMilliseconds => (long)Math.Floor( clockMilliseconds + 1e-6 );

	/// <summary>
	/// 0xa6484: <c>30 × (now − start) / 1000 × speed</c>, each step in single precision, with this animator's
	/// rate in place of the constant 30.
	/// </summary>
	private float GetFrame( long milliseconds, float speed )
	{
		var elapsed = (float)milliseconds;
		var ticks = TicksPerSecond * elapsed;
		ticks /= 1000f;
		return ticks * speed;
	}

	/// <summary>
	/// 0xa67d8 → 0xa6398: the carry past the end, capped at the duration, becomes <c>1000 × carry / 30 / speed</c>
	/// milliseconds truncated by the unsigned conversion 0x1c3fbc, with this animator's rate in place of the constant 30.
	/// </summary>
	private long GetCarryMilliseconds( float carry, float duration, float speed )
	{
		if ( carry > duration )
			carry = duration;
		var milliseconds = 1000f * carry;
		milliseconds /= TicksPerSecond;
		milliseconds /= speed;
		return (long)milliseconds;
	}

	/// <summary>Truncating float → int32 with PowerPC <c>fctiwz</c> saturation (NaN → int.MinValue).</summary>
	internal static int Fctiwz( float value ) => float.IsNaN( value ) ? int.MinValue
		: value >= 2147483648f ? int.MaxValue : value < -2147483648f ? int.MinValue : (int)value;

	private string? GetVertexLimitation( ModelAnimationTrack track, int duration )
	{
		if ( track.VertexPose != null )
			// [APPROX:RIDES-029] The 12-byte block (record flag 0x4000) shows the stored mesh: it is parsed, but no original reader of its floats was traced (0xa3e24 only sets node bits 0x800/0x100; the sampler skips vertex animation for 0x4000 at 0xa5048), and the floats differ from the stored positions in 19 of 30 corpus records — evidence needed: a reader of record +40 floats or node bit 0x800
			return "the 12-byte block (record flag 0x4000) has no traced reader; the stored mesh is shown";
		if ( track.VertexAnimation == null )
			return "no vertex block";
		var mesh = meshByNode[track.NodeIndex];
		if ( mesh < 0 )
			return "the node has no mesh";
		// Header flag 0x4 (relative animation) makes the sampler add vertex keys and translations to a per-update base copy
		// (0xa4a58/0xa4f68, 0xa7a30) and recompute face normals (0xa772c), but the ride loader clears it unless its argument
		// has 0x20000 (0x58a3c), so on the default load path these models animate in set mode with their stored normals.
		// [APPROX:RIDES-031] Relative-animation models (header flag 0x4) play their vertex tracks in set mode, as the default ride load path strips the flag; add mode behind the ride-loader argument 0x20000 is not modelled — evidence needed: the ride-data source of the 0x594c8 flag word that allows 0x20000
		return track.VertexAnimation.GetPoseLimitation( model.Meshes[mesh].Positions.Length, duration );
	}

	private void Update()
	{
		// [BIN:STP-PPC:0x100A7A5C object update channel order] 0xa7960 runs the channel updates in ascending channel order (stride 56, count +14), each sampling in set mode, so the highest channel that writes a component wins
		foreach ( var node in order )
		{
			var any = false;
			var (scale, rotation, translation) = restComponents[node];
			Channel? vertexWinner = null, uvWinner = null;
			foreach ( var channel in channels.Values )
			{
				var track = channel.Tracks[node];
				if ( track == null )
					continue;
				any = true;
				var tick = channel.Player.Tick;
				// Before a component's first key the sampler leaves it unchanged, so a lower channel's value stays.
				if ( track.SampleScale( tick ) is { } sampledScale )
					scale = sampledScale;
				if ( track.SampleRotation( tick ) is { } sampledRotation )
					rotation = sampledRotation;
				if ( track.SampleTranslation( tick ) is { } sampledTranslation )
					translation = sampledTranslation;
				if ( track.SampleNodeFlag( tick ) is { } flag )
					SetNodeStateBit( node, flag );
				if ( channel.Vertices[node] != null )
					vertexWinner = channel;
				if ( channel.Uvs[node] != null )
					uvWinner = channel;
			}
			var local = any
				? Matrix4x4.CreateScale( scale ) * Matrix4x4.CreateFromQuaternion( rotation ) * Matrix4x4.CreateTranslation( translation )
				: model.Nodes[node].Transform;
			var parent = model.Nodes[node].ParentIndex;
			world[node] = parent < 0 ? local : local * world[parent];
			if ( meshByNode[node] >= 0 )
				UpdateMesh( meshByNode[node], node, vertexWinner, uvWinner );
		}
	}

	/// <summary>
	/// The winning channel's vertex track sets every position of the mesh (set mode, static group plus animated
	/// groups at the clip tick) and its UV track every texture coordinate. A fresh key search per sample equals
	/// the original's kept cursor because its loop replay rebinds the clip (0xa7190 -> 0xa67d8 -> 0xa5894), which
	/// resets the cursor. When a mesh loses its track it shows its stored positions and texture coordinates,
	/// except on fixed items, where the last pose stays. The original's bind (0xa5894) copies the stored mesh back
	/// unless global option bit 0 is set or the object has flag 0x00100000 with 0x8 clear. The option word
	/// is only ever stored as 0 (0xa7eec from 0x54c08). Object flags come only from the ride loader's flag
	/// word (0x58a3c, via 0x594c8), and only the ride catalog loader 0x119328 asks for 0x00100000 without
	/// 0x8: for a CRideBalance whose word +56 is set, which its schema (0x39b64, laid out by 0x16f4c) makes
	/// <c>Info.DontApplyOffset</c>.
	/// </summary>
	private void UpdateMesh( int mesh, int node, Channel? vertexChannel, Channel? uvChannel )
	{
		var changed = false;
		var vertexSource = vertexChannel == null ? (0L, 0f) : (vertexChannel.Serial, vertexChannel.Player.Tick);
		if ( vertexSource != vertexSources[mesh] && !(vertexChannel == null && keepsPoseOnClipChange) )
		{
			if ( vertexChannel != null )
			{
				var positions = vertexPositions[mesh] ??= new NVector3[model.Meshes[mesh].Positions.Length];
				vertexChannel.Vertices[node]!.ApplyPose( vertexChannel.Player.Tick, positions );
			}
			vertexSources[mesh] = vertexSource;
			changed = true;
		}
		var uvSource = uvChannel == null ? (0L, 0f) : (uvChannel.Serial, uvChannel.Player.Tick);
		if ( uvSource != uvSources[mesh] && !(uvChannel == null && keepsPoseOnClipChange) )
		{
			if ( uvChannel != null )
			{
				var coordinates = texCoords[mesh] ??= new NVector2[model.Meshes[mesh].TexCoords.Length];
				// 0xa83e8 runs after the TRS and vertex passes of the same record (sampler tail 0xa56d8).
				uvChannel.Uvs[node]!.Apply( uvChannel.Player.Tick, coordinates );
			}
			uvSources[mesh] = uvSource;
			changed = true;
		}
		if ( changed )
			vertexVersions[mesh]++;
	}

	/// <summary>
	/// Animation state that feeds the scripts (GETANIM, TRIGWAITANIM): per channel its AnimID, variant, loop bit,
	/// speed, start time, ended flag and deferred clip, plus the clock. Part of the canonical hash.
	/// </summary>
	internal void AddCanonicalState( StateHasher hash )
	{
		hash.Add( clockMilliseconds );
		hash.Add( channels.Count );
		foreach ( var (index, channel) in channels )
		{
			hash.Add( index );
			hash.Add( channel.Clip.Name );
			hash.Add( channel.Clip.AnimationId );
			hash.Add( channel.Clip.Variant );
			hash.Add( channel.Clip.Loop );
			hash.Add( channel.Clip.Speed );
			hash.Add( channel.StartMilliseconds );
			hash.Add( channel.Ended );
			hash.Add( channel.Player.Tick );
			hash.Add( channel.Deferred != null );
			if ( channel.Deferred is { } deferred )
			{
				hash.Add( deferred.Name );
				hash.Add( deferred.AnimationId );
				hash.Add( deferred.Variant );
				hash.Add( deferred.Loop );
				hash.Add( deferred.Speed );
			}
		}
	}
}
