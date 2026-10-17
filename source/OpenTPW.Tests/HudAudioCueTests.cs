using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>The cat_ui sounds of the message tube (tp.exe 0x48eb20, 0x48e000) and the postcard composer (0x4a9380).</summary>
[TestClass]
[DoNotParallelize]
public class HudAudioCueTests
{
	private static ParkHud Hud() => (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) );

	[TestMethod]
	public void ANewTubeIconPlaysTheMessageSoundAndAClickOnItPlaysTheClickSound()
	{
		using var probe = new AudioTriggerProbe();
		var hud = Hud();
		hud.PostGameMessage( "first" );
		hud.PostGameMessage( "second" );
		hud.PostGameMessage( "   " );
		CollectionAssert.AreEqual( new uint[] { 0x1E, 0x1E }, probe.Events( "cat_ui" ), "a blank message makes no icon and so no sound" );
		Assert.IsTrue( hud.OpenPendingMessage( 0 ) );
		Assert.IsFalse( hud.OpenPendingMessage( 7 ) );
		hud.DeletePendingMessage( 0 );
		CollectionAssert.AreEqual( new uint[] { 0x1E, 0x1E, 0xC0 }, probe.Events( "cat_ui" ), "only the left click (an open) plays 0xC0, a delete is silent" );
		Assert.IsTrue( probe.Fired.TrueForAll( item => item.Position == null ), "flat: the original submits them with no place" );
	}
}
