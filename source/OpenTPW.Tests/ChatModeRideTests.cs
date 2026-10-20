using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>Riding from a visit's chat mode (tp.exe 0x4BD7F4, 0x4E15B0): which object a click rides, its help, and where the rider comes out.</summary>
[TestClass, DoNotParallelize]
public class ChatModeRideTests
{
	private RideUpgradeIngressTests fixture = null!;

	[TestInitialize]
	public void Setup() { fixture = new(); fixture.Setup(); }
	[TestCleanup]
	public void Cleanup() => fixture.Cleanup();

	private OriginalObject Owner( BuildCategory category, string extra = "" )
	{
		fixture.Fixture( category, out _, out _, out var first, out _, out _, ParkGameMode.FullSimulation, extra );
		return first;
	}

	/// <summary>An object of <paramref name="category"/>'s interface type whose ride view always opens (the prototype ride's), with <paramref name="extra"/> settings.</summary>
	private OriginalObject Rideable( BuildCategory category = BuildCategory.Rides, string extra = "" )
	{
		var item = (PrototypeRide)RuntimeHelpers.GetUninitializedObject( typeof( PrototypeRide ) );
		typeof( OriginalObject ).GetField( "<Runtime>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( item, Owner( category, extra ).Runtime );
		return item;
	}

	[TestMethod]
	public void AClickRidesARideOrFeatureWhoseViewOpensButNoAddOnOrShop()
	{
		var ride = Rideable();
		Assert.AreSame( ride, ParkHud.ChatModeRide( ride ), "a ride (interface type 0) whose view opens" );
		var feature = Rideable( BuildCategory.Features );
		Assert.AreSame( feature, ParkHud.ChatModeRide( feature ), "a feature (interface type 3)" );
		Assert.IsNull( ParkHud.ChatModeRide( Rideable( extra: "AddOn.UpgradesId 1102\n" ) ), "a track add-on" );
		Assert.IsNull( ParkHud.ChatModeRide( Rideable( BuildCategory.Shops ) ), "a shop's interface type (1)" );
		Assert.IsNull( ParkHud.ChatModeRide( Rideable( BuildCategory.Sideshows ) ), "a sideshow's interface type (2)" );
	}

	[TestMethod]
	public void AClickRidesNothingWhoseRideViewCannotOpen()
	{
		Assert.IsNull( ParkHud.ChatModeRide( null ) );
		// the test objects have no camera seat: a click on them walks
		Assert.IsNull( ParkHud.ChatModeRide( Owner( BuildCategory.Rides ) ) );
		var deleted = Owner( BuildCategory.Rides );
		deleted.Delete();
		Assert.IsNull( ParkHud.ChatModeRide( deleted ) );
	}

	[TestMethod]
	public void AMissingInterfaceTypeIsNotARide()
	{
		// GetInt's own fallback is 0, which would be a ride; a missing key is -1 and is not the rides' or the features' type
		var ride = Rideable();
		var values = (Dictionary<string, string>)ride.Entry.Settings.Values;
		Assert.IsTrue( values.TryGetValue( "Info.WhichUIType", out var type ) );
		values.Remove( "Info.WhichUIType" );
		try
		{
			Assert.AreEqual( -1, ride.Entry.Settings.GetInt( "Info.WhichUIType", -1 ) );
			Assert.IsTrue( RideCameraMode.CanRide( ride ), "the prototype's view still opens" );
			Assert.IsNull( ParkHud.ChatModeRide( ride ), "a missing interface type is not ridden from the chat mode" );
		}
		finally
		{
			values["Info.WhichUIType"] = type;
		}
	}

	[TestMethod]
	public void TheRideHelpNamesTheRide()
	{
		var help = new[] { new StringTemplate( new StringTemplatePart[] { new( "", -1 ), new( "", 2 ), new( ": Left-click to ride", -1 ) } ) };
		var strings = new UiStringTable( "English", new string[1], new[] { "" }, new string[1], new string[1], helpTemplates: help );
		Assert.AreEqual( "Hades: Left-click to ride", strings.HelpFormat( 0, "Hades" ), "UIHELPTEXT 580 takes the ride's name" );
		Assert.AreEqual( "Hades", new UiStringTable( "English", new string[1], new[] { "" }, new string[1], new string[1] ).HelpFormat( 0, "Hades" ),
			"a table without the records' parameters appends the name" );
	}

	[TestMethod]
	public void ARiderComesOutAtTheRidesExit()
	{
		var ride = Owner( BuildCategory.Rides );
		ride.Visitors.HasCells = false;
		Assert.IsNull( ParkPresence.ExitOf( ride ), "a ride without visitor cells keeps the figure where it stood" );
		ride.Visitors.HasCells = true;
		ride.Visitors.ExitCell = (12, 7);
		Assert.AreEqual( (12.5f, 7.5f), ParkPresence.ExitOf( ride ), "the centre of the path cell in front of the exit" );
	}
}
