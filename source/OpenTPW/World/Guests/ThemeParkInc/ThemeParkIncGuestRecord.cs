namespace OpenTPW;

/// <summary>
/// The Theme Park Inc fields of a guest that runs Theme Park Inc's own state machine (<see cref="Guest.Tpi"/>; docs/reverse/TPI-GUESTS.md): the state word and its saved copy, and every word the
/// constructor <c>0x60C3F0</c> writes that the shared <see cref="Guest"/> does not already hold. Null on every Theme Park World guest and on a Theme Park Inc guest of the
/// Theme Park World rules (<c>[EXT:tpi-twp-guest-rules]</c>, the default path).
/// </summary>
/// <remarks>
/// The shared record keeps the fields both games have: the meters (<c>+0x1BC</c> happiness, <c>+0x1C8</c> thirst, <c>+0x1CC</c> hunger, <c>+0x1D0</c> toilet, <c>+0x1D4</c> illness, <c>+0x1D8</c>
/// litter), the purse <c>+0x1C0</c>, the exit counter <c>+0x1E0</c>, the prank code <c>+0x1E4</c>, the visitor number <c>+0x200</c>, the two histories <c>+0x20A..+0x219</c>, the type <c>+0x21A</c>,
/// the umbrella <c>+0x238</c>/<c>+0x23C</c>/<c>+0x244</c>, the walk mask <c>+0x1A0</c>, the stranded mark <c>+0x1B0</c> and the speed term <c>+0xD6</c>. Words no ported code reads yet are kept
/// under their offsets so that a later phase reads them without a new save schema.
/// </remarks>
// [BIN:TPI-EXE:0x60C3F0 guest constructor] the stores at 0x60C425..0x60C571 zero every field listed here (the state pair takes 6 at 0x60C50C/0x60C512, +0x2E8 takes -1 at 0x60C54A), then the stamp
// +0x1FC = the world turn (0x60C589); the tail (0x60CAD9..0x60CAF5) clears +0x22C and the eight history words again
public sealed class ThemeParkIncGuestRecord
{
	/// <summary>The state word <c>+0x2C0</c> (0..30, <see cref="ThemeParkIncGuestStates"/>); the constructor stores 6 before its tail sets 2 or 6.</summary>
	public int State { get; internal set; } = ThemeParkIncGuestStates.Idle;
	/// <summary>The saved state <c>+0x2C4</c>: the state a spot animation (8) returns to (<c>0x60FB30</c> stores it, <c>0x60FC40</c> restores it); 6 at creation.</summary>
	public int Saved { get; internal set; } = ThemeParkIncGuestStates.Idle;
	/// <summary>The sprite object's animation mode (guest <c>+0x14 +0xC</c> = <c>+0x20</c>), written by the setter's entry table: 1 walking, 3 standing. Presentation; kept for the save.</summary>
	public int Mode { get; internal set; }
	/// <summary>The heading word <c>+0x2C</c> (0..0x7FF per turn), which a booth (state 4's entry), the crossing (state 0) and the stop (state 22's arrival) set. Presentation; kept for the save.</summary>
	public int Heading { get; internal set; }
	/// <summary>The fees the guest paid at the booth (<c>+0x1C4</c>: state 4's paying verdict adds the admission fee).</summary>
	public int FeesPaid { get; internal set; }
	/// <summary>The stamp <c>+0x1FC</c>: the park turn of the guest's creation (<c>0x60C589</c>), which the weather rule's day gate and the stay of state 30 count from.</summary>
	public long ArrivalTurn { get; internal set; }
	/// <summary>The target word <c>+0x204</c> (a ride, a bin, an infodesk; state 22's entry clears it).</summary>
	public int Target { get; internal set; }
	/// <summary>The words <c>+0x206</c> and <c>+0x208</c> (the photo booth's minor and major destinations, state 19).</summary>
	public int Word206 { get; internal set; }
	public int Word208 { get; internal set; }
	/// <summary>The byte <c>+0x21B</c> (state 17's entry roll, read by state 14).</summary>
	public int Byte21B { get; internal set; }
	/// <summary>The words <c>+0x21C</c>, <c>+0x220</c> and <c>+0x224</c> (state 14's entry: the move delay and the stamp).</summary>
	public int Word21C { get; internal set; }
	public int Word220 { get; internal set; }
	public int Word224 { get; internal set; }
	/// <summary>The wait <c>+0x228</c>: state 3's entry draws 200 + R % 150 into it, state 4's expensive verdict 50 + R % 50; both count it down.</summary>
	public int Wait { get; internal set; }
	/// <summary>The paid flag <c>+0x22C</c>: set by state 4's paying verdict; state 3 then walks to the entrance and state 30's entry records the stay.</summary>
	public bool Paid { get; internal set; }
	/// <summary>The spot animation's start turn <c>+0x230</c> (state 8's entry); state 8 restores the saved state once the turn is past it plus 10.</summary>
	public long SpotTurn { get; internal set; }
	/// <summary>The word <c>+0x234</c>.</summary>
	public int Word234 { get; internal set; }
	/// <summary>The sprite index <c>+0x248</c> = sprite <c>+0x30</c> + 4 × generation (<c>0x60C90C</c>). Presentation.</summary>
	public int SpriteIndex { get; internal set; }
	/// <summary>The byte <c>+0x28C</c> and the word <c>+0x290</c> (state 14's weather wait, P3).</summary>
	public int Byte28C { get; internal set; }
	public int Word290 { get; internal set; }
	/// <summary>Whether the guest holds a balloon object (<c>+0x294</c> nonzero).</summary>
	public bool HoldsBalloon { get; internal set; }
	/// <summary>The balloon countdown <c>+0x298</c> (unsigned): one less on every phase update in the park; the balloon goes (<c>0x613D00</c>) at 0.</summary>
	public uint BalloonCountdown { get; internal set; }
	/// <summary>The word <c>+0x29C</c> (state 6's prank case 1, P2).</summary>
	public int Word29C { get; internal set; }
	/// <summary>The generation <c>+0x2A4</c> (0 kid, 1 parent, 2 grandparent) from the type's table row (<c>0x60C67D</c>).</summary>
	public int Generation { get; internal set; }
	/// <summary>The preferred excitement <c>+0x2A8</c> and the boredom threshold <c>+0x2AC</c> of the type (<c>0x60C6F2</c>, <c>0x60C707</c>).</summary>
	public int PreferredExcitement { get; internal set; }
	public int BoredomThreshold { get; internal set; }
	/// <summary>The byte <c>+0x2B1</c>: written only by the person minimenu's eight prank commands, whose entries are never shown, so 0 in retail (guest-states-p0.md §8).</summary>
	public int Byte2B1 { get; internal set; }
	/// <summary>The first-aid wait <c>+0x2B4</c> (state 27's entry sets 100).</summary>
	public int FirstAidWait { get; internal set; }
	/// <summary>The words <c>+0x2B8</c> and <c>+0x2BC</c>.</summary>
	public int Word2B8 { get; internal set; }
	public int Word2BC { get; internal set; }
	/// <summary>The infodesk wait <c>+0x2C8</c> (state 18, P5).</summary>
	public int Word2C8 { get; internal set; }
	/// <summary>The queue link words <c>+0x2CC</c> and <c>+0x2CE</c>, cleared by the pre-update outside the queue states (<c>0x619EC3</c>).</summary>
	public int QueueLink { get; internal set; }
	public int QueueSlot { get; internal set; }
	/// <summary>The words <c>+0x2D0</c> to <c>+0x2E0</c>.</summary>
	public int[] Words2D0 { get; } = new int[Words2D0Count];
	/// <summary>The weather day <c>+0x2E8</c> (written by the weather rule, never read; -1 at creation).</summary>
	public int WeatherDay { get; internal set; } = -1;
	/// <summary>The words <c>+0x1B8</c> (state 6 zeroes it every tick), <c>+0x1DC</c> and <c>+0x1EC</c> to <c>+0x1F8</c>.</summary>
	public int Word1B8 { get; internal set; }
	public int Word1DC { get; internal set; }
	public int[] Words1EC { get; } = new int[Words1ECCount];
	/// <summary>The counter <c>+0xE0</c> (state 26's entry sets 10).</summary>
	public int Word0E0 { get; internal set; }

	/// <summary>The number of words at <c>+0x2D0</c>..<c>+0x2E0</c>.</summary>
	public const int Words2D0Count = 5;
	/// <summary>The number of words at <c>+0x1EC</c>..<c>+0x1F8</c>.</summary>
	public const int Words1ECCount = 4;
}
