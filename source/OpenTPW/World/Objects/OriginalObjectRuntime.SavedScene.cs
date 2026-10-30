namespace OpenTPW;

/// <summary>
/// One saved animation time control of an original save (<see cref="SaveRideSystemControl"/>, tp.exe loader 0x4647A0) decoded for a channel restore: the clip
/// (AnimID and sub-animation), whether it loops or ended, its speed, the milliseconds since its start on the save's game clock and the deferred clip, if any.
/// </summary>
public readonly record struct SavedChannelState( int AnimationId, int Variant, bool Loop, bool Ended, float Speed, long ElapsedMilliseconds,
	int DeferredAnimationId, int DeferredVariant, bool DeferredLoop, float DeferredSpeed )
{
	/// <summary>Saved channel flag 0x1: the clip loops (0x472F60 stores the trigger's flag bit 0x1 there).</summary>
	public const uint LoopFlag = 0x1;
	/// <summary>Saved channel flag 0x2: AnimID 13 holds the clip on its first frame (0x472F60, 0x4735D0).</summary>
	public const uint FirstFrameFlag = 0x2;
	/// <summary>Saved channel flag 0x4: the clip ended and stays frozen on its last frame (AnimID 14: 0x472F60 sets 0x14).</summary>
	public const uint EndedFlag = 0x4;
	/// <summary>Saved channel flag 0x40: the channel runs on the unscaled clock 0x7B4974 (0x4735D0).</summary>
	public const uint UnscaledClockFlag = 0x40;

	/// <summary>True when the control defers a clip (<see cref="DeferredAnimationId"/> below 12).</summary>
	public bool HasDeferred => DeferredAnimationId != ObjectAnimator.NoAnimation;

	/// <summary>
	/// Decodes one saved control. Null with a null <paramref name="gap"/>: the control holds no clip (AnimID 12 or above), which the loader does not bind either.
	/// Null with a <paramref name="gap"/>: a state the object animator does not model. <paramref name="clockWord"/> is the save's game clock (the first Clock word,
	/// 0x402E30), which the loader sets again; a save without it uses the control's own AnimTime.
	/// </summary>
	// [BIN:TPW-EXE:0x00464BEF anim control restore] the eleven saved words are, in order, flags, AnimID, SubAnim, StartAnimTime, AnimTime, NoPauseAnimTime, the speed, the deferred AnimID, SubAnim, flags and speed: the loader copies them to the live control's +0x0, +0x4, +0x8, +0x10, +0x14, +0x18, +0xC, +0x24, +0x28, +0x2C and +0x30, whose names the debug dump 0x464580 prints (Flags, AnimID, SubAnim, StartAnimTime, AnimTime, NoPauseAnimTime, DeferredAnimID, DeferredSubAnim, DeferredFlags), the speed being the +0xC the frame formula multiplies (0x473490)
	// [BIN:TPW-EXE:0x004735D0 restored channel update] the next channel update takes the frame as (u32)(clock − StartAnimTime) × speed × 0.03 on the clock the save writer stored and the loader set again (0x4164C0, 0x415270); flags & 6 = 4 (ended) keeps the frame at its total, flag 0x2 at 0; an AnimID-12 control binds nothing (0x472D10)
	public static SavedChannelState? Decode( SaveRideSystemControl control, uint? clockWord, out string? gap )
	{
		ArgumentNullException.ThrowIfNull( control );
		gap = null;
		var words = control.Words;
		if ( words.Length != 11 )
			throw new ArgumentException( $"A saved control holds 11 words, not {words.Length}.", nameof( control ) );
		var flags = words[0];
		var animation = words[1];
		if ( animation >= ObjectAnimator.NoAnimation )
			return null;
		var variant = words[2];
		var speed = BitConverter.UInt32BitsToSingle( words[6] );
		if ( (flags & UnscaledClockFlag) != 0 )
			gap = $"animation {animation}/{variant} runs on the unscaled clock (flag 0x40), which no object channel of the corpus saves uses";
		else if ( (flags & FirstFrameFlag) != 0 )
			gap = $"animation {animation}/{variant} is held on its first frame (flag 0x2, AnimID 13), which the object animator does not model";
		else if ( !float.IsFinite( speed ) || speed <= 0 )
			gap = $"animation {animation}/{variant} has speed {speed}";
		else if ( variant > int.MaxValue )
			gap = $"animation {animation} has sub-animation {variant}";
		var deferredAnimation = words[7];
		var deferredVariant = words[8];
		var deferredSpeed = BitConverter.UInt32BitsToSingle( words[10] );
		if ( gap == null && deferredAnimation != ObjectAnimator.NoAnimation )
		{
			if ( deferredAnimation > ObjectAnimator.NoAnimation )
				gap = $"animation {animation}/{variant} defers AnimID {deferredAnimation}, which is not a clip";
			else if ( !float.IsFinite( deferredSpeed ) || deferredSpeed <= 0 || deferredVariant > int.MaxValue )
				gap = $"animation {animation}/{variant} defers {deferredAnimation}/{deferredVariant} at speed {deferredSpeed}";
		}
		if ( gap != null )
			return null;
		var hasDeferred = deferredAnimation != ObjectAnimator.NoAnimation;
		// the u32 difference of 0x4735D0 on the clock the loader set again
		var elapsed = unchecked( (clockWord ?? words[4]) - words[3] );
		return new SavedChannelState( (int)animation, (int)variant, (flags & LoopFlag) != 0, (flags & EndedFlag) != 0, speed, elapsed,
			hasDeferred ? (int)deferredAnimation : ObjectAnimator.NoAnimation, hasDeferred ? (int)deferredVariant : 0, hasDeferred && (words[9] & LoopFlag) != 0, hasDeferred ? deferredSpeed : 0 );
	}
}

public sealed partial class OriginalObjectRuntime
{
	/// <summary>
	/// Restores what an original save's scene-mesh record (<see cref="SaveRideSystemRecord"/>, tp.exe loader 0x4647A0) holds for this object's model: the stored node
	/// states when the record's node and embedded-node counts are the model's, and every saved animation time control whose AnimID is below 12, one per channel in
	/// control order (<see cref="SavedChannelState"/>). Returns one line per control or node table it could not restore.
	/// </summary>
	// [BIN:TPW-EXE:0x004647A0 scene record load] the loader compares the record's node count (+0x2a) and embedded-node count (+0x2c) with the model's (+0x42, +0x48: nodes and dummy attribute records) and writes the node states only when both match (0x464AB4); the controls are restored whatever the counts (0x464B98), one per live control of the instance
	internal IReadOnlyList<string> RestoreSavedScene( SaveRideSystemRecord record, uint? clockWord )
	{
		ArgumentNullException.ThrowIfNull( record );
		var gaps = new List<string>();
		SavedSceneRestored = true;
		SavedChannelGaps.Clear();
		if ( record.NodeCount == Model.Nodes.Count && record.EmbeddedCount == Model.DummyAttributes.Count )
		{
			// [BIN:TPW-EXE:0x00464AF6 node state load] saved bit 0 becomes the node flag 0x10 (toggled: the draw skips the node's mesh) and saved bit 10 the flag 0x80000000 (no node list sets the node); the record's nodes are the model's mesh nodes, then its dummies, the order of the model's own nodes
			Animator.RestoreSavedNodeStates( record.NodeStates.Select( state => (state.Flags & ModelAnimationTrack.ToggledNodeStateBit) != 0 ).ToArray(),
				record.NodeStates.Select( state => (state.Flags & 0x80000000u) != 0 ).ToArray() );
		}
		else
			gaps.Add( $"node states not restored: the record stores {record.NodeCount} nodes and {record.EmbeddedCount} embedded nodes, the model has {Model.Nodes.Count} and {Model.DummyAttributes.Count} (the loader skips them too)" );
		for ( var channel = 0; channel < record.Controls.Length; channel++ )
			if ( RestoreSavedControl( channel, record.Controls[channel], clockWord ) is { } gap )
			{
				gaps.Add( $"channel {channel}: {gap}" );
				SavedChannelGaps.Add( channel );
			}
		return gaps;
	}

	/// <summary>True once <see cref="RestoreSavedScene"/> ran for this object (its RideSystem record was applied).</summary>
	internal bool SavedSceneRestored { get; private set; }

	/// <summary>The channels whose saved control <see cref="RestoreSavedScene"/> reported a gap for (the control held a clip it could not restore).</summary>
	internal HashSet<int> SavedChannelGaps { get; } = new();

	/// <summary>One saved control on its channel (<see cref="ObjectAnimator.RestoreSaved"/>); null when restored or when the control holds no clip, else why not.</summary>
	private string? RestoreSavedControl( int channel, SaveRideSystemControl control, uint? clockWord )
	{
		if ( SavedChannelState.Decode( control, clockWord, out var gap ) is not { } state )
			return gap;
		var clip = ResolveSavedClip( state.AnimationId, state.Variant, out var name );
		// [BIN:TPW-EXE:0x00472D10 saved clip bind] a sub-animation the AnimID's group does not have binds nothing (the loader skips 0x472CB0 when 0x472D10 fails or yields no clip, je 0x464C5D)
		if ( clip == null )
			return $"animation {state.AnimationId}/{state.Variant} does not resolve in the object's archive";
		(ModelAnimation, string, int, int, bool, float)? deferred = null;
		if ( state.HasDeferred )
		{
			var next = ResolveSavedClip( state.DeferredAnimationId, state.DeferredVariant, out var nextName );
			if ( next == null )
				return $"the deferred animation {state.DeferredAnimationId}/{state.DeferredVariant} does not resolve in the object's archive";
			deferred = (next, nextName!, state.DeferredAnimationId, state.DeferredVariant, state.DeferredLoop, state.DeferredSpeed);
		}
		try
		{
			Animator.RestoreSaved( channel, clip, name!, state.AnimationId, state.Variant, state.Loop, state.Speed, state.ElapsedMilliseconds, state.Ended, deferred );
		}
		catch ( InvalidDataException exception )
		{
			return $"animation {state.AnimationId}/{state.Variant} could not be bound: {exception.Message}";
		}
		return null;
	}

	private ModelAnimation? ResolveSavedClip( int animation, int variant, out string? name )
	{
		name = null;
		var file = Entry.ResolveAnimation( animation, variant );
		if ( file == null || LoadClip( file.Path ) is not { } clip )
			return null;
		name = file.Name;
		return clip;
	}
}
