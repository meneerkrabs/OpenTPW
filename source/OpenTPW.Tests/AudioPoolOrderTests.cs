using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// AUDIO-038: the voice pool orders voices by rank, and among equal ranks the newer voice first, as the original's insertion does
/// (tp.exe 0x6b6a80 links a new voice at the head of the manager's list, 0x6b62b0 meets it first, 0x6b6bc0 slots a voice before the
/// first slot of strictly lower rank). Synthetic data.
/// </summary>
[TestClass]
public class AudioPoolOrderTests
{
	[TestMethod]
	public void AHigherRankComesFirstWhateverTheAge()
	{
		Assert.IsTrue( SoundEventSystem.ComparePoolOrder( 10, 1, 5, 2 ) < 0 );
		Assert.IsTrue( SoundEventSystem.ComparePoolOrder( 5, 2, 10, 1 ) > 0 );
	}

	[TestMethod]
	public void AmongEqualRanksTheNewerVoiceComesFirst()
	{
		Assert.IsTrue( SoundEventSystem.ComparePoolOrder( 7, 9, 7, 3 ) < 0, "serial 9 is newer than 3" );
		Assert.IsTrue( SoundEventSystem.ComparePoolOrder( 7, 3, 7, 9 ) > 0 );
		Assert.AreEqual( 0, SoundEventSystem.ComparePoolOrder( 7, 3, 7, 3 ) );
	}

	[TestMethod]
	public void TheTwelveSlotsGoToTheHighestRanksAndThenTheNewestVoices()
	{
		// Twenty voices, two ranks alternating: all ten of the higher rank fit, then the two newest of the lower rank.
		var voices = Enumerable.Range( 0, 20 ).Select( index => (Rank: index % 2 == 0 ? 100u : 50u, Serial: (long)index + 1) ).ToList();
		var pool = voices.OrderBy( voice => voice, Comparer<(uint Rank, long Serial)>.Create( ( a, b ) => SoundEventSystem.ComparePoolOrder( a.Rank, a.Serial, b.Rank, b.Serial ) ) )
			.Take( SoundEventSystem.VoicePool ).ToList();
		Assert.AreEqual( 10, pool.Count( voice => voice.Rank == 100 ) );
		CollectionAssert.AreEqual( new long[] { 20, 18 }, pool.Where( voice => voice.Rank == 50 ).Select( voice => voice.Serial ).ToArray() );
	}

	[TestMethod]
	public void EveryNewVoiceIsNewerThanTheLast()
	{
		var first = VoiceOrder.Next();
		var second = VoiceOrder.Next();
		Assert.IsTrue( second > first );
	}
}
