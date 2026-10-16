using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// ADVISOR-009: the ride, shop and sideshow windows post their help as they open and withdraw it as they close (tp.exe 0x4AF980, 0x4B0E30,
/// 0x4B2430): a ride 297 when the truncated state of repair is 1 to 20 and 207 otherwise, a shop 209, a sideshow 208. GPU-free.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AdvisorObjectHelpTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	[TestMethod]
	public void RepairOneToTwentyIsLow()
	{
		Assert.IsFalse( ObjectInfoHelp.IsLowRepair( null ) );
		Assert.IsFalse( ObjectInfoHelp.IsLowRepair( 0 ) );
		Assert.IsFalse( ObjectInfoHelp.IsLowRepair( 0.99 ), "truncated to 0" );
		Assert.IsTrue( ObjectInfoHelp.IsLowRepair( 1 ) );
		Assert.IsTrue( ObjectInfoHelp.IsLowRepair( 1.5 ) );
		Assert.IsTrue( ObjectInfoHelp.IsLowRepair( 20 ) );
		Assert.IsTrue( ObjectInfoHelp.IsLowRepair( 20.99 ), "truncated to 20" );
		Assert.IsFalse( ObjectInfoHelp.IsLowRepair( 21 ) );
		Assert.IsFalse( ObjectInfoHelp.IsLowRepair( 100 ) );
		Assert.IsFalse( ObjectInfoHelp.IsLowRepair( double.NaN ) );
		Assert.IsTrue( ObjectInfoHelp.IsLowRepair( 257 ), "the original tests the low byte (cmp al, 0x14)" );
	}

	[TestMethod]
	public void EachWindowKindPostsItsMessage()
	{
		Assert.AreEqual( 207, AdvisorUiHelp.MessageFor( ObjectInfoHelp.WindowName( ObjectInfoKind.Ride, 80, 1 ) ) );
		Assert.AreEqual( 297, AdvisorUiHelp.MessageFor( ObjectInfoHelp.WindowName( ObjectInfoKind.Ride, 10, 2 ) ) );
		Assert.AreEqual( 207, AdvisorUiHelp.MessageFor( ObjectInfoHelp.WindowName( ObjectInfoKind.Ride, null, 3 ) ) );
		Assert.AreEqual( 209, AdvisorUiHelp.MessageFor( ObjectInfoHelp.WindowName( ObjectInfoKind.Shop, 10, 4 ) ), "the repair matters for a ride only" );
		Assert.AreEqual( 208, AdvisorUiHelp.MessageFor( ObjectInfoHelp.WindowName( ObjectInfoKind.Sideshow, 10, 5 ) ) );
	}

	[TestMethod]
	public void ARideWindowWithdrawsBothOfItsMessages()
	{
		CollectionAssert.AreEquivalent( new[] { 297, 207 }, AdvisorUiHelp.WithdrawnWith( ObjectInfoHelp.WindowName( ObjectInfoKind.Ride, 10, 1 ) ).ToArray() );
		CollectionAssert.AreEquivalent( new[] { 297, 207 }, AdvisorUiHelp.WithdrawnWith( ObjectInfoHelp.WindowName( ObjectInfoKind.Ride, 90, 1 ) ).ToArray() );
		CollectionAssert.AreEqual( new[] { 209 }, AdvisorUiHelp.WithdrawnWith( ObjectInfoHelp.WindowName( ObjectInfoKind.Shop, 90, 1 ) ).ToArray() );
		CollectionAssert.AreEqual( new[] { 208 }, AdvisorUiHelp.WithdrawnWith( ObjectInfoHelp.WindowName( ObjectInfoKind.Sideshow, 90, 1 ) ).ToArray() );
	}

	[TestMethod]
	public void EveryObjectShownIsANewWindow()
	{
		Assert.AreNotEqual( ObjectInfoHelp.WindowName( ObjectInfoKind.Ride, 50, 1 ), ObjectInfoHelp.WindowName( ObjectInfoKind.Ride, 50, 2 ) );
	}

	[TestMethod]
	public void TheOtherWindowsKeepTheirMessages()
	{
		Assert.AreEqual( 188, AdvisorUiHelp.MessageFor( "buy" ) );
		CollectionAssert.AreEqual( new[] { 188 }, AdvisorUiHelp.WithdrawnWith( "buy" ).ToArray() );
		Assert.AreEqual( 200, AdvisorUiHelp.MessageFor( "research" ), "the research window posts 0xC8 (0x4AAE9D)" );
		Assert.IsTrue( AdvisorUiHelp.RepeatsTutorialOnHelpKey( "research" ), "its procedure passes the keys on (0x4A9B3A)" );
		Assert.IsNull( AdvisorUiHelp.MessageFor( "options" ) );
		Assert.AreEqual( 0, AdvisorUiHelp.WithdrawnWith( "options" ).Count );
		Assert.IsNull( AdvisorUiHelp.MessageFor( "objectInfo/Mystery/1" ) );
		Assert.IsNull( AdvisorUiHelp.MessageFor( "objectInfo/" ) );
		Assert.IsTrue( new[] { 188, 189, 207, 208, 209, 297 }.All( AdvisorUiHelp.AllWindowMessages.Contains ) );
	}

	[TestMethod]
	public void TheObjectWindowMessagesAreShippedHelpMessages()
	{
		var rows = AdvisorHelpTable.Parse( File.ReadAllText( Shipped( AdvisorHelpTable.RelativePath ) ) );
		foreach ( var message in ObjectInfoHelp.Messages )
			Assert.IsTrue( rows.Any( row => row.MessageId == message ), $"message {message} is a help message of the table" );
	}

	private static string Shipped( string relative )
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
		{
			var path = Path.Combine( directory.FullName, relative );
			if ( File.Exists( path ) )
				return path;
		}
		Assert.Fail( $"{relative} not found above the test directory." );
		return "";
	}
}
