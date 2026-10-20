using System.Text;
using OpenTPW.Hud;

namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-commands] A command of the park's settings (docs/COOP.md, "Settings"): what the HUD's windows change in the economy, the attractions, the staff, the
/// park's name, its speed and its cheats (C1 part 2). It checks what its edit would refuse, then makes the edit through the park's own call; the dispatcher has checked
/// the visit and the player's rights already (<see cref="ParkCommands"/>), and <see cref="Level.ExecuteCommand"/> hands it to <see cref="Execute"/>.
/// </summary>
public abstract record ParkSettingCommand : ParkCommand
{
	private static readonly UTF8Encoding StrictUtf8 = new( false, true );

	/// <summary>Runs the command on <paramref name="level"/>: accepted, or rejected without a change.</summary>
	internal abstract ParkCommandResult Execute( Level level );

	/// <summary>
	/// Runs <paramref name="command"/> for <see cref="Level.ExecuteCommand"/>. A park call that throws (a rule the command's own checks missed, for example a hostile peer's or a
	/// damaged log's command) rejects the command rather than end the game loop; the throw is the same on every peer, so they stay together.
	/// </summary>
	internal static ParkCommandResult Run( Level level, ParkSettingCommand command )
	{
		try
		{
			return command.Execute( level );
		}
		catch ( Exception error ) when ( error is InvalidOperationException or ArgumentException or KeyNotFoundException )
		{
			return ParkCommandResult.Rejected( $"The park refused the {command.Kind} command: {error.Message}" );
		}
	}

	/// <summary>The rejection of a setting in a park that runs no economy.</summary>
	private protected static ParkCommandResult NoEconomy => ParkCommandResult.Rejected( "The park runs no economy." );

	/// <summary>The rejection of a command that names no placed object of the kind it changes.</summary>
	private protected static ParkCommandResult NoAttraction( int attraction ) => ParkCommandResult.Rejected( $"No attraction {attraction} takes this setting." );

	/// <summary>The rejection of a command that names no employee.</summary>
	private protected static ParkCommandResult NoStaff( int staff ) => ParkCommandResult.Rejected( $"No employee {staff}." );

	/// <summary>Whether <paramref name="value"/> is text a command can carry: valid UTF-16 whose UTF-8 form fits the codec's string bound, so it decodes to the same string.</summary>
	internal static bool IsText( string? value )
	{
		if ( value == null )
			return false;
		try
		{
			return StrictUtf8.GetByteCount( value ) <= ParkCommandCodec.MaximumString;
		}
		catch ( EncoderFallbackException )
		{
			return false;
		}
	}

	/// <summary>The placed attraction <paramref name="attraction"/> (<see cref="Level.ResolveCommandObject"/>) and its economy object, when that is of <paramref name="kinds"/>.</summary>
	private protected static bool TryEconomyObject( Level level, int attraction, out ParkEconomy economy, out OriginalObject item, out ParkObjectState state, params ParkObjectKind[] kinds )
	{
		economy = null!;
		item = null!;
		state = null!;
		if ( level.Park?.Economy is not { } park || level.ResolveCommandObject( attraction ) is not { } found || level.GetEconomyInstance( found ) is not int instance
			|| !park.TryGetObject( instance, out var foundState ) || (kinds.Length > 0 && !kinds.Contains( foundState.Kind )) )
			return false;
		economy = park;
		item = found;
		state = foundState;
		return true;
	}

	/// <summary>
	/// Whether <paramref name="item"/> is a ride whose window shows it condemned (<c>0x4ad4e0</c>, state 4): the window hides its door, call and upgrade buttons. Only a ride
	/// with an economy record has a status, as the window reads it (<c>ParkHud.StatusFactsOf</c>).
	/// </summary>
	private protected static bool IsCondemnedRide( Level level, OriginalObject item ) =>
		item.Entry.WhichUIType == (int)BuildCategory.Rides && level.Park?.Economy is { } economy && level.GetEconomyInstance( item ) is int instance
		&& economy.TryGetObject( instance, out var state ) && ParkEconomy.ObjectState( state ) == RideObjectStates.Condemned;

	/// <summary>Reads the fields of the setting command of <paramref name="kind"/> (<see cref="ParkCommandCodec.Decode"/>), or null for a kind that is not a setting.</summary>
	internal static ParkSettingCommand? Read( ParkCommandKind kind, ParkCommandReader reader ) => kind switch
	{
		ParkCommandKind.SetParkOpen => new SetParkOpenCommand( reader.Bool() ),
		ParkCommandKind.SetEntranceFee => new SetEntranceFeeCommand( reader.Int() ),
		ParkCommandKind.SetAttractionOpen => new SetAttractionOpenCommand( reader.Int(), reader.Bool() ),
		ParkCommandKind.SetRideSettings => new SetRideSettingsCommand( reader.Int(), reader.Int(), reader.Int(), reader.Int() ),
		ParkCommandKind.SetPrice => new SetPriceCommand( reader.Int(), reader.Int() ),
		ParkCommandKind.SetGamePrize => new SetGamePrizeCommand( reader.Int(), reader.Int(), reader.Int(), reader.Int() ),
		ParkCommandKind.SetShopSettings => new SetShopSettingsCommand( reader.Int(), reader.Int(), reader.Int() ),
		ParkCommandKind.SetTrainingBudget => new SetTrainingBudgetCommand( (StaffType)reader.Int(), reader.Long() ),
		ParkCommandKind.SetResearchEffort => new SetResearchEffortCommand( (ResearchCategory)reader.Int(), reader.Int() ),
		ParkCommandKind.SetResearchWorkload => new SetResearchWorkloadCommand( reader.Int() ),
		ParkCommandKind.MoveResearch => new MoveResearchCommand( (ResearchCategory)reader.Int(), reader.Int() ),
		ParkCommandKind.TakeLoan => new TakeLoanCommand( reader.Int(), reader.Long() ),
		ParkCommandKind.RepayLoan => new RepayLoanCommand( reader.Int(), reader.Long() ),
		ParkCommandKind.BuyUpgrade => new BuyUpgradeCommand( reader.Int() ),
		ParkCommandKind.CancelUpgrade => new CancelUpgradeCommand( reader.Int() ),
		ParkCommandKind.RequestMaintenance => new RequestMaintenanceCommand( reader.Int(), reader.Bool() ),
		ParkCommandKind.AnswerChallenge => new AnswerChallengeCommand( reader.Int(), reader.Long(), reader.Bool() ),
		ParkCommandKind.RenameStaff => new RenameStaffCommand( reader.Int(), reader.String() ),
		ParkCommandKind.FireStaff => new FireStaffCommand( reader.Int() ),
		ParkCommandKind.SetPatrolArea => new SetPatrolAreaCommand( reader.Int(), reader.Int(), reader.Int(), reader.Int(), reader.Int(), reader.Bool() ),
		ParkCommandKind.RenamePark => new RenameParkCommand( reader.String(), reader.String() ),
		ParkCommandKind.SetGameSpeed => new SetGameSpeedCommand( (GameSpeed)reader.Int() ),
		ParkCommandKind.QueueCheat => new QueueCheatCommand( (CheatKind)reader.Int(), reader.Int() ),
		ParkCommandKind.RefreshLoanOffers => new RefreshLoanOffersCommand(),
		ParkCommandKind.SetAllObjectsOpen => new SetAllObjectsOpenCommand( reader.Bool() ),
		_ => null
	};
}

/// <summary>Opens or closes the park's door (the entry price window's door and the open and close keys). A bankrupt park stays closed: the economy refuses silently, as the door does.</summary>
public sealed record SetParkOpenCommand( bool Open ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetParkOpen;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Bool( Open );

	internal override ParkCommandResult Execute( Level level )
	{
		if ( level.Park?.Economy is not { } economy )
			return NoEconomy;
		if ( Open )
			economy.OpenPark();
		else
			economy.ClosePark();
		return ParkCommandResult.Ok( economy.IsParkOpen );
	}
}

/// <summary>The park's entrance fee (the entry price window).</summary>
public sealed record SetEntranceFeeCommand( int Fee ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetEntranceFee;
	internal override bool IsWellFormed => Fee >= 0;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Fee );

	internal override ParkCommandResult Execute( Level level )
	{
		if ( level.Park?.Economy is not { } economy )
			return NoEconomy;
		economy.SetEntranceFee( Fee );
		return ParkCommandResult.Ok();
	}
}

/// <summary>
/// Opens or closes the placed attraction <paramref name="Attraction"/> (its window's door button). Opening asks the park's reopening guard first
/// (<see cref="ParkGate.MayOpen"/>), as the open button does; a refused opening leaves the attraction closed.
/// </summary>
public sealed record SetAttractionOpenCommand( int Attraction, bool Open ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetAttractionOpen;
	internal override bool IsWellFormed => Attraction >= 0;

	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Attraction );
		writer.Bool( Open );
	}

	internal override ParkCommandResult Execute( Level level )
	{
		if ( level.ResolveCommandObject( Attraction ) is not { } item || !item.Runtime.IsAttraction )
			return NoAttraction( Attraction );
		// [BIN:TPW-EXE:0x4ad4e0 ride window refresh] a condemned ride's window hides its door, and the two track statuses grey it whatever the open word (ParkHud.ApplyObjectStatus)
		if ( IsCondemnedRide( level, item ) )
			return ParkCommandResult.Rejected( $"{item.Entry.DisplayName} is condemned." );
		if ( item.Entry.WhichUIType == (int)BuildCategory.Rides && level.Park?.Economy is { } economy && level.GetEconomyInstance( item ) is int instance
			&& economy.TryGetObject( instance, out _ ) && (item.Runtime.TrackIncomplete || item.Runtime.TrackInvalid) )
			return ParkCommandResult.Rejected( $"{item.Entry.DisplayName}'s track is not finished." );
		if ( Open == item.IsOpen )
			return ParkCommandResult.Ok();
		if ( !Open )
			item.Close();
		// [BIN:STP-PPC:0x1007159C open button] the open buttons call the reopening guard 0xde654 first and open only when it allows (ORACLE.md F27, ECON-049)
		else if ( level.Gate?.MayOpen( item.Runtime ) ?? true )
			item.Open();
		else
			return ParkCommandResult.Rejected( $"{item.Entry.DisplayName} cannot open now." );
		return ParkCommandResult.Ok();
	}
}

/// <summary>
/// The ride window's apply for the placed ride <paramref name="Attraction"/> (<see cref="OriginalObjectRuntime.ApplyRideWindow"/>): the speed, the capacity, and the duration when
/// its type has one. The capacity and duration setters clamp as the original's do; the speed is held to the speed slider's <c>UsageInfo.MinSpeed..MaxSpeed</c>, as the window's
/// load holds it, and must fit the script's signed 16-bit speed bias.
/// </summary>
public sealed record SetRideSettingsCommand( int Attraction, int Speed, int Capacity, int Duration ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetRideSettings;
	internal override bool IsWellFormed => Attraction >= 0 && Speed is >= short.MinValue and <= short.MaxValue && Capacity is >= 0 and <= ushort.MaxValue && Duration is >= 0 and <= byte.MaxValue;

	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Attraction );
		writer.Int( Speed );
		writer.Int( Capacity );
		writer.Int( Duration );
	}

	internal override ParkCommandResult Execute( Level level )
	{
		// the window applies to a placed ride only (ParkHud.CommitObjectSettings), not to the developer prototype
		if ( level.ResolveCommandObject( Attraction ) is not { } item || item is PrototypeRide || item.Entry.WhichUIType != (int)BuildCategory.Rides || item.Runtime.Script == null )
			return NoAttraction( Attraction );
		var speed = ParkHud.SliderPosition( Speed, item.Entry.Settings.GetInt( "UsageInfo.MinSpeed" ), item.Entry.Settings.GetInt( "UsageInfo.MaxSpeed" ) );
		item.Runtime.ApplyRideWindow( new RideSettingWords( speed, Capacity, Duration ) );
		return ParkCommandResult.Ok();
	}
}

/// <summary>The sale price of a shop or the game price of a sideshow (the window's price box and the price editor): 0 to the price box's 500.</summary>
public sealed record SetPriceCommand( int Attraction, int Price ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetPrice;
	internal override bool IsWellFormed => Attraction >= 0 && Price is >= 0 and <= ParkHud.MaximumPrice;

	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Attraction );
		writer.Int( Price );
	}

	internal override ParkCommandResult Execute( Level level )
	{
		if ( !TryEconomyObject( level, Attraction, out var economy, out _, out var state, ParkObjectKind.Shop, ParkObjectKind.Sideshow ) )
			return NoAttraction( Attraction );
		economy.SetPrices( state.Id, Price );
		return ParkCommandResult.Ok();
	}
}

/// <summary>A winnable sideshow's window apply: the game price (0 to 500), the prize's cost of goods (0 to 10,000) and the chance of losing (0 to 100 %).</summary>
public sealed record SetGamePrizeCommand( int Attraction, int Price, int CostOfGoods, int ChanceOfLosing ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetGamePrize;
	internal override bool IsWellFormed => Attraction >= 0 && Price is >= 0 and <= ParkHud.MaximumPrice && CostOfGoods is >= 0 and <= ParkHud.MaximumPrize && ChanceOfLosing is >= 0 and <= 100;

	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Attraction );
		writer.Int( Price );
		writer.Int( CostOfGoods );
		writer.Int( ChanceOfLosing );
	}

	internal override ParkCommandResult Execute( Level level )
	{
		// a non-winnable sideshow's window has only the price (UsageInfo.NonWinnableSideshow, ParkHud.ObjectSettings)
		if ( !TryEconomyObject( level, Attraction, out var economy, out var item, out var state, ParkObjectKind.Sideshow )
			|| item.Entry.Settings.GetInt( "UsageInfo.NonWinnableSideshow" ) != 0 )
			return NoAttraction( Attraction );
		economy.SetPrices( state.Id, Price, CostOfGoods, ChanceOfLosing );
		return ParkCommandResult.Ok();
	}
}

/// <summary>A shop's quality of goods (the quality slider's three steps: 0, 50 or 100) and the amount of its special ingredient (0 to 100): the shop window's apply.</summary>
public sealed record SetShopSettingsCommand( int Attraction, int QualityOfGoods, int IngredientAmount ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetShopSettings;
	internal override bool IsWellFormed => Attraction >= 0 && QualityOfGoods is 0 or 50 or 100 && IngredientAmount is >= 0 and <= 100;

	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Attraction );
		writer.Int( QualityOfGoods );
		writer.Int( IngredientAmount );
	}

	internal override ParkCommandResult Execute( Level level )
	{
		if ( !TryEconomyObject( level, Attraction, out var economy, out _, out var state, ParkObjectKind.Shop ) )
			return NoAttraction( Attraction );
		economy.SetShopSettings( state.Id, QualityOfGoods, IngredientAmount );
		return ParkCommandResult.Ok();
	}
}

/// <summary>
/// A staff role's monthly training budget, 0 to the training window's <see cref="ParkHud.TrainingBudgetMaximum"/>; the money is spent by the monthly training, not now. A park
/// without staff (Theme Park Inc's) takes none.
/// </summary>
public sealed record SetTrainingBudgetCommand( StaffType Role, long Budget ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetTrainingBudget;
	internal override bool IsWellFormed => Enum.IsDefined( Role ) && Budget is >= 0 and <= ParkHud.TrainingBudgetMaximum;

	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( (int)Role );
		writer.Long( Budget );
	}

	internal override ParkCommandResult Execute( Level level )
	{
		if ( level.Park?.Economy is not { } economy )
			return NoEconomy;
		if ( !economy.Rules.Staff )
			return ParkCommandResult.Rejected( "The park runs no staff." );
		economy.SetTrainingBudget( Role, Budget );
		return ParkCommandResult.Ok();
	}
}

/// <summary>The research lab's effort for one category (0 to 100), in a park whose mode has the lab.</summary>
public sealed record SetResearchEffortCommand( ResearchCategory Category, int Effort ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetResearchEffort;
	internal override bool IsWellFormed => Enum.IsDefined( Category ) && Effort is >= 0 and <= 100;

	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( (int)Category );
		writer.Int( Effort );
	}

	internal override ParkCommandResult Execute( Level level )
	{
		if ( level.Park?.Economy is not { Features.ResearchPanel: true } economy )
			return ParkCommandResult.Rejected( "The park has no research lab." );
		economy.SetResearchEffort( Category, Effort );
		return ParkCommandResult.Ok();
	}
}

/// <summary>The research lab's workload knob (0 to 100), in a park whose mode has the lab.</summary>
public sealed record SetResearchWorkloadCommand( int Workload ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetResearchWorkload;
	internal override bool IsWellFormed => Workload is >= 0 and <= 100;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Workload );

	internal override ParkCommandResult Execute( Level level )
	{
		if ( level.Park?.Economy is not { Features.ResearchPanel: true } economy )
			return ParkCommandResult.Rejected( "The park has no research lab." );
		economy.Research.SetWorkload( Workload );
		return ParkCommandResult.Ok();
	}
}

/// <summary>Steps a research category's current project one place back (-1) or on (1), with wraparound; rejected when there is no other project.</summary>
public sealed record MoveResearchCommand( ResearchCategory Category, int Direction ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.MoveResearch;
	internal override bool IsWellFormed => Enum.IsDefined( Category ) && Direction is -1 or 1;

	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( (int)Category );
		writer.Int( Direction );
	}

	internal override ParkCommandResult Execute( Level level )
	{
		if ( level.Park?.Economy is not { Features.ResearchPanel: true } economy )
			return ParkCommandResult.Rejected( "The park has no research lab." );
		return economy.Research.MoveCurrent( Category, Direction ) ? ParkCommandResult.Ok() : ParkCommandResult.Rejected( "There is no other project to research." );
	}
}

/// <summary>
/// Takes the loan of offer <paramref name="Offer"/> for the <paramref name="Amount"/> the loans window's confirmation named: the offer must still be on the Available tab
/// (not bought, and passed at the last list build) with that amount. The accepted result's value is the new <see cref="LoanAccount"/>.
/// </summary>
public sealed record TakeLoanCommand( int Offer, long Amount ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.TakeLoan;
	internal override bool IsWellFormed => Offer >= 0 && Amount >= 0;

	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Offer );
		writer.Long( Amount );
	}

	internal override ParkCommandResult Execute( Level level )
	{
		if ( level.Park?.Economy is not { } economy )
			return NoEconomy;
		if ( economy.IsBankrupt )
			return ParkCommandResult.Rejected( "A bankrupt park cannot borrow." );
		if ( economy.AvailableLoans.FirstOrDefault( offer => offer.Index == Offer ) is not { } available || available.Amount != Amount )
			return ParkCommandResult.Rejected( $"Loan offer {Offer} is not available." );
		return ParkCommandResult.Ok( economy.TakeLoan( Offer ) );
	}
}

/// <summary>
/// Pays off the outstanding loan of offer <paramref name="Offer"/> early, for the remaining balance (<paramref name="Amount"/>) the confirmation named; rejected without that
/// loan at that balance, or without the money (UITEXT 182).
/// </summary>
public sealed record RepayLoanCommand( int Offer, long Amount ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.RepayLoan;
	internal override bool IsWellFormed => Offer >= 0 && Amount >= 0;

	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Offer );
		writer.Long( Amount );
	}

	internal override ParkCommandResult Execute( Level level )
	{
		if ( level.Park?.Economy is not { } economy )
			return NoEconomy;
		if ( economy.Loans.FirstOrDefault( loan => loan.OfferIndex == Offer ) is not { } loan || loan.RemainingBalance != Amount )
			return ParkCommandResult.Rejected( $"Loan {Offer} is not outstanding at that balance." );
		return economy.TryRepayLoan( Offer ) ? ParkCommandResult.Ok() : ParkCommandResult.Rejected( "The park cannot afford to pay off the loan." );
	}
}

/// <summary>
/// The loans dialog builds its list (it opens, or a tab is chosen): every offer's credit test runs and is stored (<see cref="ParkEconomy.RefreshLoanOffers"/>), saved and hashed
/// park state, so it is a command like any edit.
/// </summary>
public sealed record RefreshLoanOffersCommand : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.RefreshLoanOffers;
	internal override void WriteFields( ParkCommandWriter writer ) { }

	internal override ParkCommandResult Execute( Level level )
	{
		// the loans dialog opens only in a mode with loans (ParkHud.OpenLoansWindow)
		if ( level.Park?.Economy is not { Features.Loans: true } economy )
			return ParkCommandResult.Rejected( "The park takes no loans." );
		economy.RefreshLoanOffers();
		return ParkCommandResult.Ok();
	}
}

/// <summary>Asks for the next upgrade level of the ride <paramref name="Attraction"/> (its upgrade list); the result's value is the economy's <see cref="ParkEconomy.PurchaseResult"/>.</summary>
public sealed record BuyUpgradeCommand( int Attraction ) : ParkSettingCommand
{
	/// <summary>
	/// Whether the upgrade list may act on <paramref name="attraction"/> (<c>ParkHud.IsUpgradeListTarget</c>): a placed ride (not the developer prototype) whose economy record is a
	/// ride of its own type and which is not condemned (its window then hides the upgrade button, <c>0x4ad4e0</c>).
	/// </summary>
	internal static bool IsUpgradeTarget( Level level, int attraction, out ParkEconomy economy, out ParkObjectState state ) =>
		TryEconomyObject( level, attraction, out economy, out var item, out state, ParkObjectKind.Ride ) && item is not PrototypeRide
		&& state.InfoId == item.Entry.InfoId && !IsCondemnedRide( level, item );

	public override ParkCommandKind Kind => ParkCommandKind.BuyUpgrade;
	internal override bool IsWellFormed => Attraction >= 0;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Attraction );

	internal override ParkCommandResult Execute( Level level )
	{
		if ( !IsUpgradeTarget( level, Attraction, out var economy, out var state ) )
			return NoAttraction( Attraction );
		var result = economy.TryBuyUpgrade( state.Id );
		return result == ParkEconomy.PurchaseResult.Ok ? ParkCommandResult.Ok( result ) : new ParkCommandResult( false, $"The upgrade is refused: {result}.", result );
	}
}

/// <summary>Cancels the upgrade the ride <paramref name="Attraction"/> waits for (its upgrade list's cancel row).</summary>
public sealed record CancelUpgradeCommand( int Attraction ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.CancelUpgrade;
	internal override bool IsWellFormed => Attraction >= 0;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Attraction );

	internal override ParkCommandResult Execute( Level level )
	{
		if ( !BuyUpgradeCommand.IsUpgradeTarget( level, Attraction, out var economy, out var state ) )
			return NoAttraction( Attraction );
		return economy.TryCancelUpgrade( state.Id ) ? ParkCommandResult.Ok() : ParkCommandResult.Rejected( "The ride waits for no upgrade." );
	}
}

/// <summary>
/// Calls a mechanic to the ride <paramref name="Attraction"/> (a handyman to a toilet), or cancels the call: its window's call button, whose rules it keeps. Only a placed ride or
/// toilet (not the developer prototype) takes a call, and a new call needs a hired employee of the role who is not on strike (UIHELPTEXT 574, 575).
/// </summary>
public sealed record RequestMaintenanceCommand( int Attraction, bool On ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.RequestMaintenance;
	internal override bool IsWellFormed => Attraction >= 0;

	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Attraction );
		writer.Bool( On );
	}

	internal override ParkCommandResult Execute( Level level )
	{
		if ( !TryEconomyObject( level, Attraction, out var economy, out var item, out var state ) || item is PrototypeRide
			|| !economy.Catalog.TryGet( state.InfoId, out var info ) || (state.Kind != ParkObjectKind.Ride && !info.ProvidesRelief) )
			return NoAttraction( Attraction );
		// a condemned ride's window hides its call button (0x4ad4e0)
		if ( IsCondemnedRide( level, item ) )
			return ParkCommandResult.Rejected( $"{item.Entry.DisplayName} is condemned." );
		var role = info.ProvidesRelief ? StaffType.Handyman : StaffType.Mechanic;
		if ( On && !state.MaintenanceRequested && !economy.Staff.OfType( role ).Any() )
			return ParkCommandResult.Rejected( $"The park has no {role} to call." );
		if ( On && !state.MaintenanceRequested && economy.Staff.Strikes.IsActive( role ) )
			return ParkCommandResult.Rejected( $"The {role}s are on strike." );
		return economy.RequestMaintenance( state.Id, On ) ? ParkCommandResult.Ok() : NoAttraction( Attraction );
	}
}

/// <summary>
/// Accepts or declines the challenge offered on day <paramref name="OfferedDay"/> from definition <paramref name="Definition"/>: the offer the message showed. Rejected while
/// that offer does not wait for an answer (the original's accept and decline then do nothing), so an answer that arrives late never answers a newer offer.
/// </summary>
public sealed record AnswerChallengeCommand( int Definition, long OfferedDay, bool Accept ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.AnswerChallenge;
	internal override bool IsWellFormed => Definition >= 0;

	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Definition );
		writer.Long( OfferedDay );
		writer.Bool( Accept );
	}

	internal override ParkCommandResult Execute( Level level )
	{
		if ( level.Park?.Economy is not { } economy )
			return NoEconomy;
		if ( economy.Objectives.Current is not { Accepted: false } offer || offer.DefinitionIndex != Definition || offer.OfferedDay != OfferedDay )
			return ParkCommandResult.Rejected( "That challenge is not on offer." );
		if ( Accept )
			economy.AcceptChallenge();
		else
			economy.DeclineChallenge();
		return ParkCommandResult.Ok();
	}
}

/// <summary>Renames the employee <paramref name="Staff"/> (the employee window's name field); the staff roster refuses a name it cannot hold.</summary>
public sealed record RenameStaffCommand( int Staff, string Name ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.RenameStaff;
	internal override bool IsWellFormed => Staff >= 0 && IsText( Name );

	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Staff );
		writer.String( Name );
	}

	internal override ParkCommandResult Execute( Level level )
	{
		if ( level.Park?.Economy is not { } economy )
			return NoEconomy;
		if ( economy.Staff.Members.FirstOrDefault( member => member.Id == Staff ) is not { } member )
			return NoStaff( Staff );
		return economy.Staff.Rename( member, Name ) ? ParkCommandResult.Ok() : ParkCommandResult.Rejected( "The name is not valid." );
	}
}

/// <summary>Dismisses the employee <paramref name="Staff"/> (the employee window's dismiss button).</summary>
public sealed record FireStaffCommand( int Staff ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.FireStaff;
	internal override bool IsWellFormed => Staff >= 0;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Staff );

	internal override ParkCommandResult Execute( Level level )
	{
		if ( level.Park?.Economy is not { } economy )
			return NoEconomy;
		if ( economy.Staff.Members.All( member => member.Id != Staff ) )
			return NoStaff( Staff );
		economy.Fire( Staff );
		return ParkCommandResult.Ok();
	}
}

/// <summary>
/// Sets the patrol rectangle of the employee <paramref name="Staff"/> between two corner cells as the patrol tool's clicks gave them (the tool's second click), or with
/// <paramref name="Clear"/> clears it (the employee window's clear button; the corners then are 0).
/// </summary>
public sealed record SetPatrolAreaCommand( int Staff, int X0, int Y0, int X1, int Y1, bool Clear = false ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetPatrolArea;
	internal override bool IsWellFormed => Staff >= 0 && IsCell( X0, Y0 ) && IsCell( X1, Y1 ) && (!Clear || (X0 | Y0 | X1 | Y1) == 0);

	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Staff );
		writer.Int( X0 );
		writer.Int( Y0 );
		writer.Int( X1 );
		writer.Int( Y1 );
		writer.Bool( Clear );
	}

	internal override ParkCommandResult Execute( Level level )
	{
		if ( level.Park?.Economy is not { } economy )
			return NoEconomy;
		if ( economy.Staff.Members.FirstOrDefault( member => member.Id == Staff ) is not { } member )
			return NoStaff( Staff );
		if ( Clear )
		{
			member.ClearPatrolArea();
			return ParkCommandResult.Ok();
		}
		if ( X0 >= ParkLitter.MapSize || Y0 >= ParkLitter.MapSize || X1 >= ParkLitter.MapSize || Y1 >= ParkLitter.MapSize )
			return ParkCommandResult.Rejected( "The patrol area lies outside the map." );
		member.SetPatrolArea( (X0, Y0), (X1, Y1) );
		return ParkCommandResult.Ok();
	}
}

/// <summary>Renames the park: the gate sign's two lines (<see cref="Level.RenamePark"/>, which refuses lines a save could not hold).</summary>
public sealed record RenameParkCommand( string Line1, string Line2 ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.RenamePark;
	internal override bool IsWellFormed => IsText( Line1 ) && IsText( Line2 );

	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.String( Line1 );
		writer.String( Line2 );
	}

	internal override ParkCommandResult Execute( Level level ) =>
		level.RenamePark( Line1, Line2 ) ? ParkCommandResult.Ok() : ParkCommandResult.Rejected( "The park name is not valid." );
}

/// <summary>The park's game speed (the speed buttons and the time keys): paused, normal, fast or fastest. A park without an economy keeps its speed in the HUD.</summary>
public sealed record SetGameSpeedCommand( GameSpeed Speed ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetGameSpeed;
	internal override bool IsWellFormed => Enum.IsDefined( Speed );
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( (int)Speed );

	internal override ParkCommandResult Execute( Level level )
	{
		if ( level.Park?.Economy is not { } economy )
			return NoEconomy;
		economy.Speed = Speed;
		return ParkCommandResult.Ok();
	}
}

/// <summary>
/// Queues a cheat for the next park turn (the secret park name's mode, the Alt keys and the cheats window, docs/CHEATS.md); rejected when the park cannot take it
/// (<see cref="ParkEconomy.QueueCheat"/>). The HUD asks for the cheat mode first, as before.
/// </summary>
public sealed record QueueCheatCommand( CheatKind Cheat, int Argument = 0 ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.QueueCheat;
	internal override bool IsWellFormed => Enum.IsDefined( Cheat );

	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( (int)Cheat );
		writer.Int( Argument );
	}

	internal override ParkCommandResult Execute( Level level )
	{
		if ( level.Park?.Economy is not { } economy )
			return NoEconomy;
		// the other cheats act once the cheat mode is on or waits for the next turn (the cheats window's rule; the Alt keys need it on)
		if ( Cheat != CheatKind.Mode && !economy.Cheats.Mode && economy.Cheats.Pending.All( action => action.Kind != CheatKind.Mode ) )
			return ParkCommandResult.Rejected( "The cheat mode is off." );
		return economy.QueueCheat( new CheatAction( Cheat, Argument ) ) ? ParkCommandResult.Ok() : ParkCommandResult.Rejected( $"The park cannot take the {Cheat} cheat now." );
	}
}

/// <summary>
/// The developer object panel's open-all (<paramref name="Open"/> true) and close-all: every placed object opens or closes, without the reopening guard, as the panel's buttons
/// did before the command layer.
/// </summary>
public sealed record SetAllObjectsOpenCommand( bool Open ) : ParkSettingCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetAllObjectsOpen;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Bool( Open );

	internal override ParkCommandResult Execute( Level level )
	{
		if ( level.Objects is not { } objects )
			return ParkCommandResult.Rejected( "The park has no objects." );
		foreach ( var item in objects.Objects.ToArray() )
		{
			if ( Open )
				item.Open();
			else
				item.Close();
		}
		return ParkCommandResult.Ok();
	}
}
