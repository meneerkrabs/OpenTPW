using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class InstallationDiscoveryTests
{
	[TestMethod]
	public void ExternalNativeTestHostLaunchesTheApplicationThroughDotnet()
	{
		var host = Path.Combine( "sdk", "dotnet.exe" );
		var folder = "a folder with spaces";
		var info = InstallationDiscovery.ChildStartForHost( "testhost.exe", false, host, InstallationDiscovery.InspectCommand, folder );
		Assert.AreEqual( host, info.FileName );
		Assert.AreEqual( typeof( Program ).Assembly.Location, info.ArgumentList[0] );
		Assert.AreEqual( InstallationDiscovery.InspectCommand, info.ArgumentList[1] );
		Assert.AreEqual( folder, info.ArgumentList[2] );
		Assert.IsFalse( info.UseShellExecute );
	}

	[TestMethod]
	public void ApplicationApphostKeepsItsOwnExecutableForSetupChildren()
	{
		var info = InstallationDiscovery.ChildStartForHost( "renamed-game.exe", true, "unused-dotnet", InstallationDiscovery.SearchCommand );
		Assert.AreEqual( "renamed-game.exe", info.FileName );
		Assert.AreEqual( 1, info.ArgumentList.Count );
		Assert.AreEqual( InstallationDiscovery.SearchCommand, info.ArgumentList[0] );
	}

	[TestMethod]
	public void InspectsSyntheticFoldersWithoutStartingTheGame()
	{
		var root = Path.Combine( Path.GetTempPath(), "opentpw-inspect-" + Guid.NewGuid().ToString( "N" ) );
		try
		{
			foreach ( var part in new[] { "levels", "global", "Language/English" } )
				Directory.CreateDirectory( Path.Combine( root, "Data", part ) );
			var result = InstallationDiscovery.InspectAsync( root ).GetAwaiter().GetResult();
			Assert.IsFalse( result.TimedOut, result.Error );
			Assert.IsNull( result.Error );
			Assert.IsTrue( result.Reports.Single().IsUsable );
			Assert.AreEqual( root, result.Reports.Single().Path );
		}
		finally { Directory.Delete( root, true ); }
	}

	[TestMethod]
	public void CancelledRequestDoesNotStartAChild()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var result = InstallationDiscovery.InspectAsync( "unused", cancellation: cancellation.Token ).GetAwaiter().GetResult();
		Assert.IsFalse( result.TimedOut );
		Assert.AreEqual( 0, result.Reports.Count );
		StringAssert.Contains( result.Error!, "cancelled" );
	}

	[TestMethod]
	public void CancelledPickerDoesNotOpenANativeDialog()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var result = InstallationDiscovery.PickAsync( "Synthetic", null, cancellation.Token ).GetAwaiter().GetResult();
		Assert.AreEqual( 0, result.Reports.Count );
		Assert.IsFalse( result.TimedOut );
		StringAssert.Contains( result.Error!, "cancelled" );
	}

	[TestMethod]
	public void TimeoutKillsAStalledChildAndReleasesTheProbeSlot()
	{
		if ( OperatingSystem.IsWindows() )
			return; // The Unix sleep fixture has no subprocesses and is never a game binary.
		var info = new ProcessStartInfo( "/bin/sleep" ) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
		info.ArgumentList.Add( "30" );
		var watch = Stopwatch.StartNew();
		var result = InstallationDiscovery.RunAsync( info, TimeSpan.FromMilliseconds( 100 ) ).GetAwaiter().GetResult();
		Assert.IsTrue( result.TimedOut );
		Assert.IsTrue( watch.Elapsed < TimeSpan.FromSeconds( 3 ) );
		var missing = InstallationDiscovery.InspectAsync( Path.Combine( Path.GetTempPath(), Guid.NewGuid().ToString( "N" ) ) ).GetAwaiter().GetResult();
		Assert.IsFalse( missing.TimedOut, missing.Error );
		Assert.IsFalse( missing.Reports.Single().IsUsable );
	}

	[TestMethod]
	public void AutomaticSearchDoesNotBlockManualFolderInspection()
	{
		if ( OperatingSystem.IsWindows() ) return;
		var info = new ProcessStartInfo( "/bin/sleep" ) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
		info.ArgumentList.Add( "30" );
		using var cancellation = new CancellationTokenSource();
		var search = InstallationDiscovery.RunAsync( info, TimeSpan.FromSeconds( 5 ), cancellation.Token, search: true );
		try
		{
			var manual = InstallationDiscovery.InspectAsync( Path.Combine( Path.GetTempPath(), Guid.NewGuid().ToString( "N" ) ) ).GetAwaiter().GetResult();
			Assert.IsFalse( manual.TimedOut, manual.Error );
			Assert.IsFalse( manual.Reports.Single().IsUsable );
			Assert.IsFalse( search.IsCompleted, "Manual validation did not wait for the automatic scan." );
		}
		finally { cancellation.Cancel(); search.GetAwaiter().GetResult(); }
	}

	[TestMethod]
	public void FailedKillStillWaitsForConfirmedExitBeforeReleasingSlot()
	{
		using var slot = new SemaphoreSlim( 0, 1 );
		var exit = new System.Threading.Tasks.TaskCompletionSource( System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously );
		var disposed = false;
		var cleanup = InstallationDiscovery.ReleaseAfterExitAsync(
			() => throw new System.ComponentModel.Win32Exception( "synthetic kill failure" ),
			() => exit.Task, () => disposed = true, slot );
		Assert.IsFalse( cleanup.IsCompleted );
		Assert.AreEqual( 0, slot.CurrentCount );
		Assert.IsFalse( disposed );
		exit.SetResult();
		cleanup.GetAwaiter().GetResult();
		Assert.AreEqual( 1, slot.CurrentCount );
		Assert.IsTrue( disposed );
	}

	[TestMethod]
	public void FailedExitConfirmationDoesNotAdmitAnotherChild()
	{
		using var slot = new SemaphoreSlim( 0, 1 );
		var disposed = false;
		InstallationDiscovery.ReleaseAfterExitAsync( () => { },
			() => throw new System.ComponentModel.Win32Exception( "synthetic status failure" ),
			() => disposed = true, slot ).GetAwaiter().GetResult();
		Assert.AreEqual( 0, slot.CurrentCount );
		Assert.IsFalse( disposed );
	}

	[TestMethod]
	public void LogicalOnlyDpiChangeUpdatesScaleAndFooterHasRoom()
	{
		var pixels = new Point2( 760, 560 );
		Assert.AreEqual( 1f, SetupWizard.DpiScale( new Point2( 760, 560 ), pixels ) );
		Assert.AreEqual( 2f, SetupWizard.DpiScale( new Point2( 380, 280 ), pixels ) );
		Assert.IsTrue( SetupWizard.MinimumWidth >= 90 + 150 + 150 + 48,
			"The Quit, Back and Next footer fits the minimum logical width." );
	}
}
