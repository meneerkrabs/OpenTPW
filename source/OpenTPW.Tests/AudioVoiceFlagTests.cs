using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>AUDIO-003: the voice flag word the sound library builds from an event, and the fixed-pitch flag nothing sets (tp.exe 0x6bbe90, 0x6bc484).</summary>
[TestClass]
public class AudioVoiceFlagTests
{
	[DataTestMethod]
	[DataRow( (ushort)0x200, true )]
	[DataRow( (ushort)0x208, true )]
	[DataRow( (ushort)0x606, true )]
	[DataRow( (ushort)0x404, false )]
	[DataRow( (ushort)0x6, false )]
	[DataRow( (ushort)0, false )]
	public void TheLibraryPlacesAnEventFlatExactlyWhenItsAnyDistanceBitIsSet( ushort flags, bool flat )
	{
		Assert.AreEqual( flat, SoundVoiceFlags.IsAnyDistance( flags ) );
		Assert.AreEqual( flat, (SoundVoiceFlags.FromEvent( flags ) & SoundVoiceFlags.AnyDistance) != 0 );
	}

	[TestMethod]
	public void NoEventWordMakesTheFixedPitchVoiceFlag()
	{
		// [BIN:TPW-EXE:0x6bbe90 voice flags from the event] [BIN:TPW-EXE:0x6bc484 fixed-pitch flag]
		for ( var flags = 0; flags <= ushort.MaxValue; flags++ )
		{
			Assert.AreEqual( 0u, SoundVoiceFlags.FromEvent( (uint)flags ) & SoundVoiceFlags.FixedPitch, $"event {flags:X}" );
			Assert.AreEqual( 0u, SoundVoiceFlags.FromEvent( (uint)flags, rangeScaled: true ) & SoundVoiceFlags.FixedPitch, $"event {flags:X} with a range scale" );
		}
	}

	[DataTestMethod]
	[DataRow( 0x0u, 0x0u )]
	[DataRow( 0x4u, 0x2u )]
	[DataRow( 0x6u, 0x2u )]
	[DataRow( 0x8u, 0x6u )]
	[DataRow( 0x208u, 0xEu )]
	[DataRow( 0x404u, 0x2u )]
	[DataRow( 0x606u, 0xAu )]
	[DataRow( 0x20u, 0x200u )]
	[DataRow( 0x4000u, 0x20000u )]
	public void TheVoiceFlagsFollowTheEventBitsOfTheLibraryBuilder( uint eventFlags, uint voiceFlags )
	{
		Assert.AreEqual( voiceFlags, SoundVoiceFlags.FromEvent( eventFlags ) );
	}

}
