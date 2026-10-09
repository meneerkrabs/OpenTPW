using OpenTPW.FrontEnd;

namespace OpenTPW;

/// <summary>
/// How a park is started. Separate from <see cref="GameMode"/> (the front-end button) and from
/// <see cref="ParkGameMode"/> (the economy rules): the original keeps its GameType, the front-end
/// exit code and the main-loop state apart (docs/reverse/PPC-scenarios.md, "GameType versus other
/// state values"), and so does OpenTPW.
/// </summary>
public enum ParkStartKind
{
	/// <summary>New Full Simulation park (Mac GameType 0): standard balance, no shipped seed.</summary>
	FullSimulation,
	/// <summary>New Instant Action park (Mac GameType 2): <c>Easy_</c> balance layer and the shipped <c>Easymode.TPWI</c> seed where the level has them.</summary>
	InstantAction,
	/// <summary>
	/// OpenTPW inspection path (<c>--load-original-level</c>, the Load Park entry of a shipped save):
	/// imports the shipped save with the balance it was made with, under Full Simulation rules. This
	/// combination is not an original GameType.
	/// </summary>
	OriginalSaveReference
}

/// <summary>What a level start resolved to once the level's files are known.</summary>
/// <param name="EasyBalance">Layer the theme's <c>Easy_Standard.sam</c> and <c>Easy_</c> object files.</param>
/// <param name="ImportShippedSave">Import the level's <c>Easymode.TPWI</c> as the starting park.</param>
public readonly record struct ParkStart( ParkStartKind Kind, ParkGameMode Mode, bool EasyBalance, bool ImportShippedSave )
{
	/// <summary>The front-end Game Mode button that was chosen. An explicit map: the two enums have different value orders.</summary>
	public static ParkStartKind FromFrontEnd( GameMode mode ) => mode switch
	{
		GameMode.FullSimulation => ParkStartKind.FullSimulation,
		GameMode.InstantAction => ParkStartKind.InstantAction,
		_ => throw new ArgumentOutOfRangeException( nameof( mode ), mode, null )
	};

	/// <summary>
	/// Whether the level's shipped save should be read at all. Mac evidence: creating an Instant Action
	/// player copies each theme's <c>easymode.TPWI</c> into the player directory (<c>0x137600</c>);
	/// nothing copies it for Full Simulation players. The Full Simulation new-park loader itself was not
	/// traced, so "no seed" for Full Simulation rests on that copy being Instant-Action-only.
	/// </summary>
	public static bool ReadsShippedSave( ParkStartKind kind ) => kind != ParkStartKind.FullSimulation;

	/// <summary>
	/// Resolves a start for a level. The <c>Easy_</c> balance layer is added only for GameType 2 and a
	/// missing layer is not an error (Mac <c>0x10474c</c>); the shipped save is the Instant Action seed.
	/// The reference path keeps the balance the shipped save was made with (its loan table matches
	/// only <c>Easy_Standard.sam</c>, checked by <see cref="OriginalEconomyImport"/>).
	/// </summary>
	public static ParkStart Resolve( ParkStartKind kind, bool levelShipsSave, bool themeHasEasyLayer ) => kind switch
	{
		ParkStartKind.FullSimulation => new( kind, ParkGameMode.FullSimulation, false, false ),
		ParkStartKind.InstantAction => new( kind, ParkGameMode.InstantAction, themeHasEasyLayer, levelShipsSave ),
		ParkStartKind.OriginalSaveReference => new( kind, ParkGameMode.FullSimulation, levelShipsSave && themeHasEasyLayer, levelShipsSave ),
		_ => throw new ArgumentOutOfRangeException( nameof( kind ), kind, null )
	};
}

/// <summary>
/// Park features each <see cref="ParkGameMode"/> offers. Every Instant Action gate is a direct
/// GameType test in the Mac executable (docs/reverse/PPC-scenarios.md, "Instant Action
/// availability"); PC equivalence is not established. Research points come from researcher staff in
/// both modes: the Mac code has no staffless research path.
/// </summary>
public readonly record struct ParkModeFeatures( bool GoldenTickets, bool Challenges, bool Loans, bool ResearchPanel, bool Upgrades )
{
	public static ParkModeFeatures For( ParkGameMode mode ) => mode switch
	{
		ParkGameMode.FullSimulation => new( true, true, true, true, true ),
		// Mac: tickets 0xd2f1c and challenges 0xcfef4 run for GameType 0 only; bank panel 0x154aa0 and the
		// loan buttons are refused; research panel 0x161910 shows UITEXT 467; upgrade list 0x165a0c shows UITEXT 27.
		ParkGameMode.InstantAction => new( false, false, false, false, false ),
		_ => throw new ArgumentOutOfRangeException( nameof( mode ), mode, null )
	};
}
