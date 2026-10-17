using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Veldrid;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public sealed class BrowserTexturePackTests
{
	[TestMethod]
	public void ThreadlessSwitchWaitsForPumpAndUploadsEachDecodedTargetImmediately()
	{
		var decoded = new List<string>();
		var applied = new List<string>();
		var targets = Enumerable.Range( 0, 20 ).Select( index => ($"{index}.wct", TextureFlags.None) ).ToList();
		var textureSwitch = new TexturePackSwitch( targets, TimeSpan.FromSeconds( 5 ), path =>
		{
			Assert.AreEqual( decoded.Count, applied.Count, "the browser never accumulates decoded frames" );
			decoded.Add( path );
			return (new byte[4], 1, 1);
		}, (path, _, _, _, _) => applied.Add( path ), decodeOnRenderThread: true );
		Assert.AreEqual( 0, decoded.Count, "starting a switch must not block its caller" );
		textureSwitch.Pump();
		Assert.IsTrue( textureSwitch.Finished );
		Assert.AreEqual( 20, textureSwitch.Done );
		CollectionAssert.AreEqual( decoded, applied );
	}

	[TestMethod]
	public void ThreadlessSwitchKeepsBadTextureAndContinuesThenNotifiesOnce()
	{
		var notifications = 0;
		void Notify() => notifications++;
		TexturePackSwitch.PackChanged += Notify;
		try
		{
			var applied = new List<string>();
			var textureSwitch = new TexturePackSwitch( new() { ("bad.wct", TextureFlags.None), ("good.wct", TextureFlags.None) },
				TimeSpan.FromSeconds( 5 ), path => path == "bad.wct" ? throw new InvalidDataException() : (new byte[4], 1, 1),
				(path, _, _, _, _) => applied.Add( path ), decodeOnRenderThread: true );
			textureSwitch.Pump();
			textureSwitch.Pump();
			CollectionAssert.AreEqual( new[] { "good.wct" }, applied );
			Assert.AreEqual( 2, textureSwitch.Done );
			Assert.AreEqual( 1, notifications );
		}
		finally
		{
			TexturePackSwitch.PackChanged -= Notify;
		}
	}

	[TestMethod]
	public void CancellingThreadlessSwitchBeforePumpDoesNotDecodeOrAnnounce()
	{
		var textureSwitch = new TexturePackSwitch( new() { ("unused.wct", TextureFlags.None) }, TimeSpan.FromSeconds( 5 ),
			_ => throw new AssertFailedException( "cancelled switch decoded" ), decodeOnRenderThread: true );
		textureSwitch.Cancel();
		textureSwitch.Pump();
		Assert.IsTrue( textureSwitch.Finished );
		Assert.AreEqual( 0, textureSwitch.Done );
	}
}
