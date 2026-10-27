namespace OpenTPW;

/// <summary>[EXT:online-coop-undo] What the host decided about a take-back (<see cref="TakeBackCommand"/>, C5).</summary>
public enum TakeBackVerdict : byte
{
	/// <summary>No host decided it (single player, a hand-made command): refused.</summary>
	Undecided = 0,
	/// <summary>The step is taken back by the inverse the command carries.</summary>
	Taken = 1,
	/// <summary>A later command still in effect changed the step's cells, or the step's slide or ride is not as it left it.</summary>
	Changed = 2,
	/// <summary>The step's undo would not do exactly its work now: something stands in its way.</summary>
	InTheWay = 3,
	/// <summary>The member laid no such step that is still in the journal.</summary>
	NotOwn = 4,
	/// <summary>The step is not the member's last of its tool.</summary>
	NotLast = 5,
	/// <summary>The step is older than the member's oldest kept step.</summary>
	Forgotten = 6,
	/// <summary>The step's undo does not fit one command.</summary>
	TooLarge = 7
}

/// <summary>
/// [EXT:online-coop-undo] Backspace in a building tool of a shared park (docs/COOP.md, "Conflicts and undo (C5)"): takes back the issuing player's last step of that tool, which the
/// command names by its record (<paramref name="Step"/>; with <paramref name="Stroke"/>, a pool stroke by its stroke number instead) and, for a slide piece, its ordinal within its record
/// (<paramref name="Part"/>, −1 for none). <paramref name="ClaimedRide"/> is the claimed ride the step lies on (a track ride's or a coaster's attraction id, −1 otherwise), which the rights
/// check whatever the verdict; <paramref name="SelectedRide"/> is the issuer's selected ride, which a path line's undo re-evaluates (as <see cref="UndoLineCommand"/> carries it).
/// <para>
/// The host decides it from its undo journal (<see cref="CoopUndoJournal"/>) before it runs (<see cref="ParkCommands.Prepare"/>): its <paramref name="Verdict"/>, and when taken the
/// <paramref name="Inverse"/>, the tool's own undo of the step, of the issuer's player. Every peer applies the decided record as it comes: a verdict other than taken refuses it before
/// anything runs; taken runs the inverse as the take-back's player, with its refunds.
/// </para>
/// </summary>
public sealed record TakeBackCommand( bool Stroke, long Step, int Part, int ClaimedRide, int SelectedRide, TakeBackVerdict Verdict = TakeBackVerdict.Undecided, ParkCommand? Inverse = null ) : ParkToolCommand
{
	/// <summary>The most bytes the inverse a take-back carries may take.</summary>
	internal const int MaximumInverseBytes = ParkCommandCodec.MaximumBytes - 64;

	/// <summary>The reasons each refusing verdict gives (<see cref="ReasonOf"/>).</summary>
	internal const string UndecidedReason = "No host decided this take-back.";
	internal const string ChangedReason = "Someone has changed that since.";
	internal const string InTheWayReason = "Something is in the way of taking that back.";
	internal const string NotOwnReason = "That is not this player's to take back.";
	internal const string NotLastReason = "That is not this player's last step.";
	internal const string ForgottenReason = "That is too long ago to take back.";
	internal const string TooLargeReason = "That is too large to take back.";

	public override ParkCommandKind Kind => ParkCommandKind.TakeBack;

	internal override bool IsWellFormed => Step >= 0 && Part >= -1 && ClaimedRide >= -1 && SelectedRide >= -1 && Enum.IsDefined( Verdict )
		&& (Verdict == TakeBackVerdict.Taken) == (Inverse != null)
		&& (Inverse == null || (IsInverseKind( Inverse.Kind ) && Inverse.Player == Player && Inverse.IsWellFormed));

	/// <summary>The kinds an inverse may be: the building tools' own undo of a step.</summary>
	internal static bool IsInverseKind( ParkCommandKind kind ) => kind is ParkCommandKind.UndoLine or ParkCommandKind.UndoQueueSegment or ParkCommandKind.UndoPoolSlidePiece;

	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Bool( Stroke );
		writer.Long( Step );
		writer.Int( Part );
		writer.Int( ClaimedRide );
		writer.Int( SelectedRide );
		writer.Byte( (byte)Verdict );
		writer.Bytes( Inverse != null ? ParkCommandCodec.Encode( Inverse ) : ReadOnlySpan<byte>.Empty );
	}

	/// <summary>Reads a take-back's fields (<see cref="ParkCommandCodec.Decode"/>): its inverse is a whole command of its own, of an inverse kind (checked with the player once the envelope's is set).</summary>
	internal static TakeBackCommand Read( ParkCommandReader reader )
	{
		var stroke = reader.Bool();
		var step = reader.Long();
		var part = reader.Int();
		var claimed = reader.Int();
		var selected = reader.Int();
		var verdict = (TakeBackVerdict)reader.Byte();
		var bytes = reader.Bytes( MaximumInverseBytes );
		ParkCommand? inverse = null;
		if ( bytes.Length > 0 )
		{
			inverse = ParkCommandCodec.Decode( bytes );
			if ( !IsInverseKind( inverse.Kind ) )
				throw new InvalidDataException( $"A take-back's inverse is no undo ({inverse.Kind})." );
		}
		return new TakeBackCommand( stroke, step, part, claimed, selected, verdict, inverse );
	}

	/// <summary>The reason a refusing <paramref name="verdict"/> gives.</summary>
	internal static string ReasonOf( TakeBackVerdict verdict ) => verdict switch
	{
		TakeBackVerdict.Changed => ChangedReason,
		TakeBackVerdict.InTheWay => InTheWayReason,
		TakeBackVerdict.NotOwn => NotOwnReason,
		TakeBackVerdict.NotLast => NotLastReason,
		TakeBackVerdict.Forgotten => ForgottenReason,
		TakeBackVerdict.TooLarge => TooLargeReason,
		_ => UndecidedReason
	};

	/// <summary>The verdict whose reason <paramref name="reason"/> is, or null for any other reason (the tools read a refused take-back's verdict from its result).</summary>
	internal static TakeBackVerdict? VerdictOf( string? reason ) => reason switch
	{
		ChangedReason => TakeBackVerdict.Changed,
		InTheWayReason => TakeBackVerdict.InTheWay,
		NotOwnReason => TakeBackVerdict.NotOwn,
		NotLastReason => TakeBackVerdict.NotLast,
		ForgottenReason => TakeBackVerdict.Forgotten,
		TooLargeReason => TakeBackVerdict.TooLarge,
		UndecidedReason => TakeBackVerdict.Undecided,
		_ => null
	};

	internal override ParkCommandResult Execute( Level level ) => Verdict != TakeBackVerdict.Taken || Inverse == null
		? ParkCommandResult.Rejected( ReasonOf( Verdict ) )
		: level.ExecuteTakenBack( Inverse );

	/// <summary>Equal when every field and the inverse's bytes are equal (an inverse's lists compare by reference in a record).</summary>
	public bool Equals( TakeBackCommand? other ) => other is not null && Player == other.Player && Stroke == other.Stroke && Step == other.Step && Part == other.Part
		&& ClaimedRide == other.ClaimedRide && SelectedRide == other.SelectedRide && Verdict == other.Verdict
		&& (Inverse == null ? other.Inverse == null : other.Inverse != null && ParkCommandCodec.Encode( Inverse ).AsSpan().SequenceEqual( ParkCommandCodec.Encode( other.Inverse ) ));

	public override int GetHashCode() => HashCode.Combine( Stroke, Step, Part, ClaimedRide, SelectedRide, Verdict );
}
