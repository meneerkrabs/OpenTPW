using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class ParkBuildStepBoundaryTests
{
	[TestMethod]
	public void EveryMoveNextRunsOnlyItsOwnStepBeforeTheTerminalBoundary()
	{
		foreach ( var markers in new[]
		{
			Array.Empty<bool>(), new[] { true }, new[] { false },
			new[] { false, true, true }, new[] { true, false, true }, new[] { true, false },
		} )
		{
			var work = new List<int>();
			var finished = false;
			IEnumerable<bool> Steps()
			{
				for ( var index = 0; index < markers.Length; index++ )
				{
					work.Add( index );
					yield return markers[index];
				}
				finished = true;
			}
			using var steps = GameFlow.EndingBeforeLong( Steps() ).GetEnumerator();
			Assert.AreEqual( 0, work.Count, "creating the iterator does not run work" );
			for ( var index = 0; index < markers.Length; index++ )
			{
				Assert.IsTrue( steps.MoveNext() );
				Assert.AreEqual( markers[index], steps.Current );
				CollectionAssert.AreEqual( Enumerable.Range( 0, index + 1 ).ToArray(), work );
				Assert.IsFalse( finished, "no lookahead past the current yield" );
			}
			Assert.IsTrue( steps.MoveNext(), "even an empty source gets a boundary before following long work" );
			Assert.IsFalse( steps.Current );
			Assert.IsTrue( finished );
			Assert.AreEqual( markers.Length, work.Count );
			Assert.IsFalse( steps.MoveNext() );
			Assert.IsFalse( steps.MoveNext(), "completion stays complete" );
		}
	}

	[TestMethod]
	public void ShaderModelAndFollowingCaptureRespectTheirFrameBoundaries()
	{
		var work = new List<string>();
		IEnumerable<bool> Presentation()
		{
			work.Add( "shader" );
			yield return false;
			work.Add( "global model" );
			yield return true;
			work.Add( "level model" );
			yield return true;
		}
		IEnumerable<bool> Preparation()
		{
			foreach ( var step in GameFlow.EndingBeforeLong( Presentation() ) )
				yield return step;
			work.Add( "capture" );
			yield return true;
		}
		using var steps = Preparation().GetEnumerator();
		// PumpParkBuild keeps advancing while there is budget, but stops the frame on false.
		void PumpFrame()
		{
			while ( steps.MoveNext() )
				if ( !steps.Current )
					break;
		}
		PumpFrame();
		CollectionAssert.AreEqual( new[] { "shader" }, work );
		PumpFrame();
		CollectionAssert.AreEqual( new[] { "shader", "global model", "level model" }, work );
		PumpFrame();
		CollectionAssert.AreEqual( new[] { "shader", "global model", "level model", "capture" }, work );
	}

	[TestMethod]
	public void DisposingAtTheFirstBoundaryDoesNotRunTheFollowingLongStep()
	{
		var work = new List<string>();
		var disposed = false;
		IEnumerable<bool> Steps()
		{
			try
			{
				work.Add( "shader" );
				yield return false;
				work.Add( "global model" );
				yield return true;
			}
			finally { disposed = true; }
		}
		using ( var steps = GameFlow.EndingBeforeLong( Steps() ).GetEnumerator() )
		{
			Assert.IsTrue( steps.MoveNext() );
			Assert.IsFalse( steps.Current );
			CollectionAssert.AreEqual( new[] { "shader" }, work );
			Assert.IsFalse( disposed );
		}
		Assert.IsTrue( disposed, "disposing the wrapper disposes its source" );
		CollectionAssert.AreEqual( new[] { "shader" }, work );
	}

	[TestMethod]
	public void AFailureAfterABoundaryIsNotPulledIntoThePreviousFrame()
	{
		var disposed = false;
		IEnumerable<bool> Steps()
		{
			try
			{
				yield return false;
				throw new InvalidOperationException( "following step" );
			}
			finally { disposed = true; }
		}
		using var steps = GameFlow.EndingBeforeLong( Steps() ).GetEnumerator();
		Assert.IsTrue( steps.MoveNext() );
		Assert.IsFalse( steps.Current );
		Assert.IsFalse( disposed );
		Assert.ThrowsException<InvalidOperationException>( () => steps.MoveNext() );
		Assert.IsTrue( disposed );
	}
}
