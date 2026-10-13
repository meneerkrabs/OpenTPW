using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class LoadProfileTests
{
	[TestCleanup]
	public void Cleanup() => LoadProfile.Enable( false );

	[TestMethod]
	public void SectionsCountOnlyWhileEnabledAndNest()
	{
		LoadProfile.Enable( false );
		using ( LoadProfile.Measure( "off" ) ) { }
		Assert.AreEqual( 0, LoadProfile.Snapshot().Count, "nothing is recorded while off" );

		LoadProfile.Enable( true );
		using ( LoadProfile.Measure( "outer" ) )
		{
			for ( var index = 0; index < 3; index++ )
				using ( LoadProfile.Measure( "inner" ) )
					Thread.Sleep( 2 );
		}
		var snapshot = LoadProfile.Snapshot();
		Assert.AreEqual( (1, 3), (snapshot["outer"].Count, snapshot["inner"].Count) );
		Assert.IsTrue( snapshot["outer"].Milliseconds >= snapshot["inner"].Milliseconds, "a parent's time includes its children's" );
		Assert.IsTrue( snapshot["inner"].Milliseconds >= 5 );

		LoadProfile.Enable( true );
		Assert.AreEqual( 0, LoadProfile.Snapshot().Count, "enabling again starts a new profile" );
	}

	[TestMethod]
	public void TimeWhileThePausedLoadWaitsIsLeftOut()
	{
		LoadProfile.Enable( true );
		using ( LoadProfile.Measure( "built over frames" ) )
		{
			Thread.Sleep( 5 );
			LoadProfile.Pause();
			Thread.Sleep( 200 );
			LoadProfile.Resume();
			Thread.Sleep( 5 );
		}
		var milliseconds = LoadProfile.Snapshot()["built over frames"].Milliseconds;
		Assert.IsTrue( milliseconds >= 9 && milliseconds < 150, $"{milliseconds} ms: only the work, not the wait between frames" );
	}

	[TestMethod]
	public void OtherThreadsAreNotCounted()
	{
		LoadProfile.Enable( true );
		Task.Run( () => { using ( LoadProfile.Measure( "worker" ) ) { } } ).Wait();
		Assert.IsFalse( LoadProfile.Snapshot().ContainsKey( "worker" ) );
	}
}
