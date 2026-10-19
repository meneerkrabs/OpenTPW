namespace OpenTPW;

// [EXT:online-coop-commands] The park's settings as commands (docs/COOP.md, "The command layer"): the door, prices, loans, staff, research, challenges, maintenance,
// upgrades, names, cheats, the rides' open state and settings and the game speed. Objects are named by their economy instance id (the economy's own saved key) or their
// attraction id; staff by their id, applicants by their candidate id, loans by their offer index.

/// <summary>Opens (<paramref name="Open"/>) or closes the park's door: the door button and its Ctrl-O/Ctrl-C keys.</summary>
public sealed record SetParkOpenCommand( bool Open ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetParkOpen;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Bool( Open );
}

/// <summary>The park's admission price.</summary>
public sealed record SetEntranceFeeCommand( int Fee ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetEntranceFee;
	internal override bool IsWellFormed => Fee >= 0;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Fee );
}

/// <summary>A shop's sale price or a sideshow's game price, with a sideshow's prize cost and chance of losing when given (null leaves them).</summary>
public sealed record SetPricesCommand( int Instance, int Price, int? CostOfGoods = null, int? ChanceOfLosing = null ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetPrices;
	internal override bool IsWellFormed => Instance >= 0 && Price >= 0;
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Instance );
		writer.Int( Price );
		writer.OptionalInt( CostOfGoods );
		writer.OptionalInt( ChanceOfLosing );
	}
}

/// <summary>A shop's quality of goods and amount of its special ingredient (null leaves a value).</summary>
public sealed record SetShopSettingsCommand( int Instance, int? Quality = null, int? Ingredient = null ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetShopSettings;
	internal override bool IsWellFormed => Instance >= 0;
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Instance );
		writer.OptionalInt( Quality );
		writer.OptionalInt( Ingredient );
	}
}

/// <summary>Takes the loan offer <paramref name="Offer"/>.</summary>
public sealed record TakeLoanCommand( int Offer ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.TakeLoan;
	internal override bool IsWellFormed => Offer >= 0;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Offer );
}

/// <summary>Pays the loan of offer <paramref name="Offer"/> off.</summary>
public sealed record RepayLoanCommand( int Offer ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.RepayLoan;
	internal override bool IsWellFormed => Offer >= 0;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Offer );
}

/// <summary>Hires the applicant <paramref name="Candidate"/> at once (no placement: a park without the staff tool).</summary>
public sealed record HireStaffCommand( int Candidate ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.HireStaff;
	internal override bool IsWellFormed => Candidate >= 0;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Candidate );
}

/// <summary>Dismisses the employee <paramref name="Staff"/>.</summary>
public sealed record FireStaffCommand( int Staff ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.FireStaff;
	internal override bool IsWellFormed => Staff >= 0;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Staff );
}

/// <summary>A staff role's monthly training budget.</summary>
public sealed record SetTrainingBudgetCommand( StaffType Role, long Budget ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetTrainingBudget;
	internal override bool IsWellFormed => Enum.IsDefined( Role ) && Budget >= 0;
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Byte( (byte)Role );
		writer.Long( Budget );
	}
}

/// <summary>A research category's effort (clamped to 0..100 by the lab).</summary>
public sealed record SetResearchEffortCommand( ResearchCategory Category, int Effort ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetResearchEffort;
	internal override bool IsWellFormed => Enum.IsDefined( Category );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Byte( (byte)Category );
		writer.Int( Effort );
	}
}

/// <summary>Steps a research category's current project by <paramref name="Direction"/> (with wraparound).</summary>
public sealed record MoveResearchCommand( ResearchCategory Category, int Direction ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.MoveResearch;
	internal override bool IsWellFormed => Enum.IsDefined( Category );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Byte( (byte)Category );
		writer.Int( Direction );
	}
}

/// <summary>The research lab's workload (clamped to 0..100).</summary>
public sealed record SetResearchWorkloadCommand( int Workload ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetResearchWorkload;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Workload );
}

/// <summary>Accepts (<paramref name="Accept"/>) or declines the waiting challenge.</summary>
public sealed record AnswerChallengeCommand( bool Accept ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.AnswerChallenge;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Bool( Accept );
}

/// <summary>Calls (<paramref name="On"/>) or cancels maintenance of the economy object <paramref name="Instance"/>.</summary>
public sealed record RequestMaintenanceCommand( int Instance, bool On ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.RequestMaintenance;
	internal override bool IsWellFormed => Instance >= 0;
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Instance );
		writer.Bool( On );
	}
}

/// <summary>Buys the next upgrade level of the ride whose economy instance is <paramref name="Instance"/>.</summary>
public sealed record BuyUpgradeCommand( int Instance ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.BuyUpgrade;
	internal override bool IsWellFormed => Instance >= 0;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Instance );
}

/// <summary>Cancels the upgrade of the ride whose economy instance is <paramref name="Instance"/>.</summary>
public sealed record CancelUpgradeCommand( int Instance ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.CancelUpgrade;
	internal override bool IsWellFormed => Instance >= 0;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Instance );
}

/// <summary>Renames the park: the gate sign's two lines.</summary>
public sealed record RenameParkCommand( string Line1, string Line2 ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.RenamePark;
	internal override bool IsWellFormed => IsText( Line1 ) && IsText( Line2 );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.String( Line1 );
		writer.String( Line2 );
	}
}

/// <summary>Renames the employee <paramref name="Staff"/>.</summary>
public sealed record RenameStaffCommand( int Staff, string Name ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.RenameStaff;
	internal override bool IsWellFormed => Staff >= 0 && IsText( Name );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Staff );
		writer.String( Name );
	}
}

/// <summary>Asks for the cheat <paramref name="Cheat"/> with its argument at the next park turn ([EXT:cheats-turn-boundary]); a session refuses cheats (decision Q17).</summary>
public sealed record QueueCheatCommand( CheatKind Cheat, int Argument = 0 ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.QueueCheat;
	internal override bool IsWellFormed => Enum.IsDefined( Cheat );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Byte( (byte)Cheat );
		writer.Int( Argument );
	}
}

/// <summary>Opens (<paramref name="Open"/>) or closes the attraction <paramref name="Attraction"/>: the object window's open button (opening only where the park's reopening guard allows).</summary>
public sealed record SetRideOpenCommand( int Attraction, bool Open ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetRideOpen;
	internal override bool IsWellFormed => Attraction >= 0;
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Attraction );
		writer.Bool( Open );
	}
}

/// <summary>The ride window's apply for the attraction <paramref name="Attraction"/>: its speed, capacity and duration words.</summary>
public sealed record SetRideSettingsCommand( int Attraction, int Speed, int Capacity, int Duration ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetRideSettings;
	internal override bool IsWellFormed => Attraction >= 0;
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Attraction );
		writer.Int( Speed );
		writer.Int( Capacity );
		writer.Int( Duration );
	}
}

/// <summary>The game speed (<see cref="GameSpeed.Paused"/> pauses the park); a session's host alone may set it (decision Q8).</summary>
public sealed record SetSpeedCommand( GameSpeed Speed ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetSpeed;
	internal override bool IsWellFormed => Enum.IsDefined( Speed );
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Byte( (byte)Speed );
}

/// <summary>
/// [EXT:online-coop-commands] Runs the economy's commands on a <see cref="ParkEconomy"/>: the park's executor hands them on (<see cref="Level"/>), and a HUD shown over an economy
/// without its level (a test's, or a level whose park is not the HUD's) has a funnel of its own over that economy (<see cref="ForEconomy"/>). Every command checks what the economy
/// would throw for before it calls it, so a refusal changes nothing.
/// </summary>
public static class ParkEconomyCommands
{
	/// <summary>A funnel over <paramref name="economy"/> alone: its commands apply at once (before the economy's next tick) and are logged there; nothing replays them.</summary>
	public static ParkCommands ForEconomy( ParkEconomy economy )
	{
		ArgumentNullException.ThrowIfNull( economy );
		return new ParkCommands( () => economy.Tick, command => Execute( economy, command ) ?? ParkCommandResult.Rejected( $"An economy alone takes no {command.Kind} command." ), () => false );
	}

	/// <summary>Runs <paramref name="command"/> on <paramref name="economy"/>; null for a command that is not the economy's own.</summary>
	public static ParkCommandResult? Execute( ParkEconomy economy, ParkCommand command )
	{
		ArgumentNullException.ThrowIfNull( economy );
		switch ( command )
		{
			case SetParkOpenCommand door:
				if ( door.Open )
				{
					if ( economy.IsBankrupt )
						return ParkCommandResult.Rejected( "A bankrupt park cannot open." );
					economy.OpenPark();
				}
				else
					economy.ClosePark();
				return ParkCommandResult.Ok( economy.IsParkOpen );
			case SetEntranceFeeCommand fee:
				economy.SetEntranceFee( fee.Fee );
				return ParkCommandResult.Ok();
			case SetPricesCommand prices:
				if ( !economy.TryGetObject( prices.Instance, out var priced ) || priced.Kind is not (ParkObjectKind.Shop or ParkObjectKind.Sideshow) )
					return ParkCommandResult.Rejected( $"No shop or sideshow {prices.Instance}." );
				economy.SetPrices( prices.Instance, prices.Price, prices.CostOfGoods, prices.ChanceOfLosing );
				return ParkCommandResult.Ok();
			case SetShopSettingsCommand shop:
				if ( !economy.TryGetObject( shop.Instance, out var store ) || store.Kind != ParkObjectKind.Shop )
					return ParkCommandResult.Rejected( $"No shop {shop.Instance}." );
				economy.SetShopSettings( shop.Instance, shop.Quality, shop.Ingredient );
				return ParkCommandResult.Ok();
			case TakeLoanCommand loan:
				if ( economy.IsBankrupt || !economy.Features.Loans || economy.AvailableLoans.All( offer => offer.Index != loan.Offer ) )
					return ParkCommandResult.Rejected( $"Loan offer {loan.Offer} is not available." );
				return ParkCommandResult.Ok( economy.TakeLoan( loan.Offer ) );
			case RepayLoanCommand repay:
				if ( economy.Loans.All( account => account.OfferIndex != repay.Offer ) )
					return ParkCommandResult.Rejected( $"Loan {repay.Offer} is not outstanding." );
				return economy.TryRepayLoan( repay.Offer ) ? ParkCommandResult.Ok() : ParkCommandResult.Rejected( "Cannot afford to pay the loan off." );
			case HireStaffCommand hire:
				if ( !economy.Rules.Staff || economy.IsBankrupt || economy.Staff.Candidates.FirstOrDefault( item => item.Id == hire.Candidate ) is not { } candidate || !economy.Staff.CanHire( candidate.Type ) )
					return ParkCommandResult.Rejected( $"Applicant {hire.Candidate} cannot be hired." );
				return ParkCommandResult.Ok( economy.Hire( hire.Candidate ) );
			case FireStaffCommand fire:
				if ( !economy.Rules.Staff || economy.Staff.Members.All( member => member.Id != fire.Staff ) )
					return ParkCommandResult.Rejected( $"No employee {fire.Staff}." );
				economy.Fire( fire.Staff );
				return ParkCommandResult.Ok();
			case SetTrainingBudgetCommand training:
				if ( !economy.Rules.Staff )
					return ParkCommandResult.Rejected( "This park runs no staff." );
				economy.SetTrainingBudget( training.Role, training.Budget );
				return ParkCommandResult.Ok();
			case SetResearchEffortCommand effort:
				if ( !economy.Rules.Research || !economy.Features.ResearchPanel )
					return ParkCommandResult.Rejected( "The research lab cannot be changed." );
				economy.SetResearchEffort( effort.Category, effort.Effort );
				return ParkCommandResult.Ok();
			case MoveResearchCommand move:
				if ( !economy.Features.ResearchPanel )
					return ParkCommandResult.Rejected( "The research lab cannot be changed." );
				return economy.Research.MoveCurrent( move.Category, move.Direction ) ? ParkCommandResult.Ok( true ) : ParkCommandResult.Rejected( "No other project to move to." );
			case SetResearchWorkloadCommand workload:
				if ( !economy.Features.ResearchPanel )
					return ParkCommandResult.Rejected( "The research lab cannot be changed." );
				economy.Research.SetWorkload( workload.Workload );
				return ParkCommandResult.Ok();
			case AnswerChallengeCommand answer:
				if ( !economy.Rules.Challenges || economy.Objectives.Current is not { Accepted: false } )
					return ParkCommandResult.Rejected( "No challenge waits for an answer." );
				if ( answer.Accept )
					economy.AcceptChallenge();
				else
					economy.DeclineChallenge();
				return ParkCommandResult.Ok();
			case RequestMaintenanceCommand maintenance:
				return economy.RequestMaintenance( maintenance.Instance, maintenance.On ) ? ParkCommandResult.Ok() : ParkCommandResult.Rejected( $"No object {maintenance.Instance}." );
			case BuyUpgradeCommand upgrade:
				if ( !economy.TryGetObject( upgrade.Instance, out _ ) )
					return ParkCommandResult.Rejected( $"No object {upgrade.Instance}." );
				return economy.TryBuyUpgrade( upgrade.Instance ) is var bought && bought == ParkEconomy.PurchaseResult.Ok
					? ParkCommandResult.Ok( bought ) : new ParkCommandResult( false, $"Cannot upgrade: {bought}.", bought );
			case CancelUpgradeCommand cancel:
				return economy.TryCancelUpgrade( cancel.Instance ) ? ParkCommandResult.Ok() : ParkCommandResult.Rejected( "No upgrade to cancel." );
			case RenameStaffCommand rename:
				return economy.Staff.Members.FirstOrDefault( member => member.Id == rename.Staff ) is { } named && economy.Staff.Rename( named, rename.Name )
					? ParkCommandResult.Ok() : ParkCommandResult.Rejected( "The name cannot be given." );
			case QueueCheatCommand cheat:
				return economy.QueueCheat( new CheatAction( cheat.Cheat, cheat.Argument ) ) ? ParkCommandResult.Ok() : ParkCommandResult.Rejected( "The park takes no such cheat now." );
			case SetSpeedCommand speed:
				economy.Speed = speed.Speed;
				return ParkCommandResult.Ok();
			default:
				return null;
		}
	}
}
