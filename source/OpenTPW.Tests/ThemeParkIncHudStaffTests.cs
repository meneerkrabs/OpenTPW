using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The experimental Theme Park Inc HUD's read-only Staff panel.</summary>
[TestClass]
public class ThemeParkIncHudStaffTests
{
	private static readonly string[] Types = { "Cleaner", "Engineer", "Entertainer", "Guard", "Scientist", "Gardener" };

	private static readonly string[] TypeTable = Types.SelectMany( type => new[] { type, type + "s" } ).ToArray();

	private static ThemeParkIncHireCandidate Candidate( int id, ThemeParkIncStaffRole role, int nameIndex, int wage, int experience )
		=> new( id, role, nameIndex, wage, 0, 0, experience, 0, 100 );

	private static UiStringTable Strings() => new( "English", new string[1], new[] { "" }, new string[1], new string[1],
		staffTypes: TypeTable, staffNames: Enumerable.Range( 0, 6 ).Select( role => Enumerable.Range( 0, 40 ).Select( index => $"{Types[role]}Name{index}" ).ToArray() ).ToArray() );

	private static ThemeParkIncHud BuildHud( UiStringTable strings, ThemeParkIncStaff staff )
	{
		var catalog = (ObjectCatalog)Activator.CreateInstance( typeof( ObjectCatalog ), BindingFlags.NonPublic | BindingFlags.Instance, null,
			new object[] { "science", Array.Empty<ObjectCatalogEntry>() }, null )!;
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		Set( level, "Objects", new ParkObjects( catalog, new SandboxParkGrid(), new WorldSeed( 1 ) ) );
		Set( level, "CellTool", new CellBuildTool() );
		Set( level, "LastActionMessage", "" );
		var economy = (ParkEconomy)RuntimeHelpers.GetUninitializedObject( typeof( ParkEconomy ) );
		Set( economy, "TpiStaff", staff );
		Set( economy, "TpiWageDemands", new List<ThemeParkIncStaff.WageDemand>() );
		var runtime = (ParkEconomyRuntime)RuntimeHelpers.GetUninitializedObject( typeof( ParkEconomyRuntime ) );
		Set( runtime, "Economy", economy );
		Set( level, "Park", runtime );
		return new ThemeParkIncHud( level, strings, new ThemeParkIncHudHost() );
	}

	private static void Set( object target, string property, object value ) =>
		target.GetType().GetField( "<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	private static ThemeParkIncStaff SyntheticStaff( params (int Slot, ThemeParkIncHireCandidate Candidate)[] placed )
	{
		var staff = (ThemeParkIncStaff)RuntimeHelpers.GetUninitializedObject( typeof( ThemeParkIncStaff ) );
		var slots = new ThemeParkIncHireCandidate?[ThemeParkIncStaff.PoolSlots];
		foreach ( var (slot, candidate) in placed )
			slots[slot] = candidate;
		typeof( ThemeParkIncStaff ).GetField( "slots", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( staff, slots );
		typeof( ThemeParkIncStaff ).GetField( "members", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( staff, new List<ThemeParkIncStaffMember>() );
		typeof( ThemeParkIncStaff ).GetField( "placing", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( staff, new bool[ThemeParkIncStaff.PoolSlots] );
		return staff;
	}

	private GameEdition previousEdition;

	[TestInitialize]
	public void Initialize() { previousEdition = GameEditions.Current; GameEditions.Select( GameEdition.ThemeParkInc ); }

	[TestCleanup]
	public void Cleanup() => GameEditions.Select( previousEdition );

	[TestMethod]
	public void PanelListsExactlyThePoolsCandidatesInSlotOrderWithRoleAndNameTexts()
	{
		var staff = SyntheticStaff( (5, Candidate( 1, ThemeParkIncStaffRole.Guard, 7, 1200, 30 )), (0, Candidate( 2, ThemeParkIncStaffRole.Gardener, 3, 900, 12 )),
			(31, Candidate( 3, ThemeParkIncStaffRole.Researcher, 0, 2500, 80 )) );
		var hud = BuildHud( Strings(), staff );
		var before = staff.Slots.ToArray();
		var rows = hud.StaffRows();
		Assert.AreEqual( 3, rows.Count );
		StringAssert.Contains( rows[0], "Gardener: GardenerName3" );
		StringAssert.Contains( rows[1], "Guard: GuardName7" );
		StringAssert.Contains( rows[2], "Scientist: ScientistName0" );
		StringAssert.Contains( rows[1], "30" );
		StringAssert.Contains( rows[1], 1200.ToString( "N0" ) );
		CollectionAssert.AreEqual( before, staff.Slots.ToArray(), "the panel does not change the pool" );
	}

	[TestMethod]
	public void StaffButtonTogglesThePanelWithoutPausingTheSimulation()
	{
		var hud = BuildHud( Strings(), SyntheticStaff( (0, Candidate( 1, ThemeParkIncStaffRole.Handyman, 1, 800, 5 )) ) );
		var panel = new[] { "staffPanelTitle", "staffPanelNote", "staffPanelList", "staffPanelClose" }.Select( id => hud.Screen.Find( id )! ).ToArray();
		Assert.IsTrue( panel.All( element => !element.Visible ) );
		var scale = hud.TimeScale;
		var button = (UiButton)hud.Screen.Find( "staff" )!;
		button.Clicked!();
		Assert.IsTrue( panel.All( element => element.Visible ) );
		Assert.AreEqual( scale, hud.TimeScale );
		Assert.AreEqual( GameSpeed.Normal, hud.Speed );
		Assert.AreEqual( "Choose a staff member to hire him, then click the park to place him. Guards do not chase troublemakers or give first aid yet.", ((UiLabel)panel[1]).Text() );
		button.Clicked!();
		Assert.IsTrue( panel.All( element => !element.Visible ) );
	}

	[TestMethod]
	public void EveryRolesRowIsOfferedToTheHireWithoutARoleRefusal()
	{
		// every role (the Cleaner, the Engineer, the Entertainer, the Guard, the Scientist and the Gardener) is placed in the park now: no row is refused for its role
		foreach ( var role in Enum.GetValues<ThemeParkIncStaffRole>() )
		{
			Assert.IsTrue( ThemeParkIncStaffMember.IsPlacedRole( role ), $"{role} is placed in the park" );
			var staff = SyntheticStaff( (0, Candidate( 1, role, 7, 1200, 30 )) );
			var hud = BuildHud( Strings(), staff );
			var before = staff.Slots.ToArray();
			hud.ActivateStaffRow( 0 );
			Assert.AreEqual( 0, hud.Messages.Count( message => message.Contains( "cannot be hired" ) ), "no row is refused for its role" );
			CollectionAssert.AreEqual( before, staff.Slots.ToArray(), "a candidate stays in the pool until a drop" );
		}
	}

	[TestMethod]
	public void ThePauseMenuGivesACandidateInTheHandBackToThePool()
	{
		// the menu's save must not find a hire half placed: opening the menu is a tool switch (0x6319E0(1), the placement tool's exit with placed 0)
		var candidate = Candidate( 2, ThemeParkIncStaffRole.Handyman, 3, 900, 12 );
		var staff = SyntheticStaff( (2, candidate) );
		var hud = BuildHud( Strings(), staff );
		var level = (Level)typeof( ThemeParkIncHud ).GetField( "level", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;
		((bool[])typeof( ThemeParkIncStaff ).GetField( "placing", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( staff )!)[2] = true;
		Set( level, "TpiPlacingSlot", 2 );
		typeof( ThemeParkIncHud ).GetMethod( "OpenPauseMenu", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( hud, null );
		Assert.IsTrue( hud.MenuOpen, "the menu opened" );
		Assert.AreEqual( -1, level.TpiPlacingSlot );
		Assert.IsFalse( staff.IsPlacing( 2 ), "the being-placed byte is cleared" );
		Assert.AreSame( candidate, staff.Slots[2], "the candidate stays in the pool" );
	}

	[TestMethod]
	public void StaffPanelIsHiddenWhileThePauseMenuIsOpen()
	{
		var hud = BuildHud( Strings(), SyntheticStaff( (0, Candidate( 1, ThemeParkIncStaffRole.Handyman, 1, 800, 5 )) ) );
		var panel = new[] { "staffPanelTitle", "staffPanelNote", "staffPanelList", "staffPanelClose" }.Select( id => hud.Screen.Find( id )! ).ToArray();
		((UiButton)hud.Screen.Find( "staff" )!).Clicked!();
		Assert.IsTrue( panel.All( element => element.Visible ) );
		typeof( ThemeParkIncHud ).GetMethod( "OpenPauseMenu", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( hud, null );
		Assert.IsTrue( hud.MenuOpen );
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		var context = new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 1024, 768 ) };
		hud.Update( context, new UiInput() );
		Assert.IsTrue( panel.All( element => !element.Visible ), "the staff panel hides under the pause menu" );
	}

	[TestMethod]
	public void EveryStaffTextHasElevenNonEmptyTranslations()
	{
		var keys = Enum.GetValues<OpenTpwText>().Where( key => key.ToString().StartsWith( "TpiStaff", StringComparison.Ordinal ) ).ToArray();
		Assert.AreEqual( 9, keys.Length );
		Assert.AreEqual( 11, SupplementaryStrings.SupportedLanguages.Count );
		foreach ( var key in keys )
			foreach ( var language in SupplementaryStrings.SupportedLanguages )
				Assert.IsFalse( string.IsNullOrWhiteSpace( SupplementaryStrings.Get( key, language ) ), $"{key} {language}" );
	}

	[TestMethod]
	public void RetailTablesGiveRoleAndNameTexts()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path!;
		var previous = FileSystem;
		try
		{
			FileSystem = new BaseFileSystem( data );
			var language = GameLanguage.Resolve( data, "English", null );
			var strings = UiStringTable.Load( language );
			for ( var role = 0; role < 6; role++ )
			{
				Assert.IsFalse( string.IsNullOrWhiteSpace( strings.StaffType( role ) ), $"role {role}" );
				Assert.IsFalse( string.IsNullOrWhiteSpace( strings.StaffName( role, 0 ) ), $"name 0 of role {role}" );
				Assert.IsFalse( string.IsNullOrWhiteSpace( strings.StaffName( role, 34 ) ), $"name 34 of role {role}" );
				Assert.AreEqual( "", strings.StaffName( role, 35 ), $"role {role} table has 35 entries" );
			}
			CollectionAssert.AreEqual( new[] { "Cleaner", "Engineer", "Entertainer", "Guard", "Scientist", "Gardener" }, Enumerable.Range( 0, 6 ).Select( role => strings.StaffType( role ) ).ToArray() );
		}
		finally { FileSystem = previous; }
	}
}
