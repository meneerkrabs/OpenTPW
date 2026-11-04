using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	/// <summary>
	/// One occupied slot of the message tube: a tag window of the original (<c>0x48DAE0</c>) with the properties the add routine <c>0x48EB20</c> stores in it. The identity (tag, context word and dword)
	/// is what a later post of the same or a replacing tag matches; the two timers are the window's expiry (<c>0x80080</c>, the lifetime) and warning (<c>0x80081</c>, five seconds earlier) timers.
	/// </summary>
	private sealed class TubeRow( ChallengeMessageIdentity identity, int lifetime = 0, bool isOffer = false, int icon = 0 )
	{
		public const int WarningLead = 5000;

		/// <summary>A challenge offer (second action 0x1B, the decline): reading it leaves its timers running, and removing it declines.</summary>
		public bool IsOffer { get; } = isOffer;
		public ActiveChallenge? OfferIdentity { get; init; }
		public ActiveChallenge? PendingOfferIdentity { get; set; }
		public ChallengeMessageIdentity Identity { get; } = identity;

		/// <summary>The tag's lifetime in milliseconds; 0 keeps the row until it is deleted, answered or expires another way.</summary>
		public int Lifetime { get; } = lifetime;
		public int Icon { get; } = icon;

		/// <summary>A row with a lifetime that is not an offer: it expires unread and is read away from its timers by a selection.</summary>
		public bool IsTerminal => Lifetime > 0 && !IsOffer;

		// [BIN:TPW-EXE:0x48ECD2 tag timers] a lifetime above 0 sets the expiry timer 0x80080 to it and the warning timer 0x80081 to 5000 ms less, enables both (the origin is the clock of that moment) and sets the window's byte +0x134
		public UiTimer? Expiry { get; private set; }
		public UiTimer? Warning { get; private set; }

		/// <summary>The window's byte +0x134: set with the timers, cleared by the expiry, by a selection that stops the timers and by a right click; the expiry message acts only while it is set.</summary>
		public bool ExpiryArmed { get; set; }

		/// <summary>The warning timer has fired once (it makes the retained particle and disables itself).</summary>
		public bool WarningFired { get; set; }

		/// <summary>The window is hidden (the expiry, a right click and the bomb hide it before the delayed removal; an answer leaves it drawn until the removal).</summary>
		public bool Hidden { get; set; }

		/// <summary>The window's hit-test attribute is cleared and its removal is queued for this clock reading (message 0x100002 -> 0x4A13E8: the removal is delayed 500 ms).</summary>
		public long? ReleaseAt { get; set; }

		/// <summary>The removal a right click on the tube's body queued for this row (message 0x100003, 100 ms apart); the row stays until then.</summary>
		public long? ClearAt { get; set; }

		/// <summary>The window's property 9: the retained UI particle (0x5B) of the warning timer, null for the original's -1.</summary>
		public object? ParticleHandle { get; set; }

		/// <summary>The window's property 10: the first-visibility particle (0x5A) was made.</summary>
		public bool FirstShown { get; set; }

		/// <summary>Where the retained particle was last put (native units), so a moved icon moves it once.</summary>
		public (int X, int Z) ParticleAt { get; set; }

		/// <summary>The row can still be clicked: its hit-test attribute is set until a removal starts.</summary>
		public bool Visible => !Hidden && ReleaseAt == null;

		/// <summary>The row is drawn (an answered row stays drawn, unclickable, until its removal).</summary>
		public bool Drawn => !Hidden;

		/// <summary>The timer and flag state the add routine gives a row with a lifetime (a lifetime of 0 makes none).</summary>
		public void StartTimers( int now )
		{
			if ( Lifetime <= 0 )
				return;
			Expiry = new UiTimer();
			Expiry.SetInterval( Lifetime );
			Expiry.Enable( now );
			ExpiryArmed = true;
			Warning = new UiTimer();
			Warning.SetInterval( Lifetime - WarningLead );
			Warning.Enable( now );
		}

		/// <summary>
		/// A selection (<c>0x48D9C0</c>): an armed row that is not an offer disables both timers and clears the flag; the same armed row's retained particle is ended by the caller.
		/// Returns whether the armed block ran.
		/// </summary>
		// [BIN:TPW-EXE:0x48D9E9 offer keeps its timers] the selection routine kills both timers of a selected tag (0x48d9ee-0x48da06) unless the tag's sixth parameter, its second action, is 0x1B (the decline of an offer)
		public bool Read()
		{
			if ( !ExpiryArmed )
				return false;
			if ( !IsOffer )
			{
				Expiry?.Disable();
				Warning?.Disable();
				ExpiryArmed = false;
			}
			return true;
		}
	}
}
