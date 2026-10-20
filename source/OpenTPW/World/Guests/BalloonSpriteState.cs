namespace OpenTPW;

/// <summary>Presentation-only sprite-script state. Native create/rebind/pass: 4758F0, 475B80, 475360.
/// Uses the host's documented fixed-60Hz clock, not render time. No gameplay RNG or hash state.</summary>
public sealed class BalloonSpriteState
{
	[System.Text.Json.Serialization.JsonInclude, System.Text.Json.Serialization.JsonRequired]
	public long Identity { get; internal set; }
	[System.Text.Json.Serialization.JsonInclude, System.Text.Json.Serialization.JsonRequired]
	public int ColourGuestId { get; internal set; }
	[System.Text.Json.Serialization.JsonInclude, System.Text.Json.Serialization.JsonRequired]
	public int? OwnerId { get; internal set; }
	[System.Text.Json.Serialization.JsonInclude, System.Text.Json.Serialization.JsonRequired]
	public int Deadline { get; internal set; }
	[System.Text.Json.Serialization.JsonInclude, System.Text.Json.Serialization.JsonRequired]
	public bool Visible { get; internal set; }
	[System.Text.Json.Serialization.JsonInclude, System.Text.Json.Serialization.JsonRequired]
	public bool PendingBurst { get; internal set; }
	[System.Text.Json.Serialization.JsonInclude, System.Text.Json.Serialization.JsonRequired]
	public int BurstStep { get; internal set; } = -1;
	[System.Text.Json.Serialization.JsonInclude, System.Text.Json.Serialization.JsonRequired]
	public bool Ended { get; internal set; }
	[System.Text.Json.Serialization.JsonInclude, System.Text.Json.Serialization.JsonRequired]
	public bool HasPosition { get; internal set; }
	[System.Text.Json.Serialization.JsonInclude, System.Text.Json.Serialization.JsonRequired]
	public float X { get; internal set; }
	[System.Text.Json.Serialization.JsonInclude, System.Text.Json.Serialization.JsonRequired]
	public float Y { get; internal set; }
	[System.Text.Json.Serialization.JsonInclude, System.Text.Json.Serialization.JsonRequired]
	public float Lift { get; internal set; }
	[System.Text.Json.Serialization.JsonInclude, System.Text.Json.Serialization.JsonRequired]
	public float LastX { get; internal set; }
	[System.Text.Json.Serialization.JsonInclude, System.Text.Json.Serialization.JsonRequired]
	public float LastY { get; internal set; }
	[System.Text.Json.Serialization.JsonIgnore]
	public int Frame => BurstStep < 0 ? 0 : 1;
	[System.Text.Json.Serialization.JsonIgnore]
	public int Alpha => BurstStep < 0 ? 255 : 250 - 20 * BurstStep;

	internal void Burst()
	{
		OwnerId = null;
		PendingBurst = true; // Native direct rebind preserves deadline, frame and visibility.
	}

	/// <summary>True when an already-ended native slot is reclaimed. Signed strict comparison is deliberate, including wrap.</summary>
	internal bool Tick( int now )
	{
		if ( now <= Deadline ) return false;
		if ( Ended ) return true;
		if ( PendingBurst ) { PendingBurst = false; BurstStep = 0; }
		else if ( BurstStep >= 0 ) BurstStep++;
		Ended = BurstStep >= 13;
		Visible = !Ended;
		Deadline = unchecked( now + 62 );
		return false;
	}

	internal BalloonSpriteState Copy() => (BalloonSpriteState)MemberwiseClone();
}

/// <summary>Versioned optional presentation section; absent old/native saves reconstruct held players only (SAVE-013).</summary>
public sealed record BalloonPresentationSnapshot
{
	public required int Version { get; init; }
	public required long NextIdentity { get; init; }
	public required bool HistoryReconstructed { get; init; }
	public required BalloonSpriteState[] Players { get; init; }
}
