namespace OpenTPW;

/// <summary>Identity of one clip a channel plays, as the park-resume snapshot stores it: the RSE animation
/// number and variant that triggered it plus the member name and clip length, so the restore can resolve the same
/// clip through the object's archive and refuse a changed one.</summary>
public sealed record AnimatorClipIdentity( int AnimationId, int Variant, string Name );

/// <summary>A clip resolved from the object's own archive for a snapshot restore.</summary>
public sealed record ResolvedAnimatorClip( string Name, ModelAnimation Clip );

/// <summary>
/// A deferred clip of a channel (trigger flag 0x2): plain data — it becomes the channel's clip only once the
/// running one ends, so it has no player state.
/// </summary>
public sealed record AnimatorDeferredClipSnapshot
{
	public required string Name { get; init; }
	public required int AnimationId { get; init; }
	public required int Variant { get; init; }
	public required bool Loop { get; init; }
	public required float Speed { get; init; }
	/// <summary>Clip length in ticks, for the resolved-asset identity check.</summary>
	public required int ClipDurationTicks { get; init; }
}

/// <summary>One channel of an <see cref="ObjectAnimator"/>: its clip, timing, ended flag and deferred clip.</summary>
public sealed record AnimatorChannelSnapshot
{
	/// <summary>Channel index; 0 is the plain animation opcodes' channel.</summary>
	public required int ChannelIndex { get; init; }
	public required string ClipName { get; init; }
	public required int AnimationId { get; init; }
	public required int Variant { get; init; }
	public required bool Loop { get; init; }
	public required float Speed { get; init; }
	/// <summary>Bind serial of this start; the mesh caches compare against it.</summary>
	public required long Serial { get; init; }
	/// <summary>
	/// StartAnimTime: the animator clock (whole ms) this clip started at. A clip an original save restored (<see cref="ObjectAnimator.RestoreSaved"/>) may have started before this
	/// animator's clock began, up to the save's 32-bit elapsed time back: its start is then negative.
	/// </summary>
	public required long StartMilliseconds { get; init; }
	/// <summary>AnimID-14 semantics: frozen at the last frame, still posing, GETANIM −1.</summary>
	public required bool Ended { get; init; }
	/// <summary>Optional for older saves: the channel ended through the hold route (AnimID 12, ended), not as AnimID 14.</summary>
	public bool Held { get; init; }
	/// <summary>Current frame of the clip.</summary>
	public required float Tick { get; init; }
	/// <summary>Clip length in ticks, for the resolved-asset identity check.</summary>
	public required int ClipDurationTicks { get; init; }
	public AnimatorDeferredClipSnapshot? Deferred { get; init; }
}

/// <summary>
/// The per-mesh vertex/texture caches of an <see cref="ObjectAnimator"/>: the (serial, tick) source each mesh's
/// pose came from and the pose itself. Null arrays mean the mesh shows its stored positions/coordinates; on fixed
/// items a stale non-null pose is real state (the retained pose), not derivable from the channels.
/// </summary>
public sealed record AnimatorMeshCacheSnapshot
{
	public required int Mesh { get; init; }
	/// <summary>Version counter of this mesh's vertex/UV data (render invalidation).</summary>
	public required int VertexVersion { get; init; }
	public required long VertexSerial { get; init; }
	public required float VertexTick { get; init; }
	/// <summary>Flat XYZ per vertex, or null when the mesh shows its stored positions.</summary>
	public required float[]? Positions { get; init; }
	public required long UvSerial { get; init; }
	public required float UvTick { get; init; }
	/// <summary>Flat UV pairs, or null when the mesh shows its stored coordinates.</summary>
	public required float[]? TexCoords { get; init; }
}

/// <summary>Plain texture-slot history: null/invalid samples retain the last selected frame.</summary>
public sealed record AnimatorTextureFrameSnapshot
{
	public required int[] Frames { get; init; }
	public required bool[] AnimatedSlots { get; init; }
	public required int Version { get; init; }
}

/// <summary>
/// Explicit state of an <see cref="ObjectAnimator"/> (park-resume slice): the animator clock and serial counter,
/// every channel (clip identity, timing, ended flag, deferred clip), the node-state bits, the per-mesh caches with
/// retained poses and the vertex limitations seen so far. The node transforms are recomputed from the restored
/// channels; nothing here needs live objects.
/// </summary>
public sealed record ObjectAnimatorSnapshot
{
	public const int CurrentSchemaVersion = 1;
	/// <summary>Bound for the channel count of one animator (no corpus object exceeds a handful).</summary>
	public const int MaximumChannels = 1024;

	public int SchemaVersion { get; init; } = CurrentSchemaVersion;
	public required double ClockMilliseconds { get; init; }
	public required long Serial { get; init; }
	public required int HiddenVersion { get; init; }
	/// <summary>Clip rate of every channel — identity of the animator configuration; the restore must match.</summary>
	public required float TicksPerSecond { get; init; }
	/// <summary>Fixed-item pose retention — identity of the animator configuration; the restore must match.</summary>
	public required bool KeepsPoseOnClipChange { get; init; }
	/// <summary>Channels in ascending index order.</summary>
	public required IReadOnlyList<AnimatorChannelSnapshot> Channels { get; init; }
	/// <summary>Node-state bit 0x10 per node, in node order.</summary>
	public required bool[] NodeStateBits { get; init; }
	/// <summary>Per-mesh caches, in mesh order.</summary>
	public required IReadOnlyList<AnimatorMeshCacheSnapshot> Meshes { get; init; }
	public required IReadOnlyList<string> VertexLimitations { get; init; }
	/// <summary>Optional for older saves; absent history starts at frame 0, then samples restored channels.</summary>
	public AnimatorTextureFrameSnapshot? TextureFrames { get; init; }
	/// <summary>
	/// The nodes a track add-on's walk pinned (bit 31 of their node state, <see cref="ObjectAnimator.SetNodeHidden"/>), ascending; absent when none is, so the snapshots of
	/// every other object are written as before.
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public int[]? PinnedNodes { get; init; }

	/// <summary>Deep copy.</summary>
	public ObjectAnimatorSnapshot DeepCopy() => this with
	{
		Channels = Channels.Select( channel => channel with { Deferred = channel.Deferred } ).ToArray(),
		NodeStateBits = (bool[])NodeStateBits.Clone(),
		PinnedNodes = (int[]?)PinnedNodes?.Clone(),
		TextureFrames = TextureFrames == null ? null : TextureFrames with
		{
			Frames = (int[])TextureFrames.Frames.Clone(),
			AnimatedSlots = (bool[])TextureFrames.AnimatedSlots.Clone()
		},
		Meshes = Meshes.Select( mesh => mesh with { Positions = mesh.Positions == null ? null : (float[])mesh.Positions.Clone(), TexCoords = mesh.TexCoords == null ? null : (float[])mesh.TexCoords.Clone() } ).ToArray(),
		VertexLimitations = VertexLimitations.ToArray()
	};

	/// <summary>
	/// Pure structural validation: schema, bounds, finite numbers, channel-index uniqueness, cache-array shapes.
	/// Throws <see cref="InvalidDataException"/>; checks that need the model (array lengths per mesh, node counts)
	/// happen in the restore.
	/// </summary>
	public void ValidateSnapshot()
	{
		if ( SchemaVersion != CurrentSchemaVersion )
			throw new InvalidDataException( $"animator snapshot schema version {SchemaVersion} is not {CurrentSchemaVersion}." );
		if ( !double.IsFinite( ClockMilliseconds ) || ClockMilliseconds < 0 )
			throw new InvalidDataException( $"animator clock {ClockMilliseconds} is not finite and non-negative." );
		if ( Serial < 0 || HiddenVersion < 0 )
			throw new InvalidDataException( "animator serial and hidden version are negative." );
		if ( !float.IsFinite( TicksPerSecond ) || TicksPerSecond <= 0 )
			throw new InvalidDataException( $"animator clip rate {TicksPerSecond} is not positive finite." );
		if ( Channels.Count > MaximumChannels )
			throw new InvalidDataException( $"animator snapshot has {Channels.Count} channels, above the bound {MaximumChannels}." );
		var indices = new HashSet<int>();
		foreach ( var channel in Channels )
		{
			if ( !indices.Add( channel.ChannelIndex ) )
				throw new InvalidDataException( $"channel {channel.ChannelIndex} appears twice." );
			if ( channel.ChannelIndex < 0 )
				throw new InvalidDataException( $"channel index {channel.ChannelIndex} is negative." );
			if ( channel.ClipName == null )
				throw new InvalidDataException( $"channel {channel.ChannelIndex} has a null clip name." );
			if ( channel.Held && !channel.Ended )
				throw new InvalidDataException( $"channel {channel.ChannelIndex} is held without having ended." );
			if ( !float.IsFinite( channel.Speed ) || channel.Speed <= 0 )
				throw new InvalidDataException( $"channel {channel.ChannelIndex} speed {channel.Speed} is not positive finite." );
			if ( channel.Serial < 1 )
				throw new InvalidDataException( $"channel {channel.ChannelIndex} serial {channel.Serial} is not positive." );
			// a clip an original save restored started up to the save's u32 elapsed time before this animator's clock began
			if ( channel.StartMilliseconds < -(long)uint.MaxValue )
				throw new InvalidDataException( $"channel {channel.ChannelIndex} start {channel.StartMilliseconds} lies further back than a saved 32-bit elapsed time." );
			if ( !float.IsFinite( channel.Tick ) || channel.Tick < 0 )
				throw new InvalidDataException( $"channel {channel.ChannelIndex} tick {channel.Tick} is not finite and non-negative." );
			if ( channel.ClipDurationTicks < 0 )
				throw new InvalidDataException( $"channel {channel.ChannelIndex} clip length {channel.ClipDurationTicks} is negative." );
			if ( channel.Deferred is { } deferred )
			{
				if ( deferred.Name == null )
					throw new InvalidDataException( $"channel {channel.ChannelIndex} has a deferred clip without a name." );
				if ( !float.IsFinite( deferred.Speed ) || deferred.Speed <= 0 )
					throw new InvalidDataException( $"channel {channel.ChannelIndex} deferred speed {deferred.Speed} is not positive finite." );
				if ( deferred.ClipDurationTicks < 0 )
					throw new InvalidDataException( $"channel {channel.ChannelIndex} deferred clip length {deferred.ClipDurationTicks} is negative." );
			}
		}
		if ( PinnedNodes != null && PinnedNodes.Where( ( node, index ) => node < 0 || index > 0 && node <= PinnedNodes[index - 1] ).Any() )
			throw new InvalidDataException( "animator pinned nodes are not ascending non-negative node indices." );
		if ( TextureFrames is { } frames )
		{
			if ( frames.Frames == null || frames.AnimatedSlots == null || frames.Frames.Length != frames.AnimatedSlots.Length
				|| frames.Frames.Length > ushort.MaxValue || frames.Version < 0 )
				throw new InvalidDataException( "animator texture-frame history has invalid arrays or version." );
			for ( var slot = 0; slot < frames.Frames.Length; slot++ )
				if ( frames.Frames[slot] < 0 || frames.Frames[slot] > ushort.MaxValue || (!frames.AnimatedSlots[slot] && frames.Frames[slot] != 0) )
					throw new InvalidDataException( $"texture slot {slot} has invalid frame history." );
		}
		if ( NodeStateBits == null )
			throw new InvalidDataException( "animator snapshot has no node-state bits." );
		if ( Meshes == null )
			throw new InvalidDataException( "animator snapshot has no mesh caches." );
		var meshes = new HashSet<int>();
		foreach ( var mesh in Meshes )
		{
			if ( !meshes.Add( mesh.Mesh ) )
				throw new InvalidDataException( $"mesh {mesh.Mesh} appears twice." );
			if ( mesh.Mesh < 0 )
				throw new InvalidDataException( $"mesh index {mesh.Mesh} is negative." );
			if ( mesh.VertexVersion < 0 )
				throw new InvalidDataException( $"mesh {mesh.Mesh} vertex version {mesh.VertexVersion} is negative." );
			if ( mesh.VertexSerial < 0 || mesh.UvSerial < 0 )
				throw new InvalidDataException( $"mesh {mesh.Mesh} cache serials are negative." );
			if ( !float.IsFinite( mesh.VertexTick ) || !float.IsFinite( mesh.UvTick ) )
				throw new InvalidDataException( $"mesh {mesh.Mesh} cache ticks are not finite." );
			if ( mesh.Positions != null && mesh.Positions.Length % 3 != 0 )
				throw new InvalidDataException( $"mesh {mesh.Mesh} position array is not XYZ triples." );
			if ( mesh.TexCoords != null && mesh.TexCoords.Length % 2 != 0 )
				throw new InvalidDataException( $"mesh {mesh.Mesh} texture-coordinate array is not UV pairs." );
			foreach ( var value in mesh.Positions ?? Array.Empty<float>() )
				if ( !float.IsFinite( value ) )
					throw new InvalidDataException( $"mesh {mesh.Mesh} position cache holds a non-finite value." );
			foreach ( var value in mesh.TexCoords ?? Array.Empty<float>() )
				if ( !float.IsFinite( value ) )
					throw new InvalidDataException( $"mesh {mesh.Mesh} texture-coordinate cache holds a non-finite value." );
		}
		if ( VertexLimitations == null || VertexLimitations.Any( limitation => limitation == null ) )
			throw new InvalidDataException( "animator snapshot has a null vertex limitation." );
	}
}
