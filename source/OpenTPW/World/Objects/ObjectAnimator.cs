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
// [APPROX:RIDES-032] Every instance takes the placement callback route (Mac 0xa7960 with r4 = 1) on every update. PC: the render callback 0x463030 at model record +0x140 (set at 0x4638bd) poses via 0x44e380(inst, 1) and sets instance flag 0x200000; the list pass 0x44e410 updates instances without that flag via 0x473c70(inst, 0, 8) without posing and clears the flag on the others. Object flag 0x00400000 is set at 0x4737e8 when a non-looping clip ends with nothing deferred, flags & 0x10 set and & 0x8 clear (it also sets 0x40000000 and holds the last frame). The list-pass route is not modelled — evidence needed: the renderer's call site of the 0x140 callback
// [BIN:TPW-EXE:0x473c70 idle restart] The idle restart (Mac 0xa798c–0xa79c8) never fires for placed rides: placement through 0x4db090 → 0x4dd500 (flags 0x361/0x32f) and script-spawned instances (0x55a720, 0x549faf, 0x54a237, 0x54a715; flags 0x101) always set bit 4; the creator 0x463060 sets bit 4 from argument bit 1 and 0x8000 from argument 0x80. (RIDES-034 retired.)
// [APPROX:RIDES-035] MD2 texture-frame tracks (trailer word 12) now apply: the clip update 0xa56f8 runs them when trailer bit 0x2 is set and global option bit 0x8 is clear (its only store writes 0), and <see cref="GetTextureFrame"/> exposes the winning frame per slot; the PC writer is 0x4721f0 → 0x470c90 (slot record at model+0x50+slot·8: frame at +2, dirty bit 0x04 in byte +1 when the frame changes; trailer flags & 2 and option 0x7b4980 bit 0x8 clear), but its draw-path reader is not traced, so the binding to rendered textures follows this port's material-slot mapping — evidence needed: the draw-path reader of a texture slot's current frame (a write watchpoint on a slot's +2 at runtime, or the D3D texture-bind code)
public sealed class ObjectAnimator
{
	/// <summary>Default rate shared with the Totem; 30 is proved for the Feral Mac channel formula, while Windows and runtime clock selection remain unverified.</summary>
	// [BIN:STP-PPC:0x100A70B4 channel frame arithmetic] the Feral Mac formula multiplies elapsed milliseconds by 30 and divides by 1000 before channel speed.
	// [BIN:TPW-EXE:0x473490 channel frame arithmetic] The PC frame is (u32)(now − start) · speed · 0.03f on x87 = 30 per second (0x473490, 0x4735d0), so the Mac rate agrees (float rounding may differ slightly). Channel flag 0x40 selects the unscaled clock 0x7b4974, else the scaled clock 0x7b496c, both refreshed by 0x473440 from the timer 0x785970; ride channels use the scaled clock, the advisor the unscaled one. (RIDES-001 retired.)
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
		/// <summary>Per node: the clip's 12-byte vertex block (one stored pose per tick) when it fits the node's mesh, else null.</summary>
		public required ModelVertexPose?[] Poses { get; set; }
		/// <summary>Per node: the clip's texture-coordinate track when it fits the node's mesh, else null.</summary>
		public required ModelUvAnimation?[] Uvs { get; set; }
		/// <summary>The clip's texture-frame tracks that address a slot of this model (empty when the trailer does not enable them).</summary>
		public required ModelTextureFrameTrack[] FrameTracks { get; set; }
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
	private readonly int[] frameBySlot;
	private readonly int[] sampledFrameBySlot;
	private readonly bool[]?[] availableFramesBySlot;
	private readonly bool[] slotAnimated;
	private readonly bool[] slotAddressed;
	private int textureFrameVersion;
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
		// Texture frames are per model slot (frame 0 is the stored frame name every material shows).
		frameBySlot = new int[model.Textures.Count];
		sampledFrameBySlot = new int[model.Textures.Count];
		availableFramesBySlot = new bool[]?[model.Textures.Count];
		slotAnimated = new bool[model.Textures.Count];
		slotAddressed = new bool[model.Textures.Count];
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
	// [APPROX:RIDES-042] Node-state bit 0x10 set (clip node lists at start, toggle entries ≤ 0) hides the node's mesh; the draw consumer of the bit was not found (the object save persists it, the advisor lists hats and accessories, Fireworks list their Destroyed mesh) — evidence needed: the reader of node bit 0x10 in the draw path, or captures
	public bool IsNodeHidden( int node ) => meshByNode[node] >= 0 && nodeStateBit[node];

	/// <summary>Changes whenever <see cref="IsNodeHidden"/> of some node changes.</summary>
	public int HiddenVersion => hiddenVersion;

	/// <summary>
	/// True while some bound clip addresses a texture slot of this model with a texture-frame track
	/// (trailer word 12 behind bit 0x2, clip update 0xa56f8), so <see cref="GetTextureFrame"/> can change.
	/// </summary>
	public bool HasTextureFrames => channels.Values.Any( channel => channel.FrameTracks.Length > 0 );

	/// <summary>
	/// The frame a texture slot of the model currently shows: 0 (the stored frame name) until a bound clip's
	/// texture-frame track selects another, the highest channel writing the slot winning as for every node
	/// component (a null sample before a track's first key leaves the slot unchanged). A frame index outside
	/// the slot's frame names is never returned; a slot every clip stops addressing shows frame 0 again.
	/// </summary>
	// [APPROX:MD2-900] The slot's current frame mixes like the node components (ascending channels, the highest non-null sample wins; null before the first key keeps the previous frame, initially 0) and a slot no bound clip addresses any more returns to frame 0 like a mesh that loses its track returns to its stored positions — evidence needed: the original draw-path reader of a slot's current frame (the clip update 0xa56f8 only writes it)
	// [APPROX:MD2-901] Frame indices outside the slot's frame-name list are ignored (no corpus case among 458 in-range tracks; the one out-of-range track, Pmegacostm slot 12, addresses a slot its base model does not have, which the bind also ignores) — evidence needed: the original's behaviour on an out-of-range frame
	public int GetTextureFrame( int slot )
	{
		if ( (uint)slot >= (uint)frameBySlot.Length )
			throw new ArgumentOutOfRangeException( nameof( slot ) );
		return frameBySlot[slot];
	}

	/// <summary>Renderer-provided decoded-art availability; missing art holds the effective selected frame.</summary>
	internal void SetTextureFrameAvailability( int slot, bool[] available )
	{
		if ( (uint)slot >= (uint)frameBySlot.Length || available.Length != model.Textures[slot].FrameNames.Count )
			throw new ArgumentException( "Texture-frame availability must match a model slot's frame names." );
		availableFramesBySlot[slot] = (bool[])available.Clone();
		if ( frameBySlot[slot] != 0 && !available[frameBySlot[slot]] )
		{
			frameBySlot[slot] = 0;
			textureFrameVersion++;
		}
		UpdateTextureFrames();
	}

	/// <summary>Changes whenever <see cref="GetTextureFrame"/> of some slot changes.</summary>
	public int TextureFrameVersion => textureFrameVersion;

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
					// [APPROX:RIDES-003] A finished non-looping clip holds its last pose until replaced, and the next trigger restores stored components against the ended clip; the original's start passes the channel's AnimID 14, sub 0 entry as the old clip to the copy-back (0xa692c–0xa6948, 0xa69a4: only AnimID 12 skips it), and what that slot holds is not traced — evidence needed: capture after a TRIGANIM clip ends, the object's AnimID-14 table slot, and the ride-data sources of object flags 0x8/0x10 that stop or hold instead
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
		var state = BindChannel( clip );
		state.Serial = ++serial;
		state.StartMilliseconds = now;
		// A start without flag 0x2 leaves the deferred slot (+36) as it was.
		state.Deferred = channels.TryGetValue( index, out var previous ) ? previous.Deferred : null;
		channels[index] = state;
		ApplyNodeList( clip.Animation );
	}

	/// <summary>Binds a clip's tracks to this model — the body of a channel start, shared by the snapshot restore.</summary>
	private Channel BindChannel( Clip clip )
	{
		// The channel wraps the frame itself; the player only clamps.
		var player = new ModelAnimationPlayer( model, clip.Animation, TicksPerSecond ) { Loop = false };
		var tracks = new ModelAnimationTrack?[model.Nodes.Count];
		var vertices = new ModelVertexAnimation?[model.Nodes.Count];
		var poses = new ModelVertexPose?[model.Nodes.Count];
		var uvs = new ModelUvAnimation?[model.Nodes.Count];
		// The clip update 0xa56f8 samples the texture-frame tracks only when trailer bit 0x2 is set (global
		// option bit 0x8 is only ever stored clear). A track naming a slot the model does not have is dropped;
		// the one corpus case (Pmegacostm slot 12) pairs a header that repeats a smaller model's counts.
		var frameTracks = clip.Animation.TextureFramesEnabled
			? clip.Animation.TextureFrameTracks.Where( track => track.Slot < model.Textures.Count ).ToArray()
			: Array.Empty<ModelTextureFrameTrack>();
		foreach ( var track in clip.Animation.Tracks )
		{
			tracks[track.NodeIndex] = track;
			if ( (track.Flags & (ModelAnimationTrack.VertexAnimationFlag | ModelAnimationTrack.VertexLayoutFlag)) != 0 && GetVertexLimitation( track, clip.Animation.Duration ) is { } limitation )
				vertexLimitations.Add( $"{clip.Name} node {track.NodeIndex}: {limitation}" );
			else
			{
				vertices[track.NodeIndex] = track.VertexAnimation;
				// The sampler reads the block only with both 0x1000 and 0x4000 (0x4718E5, 0x4718EE); the fit is validated for every 0x4000 record above.
				poses[track.NodeIndex] = (track.Flags & ModelAnimationTrack.VertexAnimationFlag) != 0 ? track.VertexPose : null;
			}
			if ( track.UvAnimation != null )
			{
				var mesh = meshByNode[track.NodeIndex];
				if ( mesh < 0 )
					vertexLimitations.Add( $"{clip.Name} node {track.NodeIndex}: UV block on a node without a mesh" );
				else if ( track.UvAnimation.CornerCount != model.Meshes[mesh].TexCoords.Length )
					vertexLimitations.Add( $"{clip.Name} node {track.NodeIndex}: UV block animates {track.UvAnimation.CornerCount} corners, the mesh has {model.Meshes[mesh].TexCoords.Length}" );
				else
					// The sampler runs the UV pass when global option +16396 bit 0x10 and node bit 0x4000 are both clear, or header flag 0x4 is set (0xa5698–0xa56b8).
					// [BIN:TPW-EXE:0x0047219A UV pass gate] the UV pass 0x4745c0 of the sampler 0x471860 runs when (option 0x7b4980 & 0x10 clear and node-state bit 0x4000 clear) or the model header has flag 4, and the record has flag 0x10000; the option word has one write (0x4740d0, called once with 0 from 0x457a90) and node bit 0x4000 has one writer (0x46a81d, the generated ground-conforming base node of 0x46a5a0, which has no clip tracks), so authored nodes always run the UV pass
					uvs[track.NodeIndex] = track.UvAnimation;
			}
		}
		return new Channel { Clip = clip, Player = player, Tracks = tracks, Vertices = vertices, Poses = poses, Uvs = uvs, FrameTracks = frameTracks };
	}

	// ---- Park-resume snapshot ----

	/// <summary>Explicit state of every channel, the caches and the clocks (see <see cref="ObjectAnimatorSnapshot"/>).</summary>
	public ObjectAnimatorSnapshot CaptureSnapshot()
	{
		var capturedChannels = channels.OrderBy( pair => pair.Key ).Select( pair => new AnimatorChannelSnapshot
		{
			ChannelIndex = pair.Key,
			ClipName = pair.Value.Clip.Name,
			AnimationId = pair.Value.Clip.AnimationId,
			Variant = pair.Value.Clip.Variant,
			Loop = pair.Value.Clip.Loop,
			Speed = pair.Value.Clip.Speed,
			Serial = pair.Value.Serial,
			StartMilliseconds = pair.Value.StartMilliseconds,
			Ended = pair.Value.Ended,
			Tick = pair.Value.Player.Tick,
			ClipDurationTicks = pair.Value.Clip.Animation.Duration,
			Deferred = pair.Value.Deferred is { } deferred
				? new AnimatorDeferredClipSnapshot
				{
					Name = deferred.Name,
					AnimationId = deferred.AnimationId,
					Variant = deferred.Variant,
					Loop = deferred.Loop,
					Speed = deferred.Speed,
					ClipDurationTicks = deferred.Animation.Duration
				}
				: null
		} ).ToArray();
		var capturedMeshes = Enumerable.Range( 0, model.Meshes.Count ).Select( mesh => new AnimatorMeshCacheSnapshot
		{
			Mesh = mesh,
			VertexVersion = vertexVersions[mesh],
			VertexSerial = vertexSources[mesh].Serial,
			VertexTick = vertexSources[mesh].Tick,
			Positions = vertexPositions[mesh]?.SelectMany( position => new[] { position.X, position.Y, position.Z } ).ToArray(),
			UvSerial = uvSources[mesh].Serial,
			UvTick = uvSources[mesh].Tick,
			TexCoords = texCoords[mesh]?.SelectMany( coordinate => new[] { coordinate.X, coordinate.Y } ).ToArray()
		} ).ToArray();
		return new ObjectAnimatorSnapshot
		{
			ClockMilliseconds = clockMilliseconds,
			Serial = serial,
			HiddenVersion = hiddenVersion,
			TicksPerSecond = TicksPerSecond,
			KeepsPoseOnClipChange = keepsPoseOnClipChange,
			Channels = capturedChannels,
			NodeStateBits = (bool[])nodeStateBit.Clone(),
			TextureFrames = new AnimatorTextureFrameSnapshot
			{
				Frames = (int[])frameBySlot.Clone(),
				AnimatedSlots = (bool[])slotAnimated.Clone(),
				Version = textureFrameVersion
			},
			Meshes = capturedMeshes,
			VertexLimitations = vertexLimitations.ToArray()
		};
	}

	/// <summary>
	/// Restores a snapshot into this animator. <paramref name="resolveClip"/> resolves each clip through the
	/// object's own archive (the runtime passes <see cref="OriginalObjectRuntime"/>'s member lookup); a clip that
	/// resolves to null, another name or another length fails the restore. Everything is resolved and verified
	/// before this animator changes; a throw leaves it partially restored — discard the candidate then. The node
	/// transforms are recomputed by the trailing <see cref="Update"/> from the restored channels.
	/// </summary>
	internal void RestoreSnapshot( ObjectAnimatorSnapshot snapshot, Func<AnimatorClipIdentity, ResolvedAnimatorClip?> resolveClip )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		ArgumentNullException.ThrowIfNull( resolveClip );
		snapshot.ValidateSnapshot();
		if ( snapshot.TicksPerSecond != TicksPerSecond )
			throw new InvalidDataException( $"animator runs at {TicksPerSecond} ticks/s, the snapshot stores {snapshot.TicksPerSecond}." );
		if ( snapshot.KeepsPoseOnClipChange != keepsPoseOnClipChange )
			throw new InvalidDataException( "animator fixed-item pose retention does not match the snapshot." );
		if ( snapshot.NodeStateBits.Length != nodeStateBit.Length )
			throw new InvalidDataException( $"animator has {nodeStateBit.Length} nodes, the snapshot stores {snapshot.NodeStateBits.Length} node-state bits." );
		if ( snapshot.Meshes.Count != vertexVersions.Length || snapshot.Meshes.Where( ( cached, index ) => cached.Mesh != index ).Any() )
			throw new InvalidDataException( $"animator has {vertexVersions.Length} meshes, the snapshot does not describe exactly those, in order." );
		if ( snapshot.TextureFrames is { } frameHistory )
		{
			if ( frameHistory.Frames.Length != model.Textures.Count )
				throw new InvalidDataException( $"animator has {model.Textures.Count} texture slots, the snapshot stores {frameHistory.Frames.Length}." );
			for ( var slot = 0; slot < frameHistory.Frames.Length; slot++ )
				if ( frameHistory.Frames[slot] >= Math.Max( 1, model.Textures[slot].FrameNames.Count ) )
					throw new InvalidDataException( $"texture slot {slot} names frame {frameHistory.Frames[slot]} outside its frame names." );
		}
		foreach ( var cached in snapshot.Meshes )
		{
			if ( cached.Positions != null && cached.Positions.Length != model.Meshes[cached.Mesh].Positions.Length * 3 )
				throw new InvalidDataException( $"mesh {cached.Mesh} position cache has {cached.Positions.Length} floats, the mesh has {model.Meshes[cached.Mesh].Positions.Length} vertices." );
			if ( cached.TexCoords != null && cached.TexCoords.Length != model.Meshes[cached.Mesh].TexCoords.Length * 2 )
				throw new InvalidDataException( $"mesh {cached.Mesh} texture-coordinate cache has {cached.TexCoords.Length} floats, the mesh has {model.Meshes[cached.Mesh].TexCoords.Length} coordinates." );
		}

		// Resolve and bind every channel first; only then replace the live state.
		var restored = new List<(int Index, Channel State)>();
		foreach ( var captured in snapshot.Channels )
		{
			var clip = ResolveSnapshotClip( resolveClip, captured.ClipName, captured.AnimationId, captured.Variant, captured.ClipDurationTicks, captured.Loop, captured.Speed, $"channel {captured.ChannelIndex}" );
			var state = BindChannel( clip );
			state.Serial = captured.Serial;
			state.StartMilliseconds = captured.StartMilliseconds;
			state.Ended = captured.Ended;
			state.Player.SetTick( captured.Tick );
			if ( captured.Deferred is { } deferred )
				state.Deferred = ResolveSnapshotClip( resolveClip, deferred.Name, deferred.AnimationId, deferred.Variant, deferred.ClipDurationTicks, deferred.Loop, deferred.Speed, $"channel {captured.ChannelIndex} deferred" );
			restored.Add( (captured.ChannelIndex, state) );
		}
		if ( restored.Count > 0 && snapshot.Serial < restored.Max( pair => pair.State.Serial ) )
			throw new InvalidDataException( "animator serial counter is below a channel's bind serial." );

		channels.Clear();
		foreach ( var (index, state) in restored )
			channels[index] = state;
		Array.Copy( snapshot.NodeStateBits, nodeStateBit, nodeStateBit.Length );
		Array.Clear( frameBySlot );
		Array.Clear( slotAnimated );
		textureFrameVersion = 0;
		if ( snapshot.TextureFrames is { } restoredFrames )
		{
			Array.Copy( restoredFrames.Frames, frameBySlot, frameBySlot.Length );
			Array.Copy( restoredFrames.AnimatedSlots, slotAnimated, slotAnimated.Length );
			textureFrameVersion = restoredFrames.Version;
			// A headless save has raw selection history, without decoded-art availability. Restore
			// must also respect availability already registered by a newly built renderer, just as
			// registering availability after restore does: unavailable history starts at the base art.
			for ( var slot = 0; slot < frameBySlot.Length; slot++ )
				if ( frameBySlot[slot] != 0 && availableFramesBySlot[slot] is { } available && !available[frameBySlot[slot]] )
				{
					frameBySlot[slot] = 0;
					textureFrameVersion++;
				}
		}
		foreach ( var cached in snapshot.Meshes )
		{
			vertexVersions[cached.Mesh] = cached.VertexVersion;
			vertexSources[cached.Mesh] = (cached.VertexSerial, cached.VertexTick);
			uvSources[cached.Mesh] = (cached.UvSerial, cached.UvTick);
			vertexPositions[cached.Mesh] = cached.Positions == null ? null : RestorePositions( cached.Positions );
			texCoords[cached.Mesh] = cached.TexCoords == null ? null : RestoreTexCoords( cached.TexCoords );
		}
		serial = snapshot.Serial;
		hiddenVersion = snapshot.HiddenVersion;
		clockMilliseconds = snapshot.ClockMilliseconds;
		vertexLimitations.Clear();
		vertexLimitations.UnionWith( snapshot.VertexLimitations );
		Update();
	}

	private NVector3[] RestorePositions( float[] flat )
	{
		var positions = new NVector3[flat.Length / 3];
		for ( var index = 0; index < positions.Length; index++ )
			positions[index] = new NVector3( flat[index * 3], flat[index * 3 + 1], flat[index * 3 + 2] );
		return positions;
	}

	private NVector2[] RestoreTexCoords( float[] flat )
	{
		var coordinates = new NVector2[flat.Length / 2];
		for ( var index = 0; index < coordinates.Length; index++ )
			coordinates[index] = new NVector2( flat[index * 2], flat[index * 2 + 1] );
		return coordinates;
	}

	private Clip ResolveSnapshotClip( Func<AnimatorClipIdentity, ResolvedAnimatorClip?> resolveClip, string name, int animationId, int variant, int durationTicks, bool loop, float speed, string what )
	{
		var resolved = resolveClip( new AnimatorClipIdentity( animationId, variant, name ) );
		if ( resolved == null )
			throw new InvalidDataException( $"{what}: animation {animationId}/{variant} ('{name}') could not be resolved from the object's archive." );
		if ( !string.Equals( resolved.Name, name, StringComparison.Ordinal ) )
			throw new InvalidDataException( $"{what}: the archive resolves animation {animationId}/{variant} to '{resolved.Name}', not the snapshot's '{name}'." );
		if ( resolved.Clip.Duration != durationTicks )
			throw new InvalidDataException( $"{what}: clip '{name}' has length {resolved.Clip.Duration} ticks, the snapshot stores {durationTicks}." );
		return new Clip { Animation = resolved.Clip, Name = name, AnimationId = animationId, Variant = variant, Loop = loop, Speed = speed };
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
		{
			// [BIN:TPW-EXE:0x004718F7 12-byte vertex block] the PC sampler replaces the node's vertices by the block's stored pose of frame trunc(time), without blending (documented on ModelVertexPose)
			var poseMesh = meshByNode[track.NodeIndex];
			return poseMesh < 0 ? "the node has no mesh" : track.VertexPose.GetLimitation( model.Meshes[poseMesh].Positions.Length );
		}
		if ( track.VertexAnimation == null )
			return "no vertex block";
		var mesh = meshByNode[track.NodeIndex];
		if ( mesh < 0 )
			return "the node has no mesh";
		// Header flag 0x4 (relative animation) makes the sampler add vertex keys and translations to a per-update base copy
		// (0xa4a58/0xa4f68, 0xa7a30) and recompute face normals (0xa772c), but the ride loader clears it unless its argument
		// has 0x20000 (0x58a3c), so on the default load path these models animate in set mode with their stored normals.
		// [BIN:TPW-EXE:0x00461F10 relative animation] the loader 0x461f10 keeps header flag 4 only when the inner argument bit 0x20000 is set (else it logs "Ride %s has relative animation incorrectly set" and strips it); 0x4629d0 maps outer 0x1000000 to inner 0x20000; only the TrackMaster loader 0x42fad0 passes it (0x1013405), while the object loader 0x413c10 passes 0x50c00/0x450c00/0xb24a9/0x4b24a9/0xc0, so objects play in set mode; Add mode for the TrackMaster coaster models is registered under COAST-026
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
				if ( channel.Vertices[node] != null || channel.Poses[node] != null )
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
		UpdateTextureFrames();
	}

	/// <summary>
	/// Resamples every slot a bound clip addresses (the sampler tail of the clip update 0xa56f8): channels mix in
	/// ascending order, a non-null <see cref="ModelTextureFrameTrack.FrameAt"/> of a higher channel overwriting a
	/// lower one, a null sample (before the first key) leaving the slot unchanged. A slot that no bound clip
	/// addresses any more shows frame 0 again, like a mesh that loses its track shows its stored positions.
	/// </summary>
	private void UpdateTextureFrames()
	{
		if ( model.Textures.Count == 0 )
			return;
		Array.Clear( slotAddressed, 0, slotAddressed.Length );
		Array.Copy( frameBySlot, sampledFrameBySlot, frameBySlot.Length );
		foreach ( var channel in channels.Values )
		{
			foreach ( var track in channel.FrameTracks )
			{
				slotAddressed[track.Slot] = true;
				slotAnimated[track.Slot] = true;
				// A frame outside the slot's names is ignored (kept at the previous frame).
				if ( track.FrameAt( channel.Player.Tick ) is { } frame && frame < model.Textures[track.Slot].FrameNames.Count
					&& (availableFramesBySlot[track.Slot] == null || availableFramesBySlot[track.Slot]![frame]) )
					sampledFrameBySlot[track.Slot] = frame;
			}
		}
		for ( var slot = 0; slot < slotAddressed.Length; slot++ )
		{
			if ( slotAnimated[slot] && !slotAddressed[slot] )
			{
				slotAnimated[slot] = false;
				sampledFrameBySlot[slot] = 0;
			}
			if ( frameBySlot[slot] != sampledFrameBySlot[slot] )
			{
				frameBySlot[slot] = sampledFrameBySlot[slot];
				textureFrameVersion++;
			}
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
				if ( vertexChannel.Poses[node] is { } pose )
					pose.ApplyPose( vertexChannel.Player.Tick, positions );
				else
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
