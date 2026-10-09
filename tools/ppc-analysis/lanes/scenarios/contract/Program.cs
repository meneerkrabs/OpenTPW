using System;
using System.Collections.Generic;
using OpenTPW.Evidence.Scenarios;

internal static class Program
{
	private static int failures;
	private static int tests;

	// Synthetic thresholds; deliberately not the shipped Standard.sam values.
	private static readonly OriginalLocalTicketThresholds Local = new( 10, 20, 50, 30, 1000, 40, 3 );
	private static readonly OriginalGlobalTicketThresholds Global = new( 7, 8, 9, 11 );

	public static int Main()
	{
		Test( "keys are extra keys plus truncated earned / 3", () =>
		{
			var player = Player( 1, 0 );
			for ( var earned = 0; earned <= 12; earned++ )
			{
				SetEarned( player, earned );
				Equal( earned / 3, OriginalProgressionRules.Keys( player ) );
			}
			player.ExtraKeys = 1;
			SetEarned( player, 2 );
			Equal( 1, OriginalProgressionRules.Keys( player ) );
			SetEarned( player, 3 );
			Equal( 2, OriginalProgressionRules.Keys( player ) );
		} );
		Test( "global and secret tickets count once per player, locals per theme", () =>
		{
			var player = Player( 2, 0 );
			player.Global[0] = true;
			player.Secret[0] = true;
			player.Themes[0].Local[0] = true;
			player.Themes[1].Local[0] = true;
			Equal( 4, OriginalProgressionRules.EarnedTickets( player ) );
			player.Themes[1].SettingsLoaded = false;
			Equal( 3, OriginalProgressionRules.EarnedTickets( player ) );
		} );
		Test( "spending tickets never changes keys", () =>
		{
			var player = Player( 1, 0 );
			SetEarned( player, 6 );
			player.Themes[0].CostToEnter = 1;
			Equal( 2, OriginalProgressionRules.Keys( player ) );
			True( OriginalProgressionRules.TryUncoverMysteryItem( player, OriginalGameType.FullSimulation, 7, 4 ) );
			Equal( 2, OriginalProgressionRules.AvailableTickets( player ) );
			Equal( 2, OriginalProgressionRules.Keys( player ) );
		} );
		Test( "mystery item is uncovered once and refused when unaffordable", () =>
		{
			var player = Player( 1, 1 );
			SetEarned( player, 3 );
			False( OriginalProgressionRules.TryUncoverMysteryItem( player, OriginalGameType.FullSimulation, 9, 4 ) );
			True( OriginalProgressionRules.TryUncoverMysteryItem( player, OriginalGameType.FullSimulation, 9, 3 ) );
			Equal( 3, player.SpentTickets );
			False( OriginalProgressionRules.TryUncoverMysteryItem( player, OriginalGameType.FullSimulation, 9, 0 ) );
			Equal( 3, player.SpentTickets );
			True( OriginalProgressionRules.TryUncoverMysteryItem( player, OriginalGameType.Online, 10, 99 ) );
			False( player.UncoveredMysteryItems.Contains( 10 ) );
		} );
		Test( "placement charges tickets once, then the money price", () =>
		{
			Equal( OriginalPlacementCharge.MoneyPrice, OriginalProgressionRules.PlacementCharge( OriginalGameType.FullSimulation, 0, false ) );
			Equal( OriginalPlacementCharge.TicketPurchase, OriginalProgressionRules.PlacementCharge( OriginalGameType.FullSimulation, 2, false ) );
			Equal( OriginalPlacementCharge.MoneyPrice, OriginalProgressionRules.PlacementCharge( OriginalGameType.FullSimulation, 2, true ) );
			Equal( OriginalPlacementCharge.TicketPurchase, OriginalProgressionRules.PlacementCharge( OriginalGameType.InstantAction, 2, false ) );
			Equal( OriginalPlacementCharge.FreeOnline, OriginalProgressionRules.PlacementCharge( OriginalGameType.Online, 2, false ) );
		} );
		Test( "award code thresholds and theme cost equality", () =>
		{
			var player = Player( 2, 0 );
			player.Themes[0].CostToEnter = 1;
			player.Themes[1].CostToEnter = 3;
			SetEarned( player, 2 );
			Equal( OriginalTicketAward.Ticket, OriginalProgressionRules.AwardAfterEarning( player ) );
			SetEarned( player, 3 );
			Equal( OriginalTicketAward.TicketKeyAndNewTheme, OriginalProgressionRules.AwardAfterEarning( player ) );
			SetEarned( player, 6 );
			Equal( OriginalTicketAward.TicketAndKey, OriginalProgressionRules.AwardAfterEarning( player ) );
			player.ExtraKeys = 1;
			Equal( OriginalTicketAward.TicketKeyAndNewTheme, OriginalProgressionRules.AwardAfterEarning( player ) );
			player.ExtraKeys = -2;
			Equal( OriginalTicketAward.Ticket, OriginalProgressionRules.AwardAfterEarning( player ) );
		} );
		Test( "theme entry is signed, never consumes keys, and Instant Action skips it", () =>
		{
			True( OriginalProgressionRules.CanEnterTheme( OriginalGameType.FullSimulation, 1, 1 ) );
			False( OriginalProgressionRules.CanEnterTheme( OriginalGameType.FullSimulation, 2, 1 ) );
			True( OriginalProgressionRules.CanEnterTheme( OriginalGameType.FullSimulation, -1, 0 ) );
			True( OriginalProgressionRules.CanEnterTheme( OriginalGameType.InstantAction, 5, 0 ) );
		} );
		Test( "ticket checks: Full Simulation on unsigned multiples of 100", () =>
		{
			True( OriginalProgressionRules.TicketChecksRun( OriginalGameType.FullSimulation, 0 ) );
			True( OriginalProgressionRules.TicketChecksRun( OriginalGameType.FullSimulation, 100 ) );
			False( OriginalProgressionRules.TicketChecksRun( OriginalGameType.FullSimulation, 99 ) );
			False( OriginalProgressionRules.TicketChecksRun( OriginalGameType.FullSimulation, 101 ) );
			True( OriginalProgressionRules.TicketChecksRun( OriginalGameType.FullSimulation, 4294967200u ) );
			False( OriginalProgressionRules.TicketChecksRun( OriginalGameType.FullSimulation, uint.MaxValue ) );
			False( OriginalProgressionRules.TicketChecksRun( OriginalGameType.InstantAction, 100 ) );
			False( OriginalProgressionRules.TicketChecksRun( OriginalGameType.Online, 100 ) );
		} );
		Test( "local tickets are strictly greater", () =>
		{
			var at = new OriginalLocalTicketStats( 10, 20, 50f, false, 1000, 40, 3 );
			for ( var index = 0; index < 6; index++ )
				False( OriginalProgressionRules.LocalTicketWon( index, at, Local ) );
			var above = new OriginalLocalTicketStats( 11, 31, 50.5f, true, 1001, 41, 4 );
			for ( var index = 0; index < 6; index++ )
				True( OriginalProgressionRules.LocalTicketWon( index, above, Local ) );
		} );
		Test( "happiness ticket uses all in-park guests and a float mean", () =>
		{
			True( OriginalProgressionRules.LocalTicketWon( 2, new( 0, 31, 50.01f, false, 0, 0, 0 ), Local ) );
			False( OriginalProgressionRules.LocalTicketWon( 2, new( 0, 30, 99f, false, 0, 0, 0 ), Local ) );
			False( OriginalProgressionRules.LocalTicketWon( 2, new( 0, 99, float.NaN, false, 0, 0, 0 ), Local ) );
			False( OriginalProgressionRules.LocalTicketWon( 2, new( 0, 99, 0f, false, 0, 0, 0 ), Local ) ); // closed park mean
		} );
		Test( "recent visitors also need park age in months above the window", () =>
		{
			False( OriginalProgressionRules.LocalTicketWon( 5, new( 0, 0, 0, false, 0, 41, 3 ), Local ) );
			True( OriginalProgressionRules.LocalTicketWon( 5, new( 0, 0, 0, false, 0, 41, 4 ), Local ) );
			False( OriginalProgressionRules.LocalTicketWon( 4, new( 0, 0, 0, false, -1, 0, 0 ), Local ) );
		} );
		Test( "global tickets: strict, big park uses MinCellsCovered", () =>
		{
			var at = new OriginalGlobalTicketStats( 7, 8, 9, 11 );
			var above = new OriginalGlobalTicketStats( 8, 9, 10, 12 );
			for ( var index = 0; index < 4; index++ )
			{
				False( OriginalProgressionRules.GlobalTicketWon( index, at, Global ) );
				True( OriginalProgressionRules.GlobalTicketWon( index, above, Global ) );
			}
		} );
		Test( "secret tickets: camera exactly 100, land never awarded", () =>
		{
			True( OriginalProgressionRules.CameraSecretWon( 100 ) );
			False( OriginalProgressionRules.CameraSecretWon( 99 ) );
			False( OriginalProgressionRules.CameraSecretWon( 101 ) );
			False( OriginalProgressionRules.OwnAllLandSecretAwardable );
		} );
		Test( "cell classes for statistics, big park and camera coverage", () =>
		{
			var guests = new List<int>();
			var cameras = new List<int>();
			for ( var type = 0; type <= 31; type++ )
			{
				if ( OriginalProgressionRules.CountsForGuestStatistics( type ) )
					guests.Add( type );
				if ( !OriginalProgressionRules.RequiresCameraCoverage( type ) )
					cameras.Add( type );
			}
			Equal( "0,1,3,9,10", string.Join( ",", guests ) );
			Equal( "2,7,30", string.Join( ",", cameras ) );
			True( OriginalProgressionRules.CountsForBigPark( 21, false ) );
			True( OriginalProgressionRules.CountsForBigPark( 0, true ) );
			False( OriginalProgressionRules.CountsForBigPark( 1, false ) );
		} );
		Test( "all researched and built skips kind 4 and ticket items", () =>
		{
			var built = new[] { new OriginalCatalogItemState( 1, 0, 1 ), new OriginalCatalogItemState( 4, 0, 0 ), new OriginalCatalogItemState( 2, 3, 0 ) };
			True( OriginalProgressionRules.AllResearchedAndBuilt( 0, built ) );
			False( OriginalProgressionRules.AllResearchedAndBuilt( 1, built ) );
			False( OriginalProgressionRules.AllResearchedAndBuilt( 0, new[] { new OriginalCatalogItemState( 1, 0, 0 ) } ) );
		} );
		Test( "new-player key is awarded once; later events need front-end init", () =>
		{
			var player = Player( 1, 0 );
			var session = new OriginalFrontEndSession();
			session.Init( anySlotNamed: false );
			True( session.NewPlayerFlag );
			Equal( OriginalLobbyResponse.FirstEntryFullSimulation, session.CreatePlayer( player, OriginalGameType.FullSimulation ) );
			Equal( 1, player.ExtraKeys );
			False( session.PlayerWindowOpen );
			Throws<InvalidOperationException>( () => session.Continue( player, true, OriginalGameType.FullSimulation ) );
			session.Init( anySlotNamed: true );
			Equal( OriginalLobbyResponse.None, session.Continue( player, true, OriginalGameType.FullSimulation ) );
			Equal( 1, player.ExtraKeys );
		} );
		Test( "Instant Action first entry queues 394 without a key; quit awards nothing", () =>
		{
			var player = Player( 1, 0 );
			var session = new OriginalFrontEndSession();
			session.Init( anySlotNamed: false );
			Equal( OriginalLobbyResponse.FirstEntryInstantAction, session.CreatePlayer( player, OriginalGameType.InstantAction ) );
			Equal( 0, player.ExtraKeys );
			session.Init( anySlotNamed: false );
			session.Quit();
			False( session.PlayerWindowOpen );
			Equal( 0, player.ExtraKeys );
		} );
		Test( "game type follows the persisted easy flag except online", () =>
		{
			var player = Player( 0, 0 );
			Equal( OriginalGameType.FullSimulation, player.GameTypeOnLoad( OriginalGameType.InstantAction ) );
			player.EasyModeUser = true;
			Equal( OriginalGameType.InstantAction, player.GameTypeOnLoad( OriginalGameType.FullSimulation ) );
			Equal( OriginalGameType.Online, player.GameTypeOnLoad( OriginalGameType.Online ) );
		} );
		Test( "challenge activation boundaries", () =>
		{
			False( OriginalProgressionRules.ChallengesActive( OriginalGameType.FullSimulation, false, true, 539, 540 ) );
			True( OriginalProgressionRules.ChallengesActive( OriginalGameType.FullSimulation, false, true, 540, 540 ) );
			False( OriginalProgressionRules.ChallengesActive( OriginalGameType.FullSimulation, false, false, 999, 540 ) );
			True( OriginalProgressionRules.ChallengesActive( OriginalGameType.FullSimulation, true, false, 0, 540 ) );
			False( OriginalProgressionRules.ChallengesActive( OriginalGameType.InstantAction, true, true, 999, 540 ) );
		} );
		Test( "research groups open on ResearchTech[g+1], unsigned and capped at 7", () =>
		{
			var thresholds = new uint[] { 99, 0, 80, 85, 85, 0, 0, 0, 0, 0 };
			Equal( (byte)1, OriginalProgressionRules.OpenResearchGroups( 0, _ => (0, 10), thresholds ) );
			Equal( (byte)1, OriginalProgressionRules.OpenResearchGroups( 1, _ => (79, 100), thresholds ) );
			Equal( (byte)2, OriginalProgressionRules.OpenResearchGroups( 1, g => g == 1 ? (80u, 100u) : (0u, 100u), thresholds ) );
			Equal( (byte)7, OriginalProgressionRules.OpenResearchGroups( 4, _ => (1, 1), thresholds ) );
			Equal( (byte)5, OriginalProgressionRules.OpenResearchGroups( 5, _ => (0, 0), new uint[] { 0, 0, 0, 0, 0, 0, 1, 0, 0, 0 } ) );
			Equal( (byte)0, OriginalProgressionRules.OpenResearchGroups( 0, _ => (1, 1), new uint[] { 0, uint.MaxValue, 0, 0, 0, 0, 0, 0, 0, 0 } ) );
			Throws<ArgumentException>( () => OriginalProgressionRules.OpenResearchGroups( 0, _ => (0, 0), new uint[8] ) );
		} );
		Test( "feature availability by game type", () =>
		{
			Equal( OriginalAvailability.Unavailable, OriginalProgressionRules.Availability( OriginalGameType.InstantAction, OriginalFeature.BankPanel ) );
			Equal( OriginalAvailability.Unavailable, OriginalProgressionRules.Availability( OriginalGameType.InstantAction, OriginalFeature.LoanButtons ) );
			Equal( OriginalAvailability.Unavailable, OriginalProgressionRules.Availability( OriginalGameType.InstantAction, OriginalFeature.ResearchPanel ) );
			Equal( OriginalAvailability.Unavailable, OriginalProgressionRules.Availability( OriginalGameType.InstantAction, OriginalFeature.RideUpgradeList ) );
			Equal( OriginalAvailability.Unavailable, OriginalProgressionRules.Availability( OriginalGameType.InstantAction, OriginalFeature.GoldenTickets ) );
			Equal( OriginalAvailability.Available, OriginalProgressionRules.Availability( OriginalGameType.InstantAction, OriginalFeature.MysteryItemsCostTickets ) );
			Equal( OriginalAvailability.Available, OriginalProgressionRules.Availability( OriginalGameType.FullSimulation, OriginalFeature.LoanButtons ) );
			Equal( OriginalAvailability.NotTraced, OriginalProgressionRules.Availability( OriginalGameType.Online, OriginalFeature.ThemeKeyCheck ) );
			Equal( OriginalAvailability.Unavailable, OriginalProgressionRules.Availability( OriginalGameType.Online, OriginalFeature.Challenges ) );
		} );
		Test( "wage keeps low 32 bits; staff class bytes map to wage types", () =>
		{
			Equal( 40, OriginalProgressionRules.MonthlyWage( 10, 4 ) );
			Equal( unchecked((int)0x80000000), OriginalProgressionRules.MonthlyWage( 0x10000, 0x8000 ) );
			Equal( OriginalStaffType.Handyman, OriginalProgressionRules.StaffTypeForThingClass( 5 ) );
			Equal( OriginalStaffType.Mechanic, OriginalProgressionRules.StaffTypeForThingClass( 4 ) );
			Equal( OriginalStaffType.Researcher, OriginalProgressionRules.StaffTypeForThingClass( 8 ) );
			Equal( OriginalStaffType.Generic, OriginalProgressionRules.StaffTypeForThingClass( 1 ) );
		} );
		Test( "bankruptcy after six months in the red; world state 4 is not a game type", () =>
		{
			False( OriginalProgressionRules.BankruptcyEventDue( -1, 5 ) );
			True( OriginalProgressionRules.BankruptcyEventDue( -1, 6 ) );
			False( OriginalProgressionRules.BankruptcyEventDue( 0, 60 ) );
			Equal( 4, OriginalProgressionRules.AfterBankruptcyEvent( new( 2 ), true ).Raw );
			Equal( 2, OriginalProgressionRules.AfterBankruptcyEvent( new( 2 ), false ).Raw );
			Equal( 4, OriginalProgressionRules.AfterBankruptcyEvent( new( 4 ), true ).Raw );
		} );

		Console.WriteLine( $"{tests - failures} of {tests} cases passed" );
		return failures == 0 ? 0 : 1;
	}

	private static OriginalPlayerTickets Player( int themes, int extraKeys )
	{
		var player = new OriginalPlayerTickets { ExtraKeys = extraKeys };
		for ( var index = 0; index < themes; index++ )
			player.Themes.Add( new OriginalThemeTickets( $"theme{index}", 99 ) );
		return player;
	}

	/// <summary>Sets locals of the first theme (or globals when no theme exists) to reach the earned count.</summary>
	private static void SetEarned( OriginalPlayerTickets player, int earned )
	{
		Array.Clear( player.Global );
		Array.Clear( player.Secret );
		foreach ( var theme in player.Themes )
			Array.Clear( theme.Local );
		var flags = new List<bool[]> { player.Global, player.Secret };
		foreach ( var theme in player.Themes )
			flags.Add( theme.Local );
		foreach ( var set in flags )
		{
			for ( var index = 0; index < set.Length && earned > 0; index++, earned-- )
				set[index] = true;
		}
		if ( earned > 0 )
			throw new ArgumentOutOfRangeException( nameof( earned ) );
	}

	private static void Test( string name, Action body )
	{
		tests++;
		try
		{
			body();
		}
		catch ( Exception error )
		{
			failures++;
			Console.WriteLine( $"FAIL {name}: {error.Message}" );
		}
	}

	private static void Equal<T>( T expected, T actual )
	{
		if ( !EqualityComparer<T>.Default.Equals( expected, actual ) )
			throw new Exception( $"expected {expected}, got {actual}" );
	}

	private static void True( bool value ) => Equal( true, value );

	private static void False( bool value ) => Equal( false, value );

	private static void Throws<TException>( Action body ) where TException : Exception
	{
		try
		{
			body();
		}
		catch ( TException )
		{
			return;
		}
		throw new Exception( $"expected {typeof( TException ).Name}" );
	}
}
