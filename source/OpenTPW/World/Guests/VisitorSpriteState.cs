namespace OpenTPW;

/// <summary>The decoded body programs reached by ordinary guests and scripted ride bodies (table 0x0075A418).</summary>
public enum VisitorSpriteTemplate { Walk = 1, Run = 2, Stand = 3, Indecision = 4, Cheer = 5, Program6 = 6, Vomit = 7, Program8 = 8 }

/// <summary>Body script data. The basic loops reuse StaffScripts; finite guest programs jump to stand after their prefix.</summary>
internal static class VisitorSpriteScripts
{
	// [BIN:TPW-EXE:0x0075A418 guest sprite programs] templates 4..8 set flags 0x1200, SLOT, then FRAME yields and JMP to stand 3; no hold or delete opcode
	private static readonly IReadOnlyDictionary<VisitorSpriteTemplate, StaffScript> Finite = new Dictionary<VisitorSpriteTemplate, StaffScript>
	{
		[VisitorSpriteTemplate.Indecision] = new( 14, Enumerable.Range( 0, 4 ).SelectMany( _ => new[] { 0, 0, 1, 1 } ).ToArray(), 3 ),
		[VisitorSpriteTemplate.Cheer] = new( 12, new[] { 0, 1, 2, 3 }, 3 ),
		[VisitorSpriteTemplate.Program6] = new( 11, new[] { 0, 1, 2, 3 }, 3 ),
		[VisitorSpriteTemplate.Vomit] = new( 6, new[] { 0, 1, 2, 3 }, 3 ),
		[VisitorSpriteTemplate.Program8] = new( 4, new[] { 0, 1, 2, 3 }, 3 )
	};

	public static StaffScript Get( VisitorSpriteTemplate template ) => (int)template <= 3 ? StaffScripts.Get( (int)template )! : Finite[template];
}

/// <summary>The last FRAME emitted by a body program; a missing value means that the body has not become visible.</summary>
public readonly record struct VisitorSpriteFrame( int Slot, int Frame );

/// <summary>Presentation-only player state, with absolute signed millisecond timestamps in the host's saved clock epoch.</summary>
public sealed record VisitorSpritePlayerSnapshot( VisitorSpriteTemplate ActiveTemplate, int NextFrame, int Interval, int NextDue, VisitorSpriteFrame? Output, VisitorSpriteTemplate CurrentProgram );

/// <summary>The person's wrapper and its optional live player. Deleting a player does not delete pending wrapper requests.</summary>
public sealed record VisitorSpriteStateSnapshot( VisitorSpritePlayerSnapshot? Player, VisitorSpriteTemplate? PendingTemplate, int PendingInterval );

/// <summary>
/// A bounded, GPU-free player for the decoded guest body programs. The scheduler supplies one shared signed 32-bit millisecond sample per opportunity;
/// this object neither reads a clock nor decides when opportunities occur. This is not the general sprite interpreter or the staff renderer's clock.
/// </summary>
public sealed class VisitorSpritePlayer
{
	public VisitorSpritePlayer( VisitorSpriteTemplate template, int nowMilliseconds )
	{
		ValidateTemplate( template );
		ActiveTemplate = CurrentProgram = template;
		NextDue = unchecked( nowMilliseconds + SpritePlayer.DefaultInterval );
	}

	public VisitorSpriteTemplate ActiveTemplate { get; private set; }
	/// <summary>The program containing the next FRAME. A natural JMP changes this without changing the requested/active identity.</summary>
	public VisitorSpriteTemplate CurrentProgram { get; private set; }
	/// <summary>The next FRAME's index in the active loop, independently of the previously emitted output.</summary>
	public int NextFrame { get; private set; }
	public int Interval { get; private set; } = SpritePlayer.DefaultInterval;
	public int NextDue { get; private set; }
	public VisitorSpriteFrame? Output { get; private set; }

	/// <summary>Emits at most one frame when signed now &gt; due, then rebases due on now. Equality and missed intervals do not advance the cursor.</summary>
	// [BIN:TPW-EXE:0x00475360 visitor sprite clock] signed jle at 0x4753B9 skips now <= due; 0x47540D writes now + interval, with 32-bit wrap and no catch-up loop
	// [BIN:TPW-EXE:0x00476940 visitor sprite frame] FRAME writes the frame, sets visibility and yields; setup and JMP do not consume a separate frame
	public bool Tick( int nowMilliseconds )
	{
		if ( nowMilliseconds <= NextDue )
			return false;
		var script = VisitorSpriteScripts.Get( CurrentProgram );
		Output = new VisitorSpriteFrame( script.Slot, script.Frames[NextFrame] );
		NextFrame++;
		if ( NextFrame == script.Frames.Count )
		{
			CurrentProgram = (VisitorSpriteTemplate)script.Next;
			NextFrame = 0;
		}
		NextDue = unchecked( nowMilliseconds + Interval );
		return true;
	}

	/// <summary>Restarts the cursor only. The old output, visibility, interval and due timestamp survive application of a new program.</summary>
	// [BIN:TPW-EXE:0x00475B80 visitor template application] direct program pointers replace current/restart PC and control bookkeeping, without writing due, interval or output
	public void ApplyTemplate( VisitorSpriteTemplate template )
	{
		ValidateTemplate( template );
		ActiveTemplate = CurrentProgram = template;
		NextFrame = 0;
	}

	/// <summary>Applies a walk interval request. Zero is no change; changing the interval never moves an already scheduled due timestamp.</summary>
	public void ApplyInterval( int interval )
	{
		ValidateInterval( interval );
		if ( interval != 0 )
			Interval = interval;
	}

	public VisitorSpritePlayerSnapshot CaptureSnapshot() => new( ActiveTemplate, NextFrame, Interval, NextDue, Output, CurrentProgram );

	/// <summary>Restores presentation history without rebasing its timestamps. The caller must restore the same host clock epoch.</summary>
	public static VisitorSpritePlayer RestoreSnapshot( VisitorSpritePlayerSnapshot snapshot )
	{
		ValidateSnapshot( snapshot );
		return new VisitorSpritePlayer( snapshot.ActiveTemplate, 0 )
		{
			CurrentProgram = snapshot.CurrentProgram,
			NextFrame = snapshot.NextFrame,
			Interval = snapshot.Interval,
			NextDue = snapshot.NextDue,
			Output = snapshot.Output
		};
	}

	public static void ValidateSnapshot( VisitorSpritePlayerSnapshot snapshot )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		if ( !IsSupported( snapshot.ActiveTemplate ) || !IsSupported( snapshot.CurrentProgram ) ||
			snapshot.CurrentProgram != snapshot.ActiveTemplate && (snapshot.CurrentProgram != VisitorSpriteTemplate.Stand || (int)snapshot.ActiveTemplate <= 3) ||
			snapshot.NextFrame < 0 || snapshot.NextFrame >= VisitorSpriteScripts.Get( snapshot.CurrentProgram ).Frames.Count ||
			snapshot.Interval is < 1 or > SpritePlayer.LongestInterval || snapshot.Output is { } frame && !IsValidOutput( frame ) ||
			snapshot.Output == null && (snapshot.NextFrame != 0 || snapshot.CurrentProgram != snapshot.ActiveTemplate) )
			throw new InvalidDataException( "Visitor sprite player snapshot is inconsistent." );
	}

	internal static bool IsValidOutput( VisitorSpriteFrame frame ) => frame.Slot == 0 && frame.Frame == 0 || frame.Slot is 1 or 2 && frame.Frame is >= 0 and < 8 ||
		frame.Slot == 14 && frame.Frame is 0 or 1 || frame.Slot is 4 or 6 or 11 or 12 && frame.Frame is >= 0 and < 4;
	internal static bool IsSupported( VisitorSpriteTemplate template ) => (int)template is >= 1 and <= 8;
	internal static void ValidateTemplate( VisitorSpriteTemplate template )
	{
		if ( !IsSupported( template ) )
			throw new ArgumentOutOfRangeException( nameof( template ), "Only decoded guest programs 1 through 8 are supported." );
	}
	internal static void ValidateInterval( int interval )
	{
		if ( interval is < 0 or > SpritePlayer.LongestInterval )
			throw new ArgumentOutOfRangeException( nameof( interval ) );
	}
}

/// <summary>
/// Persistent person-side wrapper for the bounded body player. Scheduler ticks, guest begin-update application, and body-pose requests are separate phases.
/// No bounce-table lifetime, visibility test, render call or navigation step is implied by this object.
/// </summary>
public sealed class VisitorSpriteState
{
	public VisitorSpritePlayer? Player { get; private set; }
	public VisitorSpriteTemplate? PendingTemplate { get; private set; }
	public int PendingInterval { get; private set; }

	/// <summary>Creates a missing body with a fresh due/interval and no visible frame, retaining the person's independent pending requests.</summary>
	// [BIN:TPW-EXE:0x004D4140 visitor sprite creation] installs the new handle only; the wrapper's pending template and interval are not cleared
	public VisitorSpritePlayer CreatePlayer( VisitorSpriteTemplate template, int nowMilliseconds )
	{
		if ( Player != null )
			throw new InvalidOperationException( "Delete the existing visitor sprite before creating another." );
		return Player = new VisitorSpritePlayer( template, nowMilliseconds );
	}

	/// <summary>Body-pose requests create a missing player, otherwise queue only when neither the active nor pending program matches.</summary>
	// [BIN:TPW-EXE:0x004F9E60 visitor body request] creates through 0x4D4140 only with no handle; 0x4D4100 tests both active and pending before the request
	public void RequestBody( VisitorSpriteTemplate template, int nowMilliseconds )
	{
		VisitorSpritePlayer.ValidateTemplate( template );
		if ( Player == null )
			CreatePlayer( template, nowMilliseconds );
		else if ( Player.ActiveTemplate != template && PendingTemplate != template )
			PendingTemplate = template;
	}

	/// <summary>The walk caller tests only the active program, so an identical pending request does not prevent a later walk request.</summary>
	public void RequestWalkTemplate( VisitorSpriteTemplate template )
	{
		VisitorSpritePlayer.ValidateTemplate( template );
		if ( Player?.ActiveTemplate != template )
			PendingTemplate = template;
	}

	/// <summary>State-entry and stop-walk callers queue directly, including a restart of the active program.</summary>
	public void QueueTemplate( VisitorSpriteTemplate template )
	{
		VisitorSpritePlayer.ValidateTemplate( template );
		PendingTemplate = template;
	}

	public void QueueInterval( int interval )
	{
		VisitorSpritePlayer.ValidateInterval( interval );
		PendingInterval = interval;
	}

	/// <summary>Guest begin-update applies template then interval, clearing each request even if its player was deleted.</summary>
	// [BIN:TPW-EXE:0x004D4190 visitor pending application] the underlying 0x475B80 and 0x475610 ignore a zero handle; the wrapper still clears both words
	public void ApplyPending()
	{
		if ( PendingTemplate is { } template )
			Player?.ApplyTemplate( template );
		PendingTemplate = null;
		if ( PendingInterval != 0 )
			Player?.ApplyInterval( PendingInterval );
		PendingInterval = 0;
	}

	public bool Tick( int nowMilliseconds ) => Player?.Tick( nowMilliseconds ) ?? false;

	/// <summary>Deletes only the live body. Pending requests belong to the person and survive until application or a full new-person reset.</summary>
	// [BIN:TPW-EXE:0x004D4170 visitor sprite deletion] deletes the sprite and zeroes the handle without touching the pending words
	public void DeletePlayer() => Player = null;

	/// <summary>A new person starts with the wrapper constructor's three zero words; recycled identities cannot inherit an old person's requests.</summary>
	// [BIN:TPW-EXE:0x004D3ED0 visitor wrapper creation] initializes handle, pending template and pending interval to zero
	public void ResetForNewPerson()
	{
		Player = null;
		PendingTemplate = null;
		PendingInterval = 0;
	}

	public VisitorSpriteStateSnapshot CaptureSnapshot() => new( Player?.CaptureSnapshot(), PendingTemplate, PendingInterval );

	/// <summary>Validates the whole wrapper before replacing it; all due timestamps retain the supplied snapshot's clock epoch.</summary>
	public void RestoreSnapshot( VisitorSpriteStateSnapshot snapshot )
	{
		ValidateSnapshot( snapshot );
		Player = snapshot.Player is { } player ? VisitorSpritePlayer.RestoreSnapshot( player ) : null;
		PendingTemplate = snapshot.PendingTemplate;
		PendingInterval = snapshot.PendingInterval;
	}

	public static void ValidateSnapshot( VisitorSpriteStateSnapshot snapshot )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		if ( snapshot.PendingTemplate is { } template && !VisitorSpritePlayer.IsSupported( template ) || snapshot.PendingInterval is < 0 or > SpritePlayer.LongestInterval )
			throw new InvalidDataException( "Visitor sprite wrapper snapshot is inconsistent." );
		if ( snapshot.Player is { } player )
			VisitorSpritePlayer.ValidateSnapshot( player );
	}
}
