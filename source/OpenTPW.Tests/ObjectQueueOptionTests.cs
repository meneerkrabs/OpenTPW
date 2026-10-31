using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>RIDES-018: the build tool's queue option (0x81B0CC) holds only while the upgrade selection (0x8187F8) is clear (tp.exe 0x52FAB0, 0x46D605).</summary>
[TestClass]
public class ObjectQueueOptionTests
{
	private static ObjectCatalogEntry Entry( string sam ) =>
		new( "jungle", ObjectCategory.Ride, "/queue-option", new ObjectSettings( new[] { new ObjectSettingsFile( sam, "queue-option.sam" ) } ), new ObjectShape( new[] { "*" } ), "/model.MD2", null, "/script.RSE",
			Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( System.IO.Path.GetTempPath() ), null );

	[TestMethod]
	public void AnUpgradeOfKindOneOfATrackRideDropsTheQueueOptionAndAnyOtherTypeKeepsIt()
	{
		var plain = Entry( "Info.HasQueue 1\nBumper.WhichTrackType 1" );
		Assert.AreEqual( 0, plain.UpgradeSelection, "+0x288 is not 1, so the selection stays 0 whatever +0x9C says" );
		Assert.IsTrue( plain.QueueOptionOnBuild );
		var upgrade = Entry( "Info.HasQueue 1\nAddOn.UpgradeType 1\nBumper.WhichTrackType 2" );
		Assert.AreEqual( 2, upgrade.UpgradeSelection );
		Assert.IsFalse( upgrade.QueueOptionOnBuild, "the selection is set, so 0x52FAB0 answers 0" );
		var other = Entry( "Info.HasQueue 1\nAddOn.UpgradeType 2\nBumper.WhichTrackType 2" );
		Assert.AreEqual( 0, other.UpgradeSelection, "kind 2 hands 0" );
		Assert.IsTrue( other.QueueOptionOnBuild );
		var noQueue = Entry( "Info.HasQueue 0" );
		Assert.IsFalse( noQueue.QueueOptionOnBuild );
		var kindOneWithoutTrack = Entry( "Info.HasQueue 1\nAddOn.UpgradeType 1" );
		Assert.AreEqual( 0, kindOneWithoutTrack.UpgradeSelection );
		Assert.IsTrue( kindOneWithoutTrack.QueueOptionOnBuild, "a selection of 0 is clear" );
	}
}
