using System.Linq;

namespace OpenTPW.Tests;

/// <summary>Expected levels of event-system voices: the driver's table, then the decibel quantisation (tp.exe 0x6D2980, QMixer 0x1800586B and 0x1800CA40).</summary>
internal static class AudioLevels
{
	/// <summary>The amplitude factor a voice with the volume index <paramref name="index"/> has when nothing else scales it.</summary>
	internal static float Of( int index = 100 ) => AudioListener.DecibelGain( AudioListener.GainHundredthsOfDecibel( SoundVolume.Library( index )!.Value ) );

	/// <summary>The mixer's output for samples scaled by <see cref="Of"/>, truncated to 16 bits as the mixer does.</summary>
	internal static short[] Scale( int index, params int[] samples ) => samples.Select( sample => (short)(sample * Of( index )) ).ToArray();

	/// <summary>Marks a voice the way the event system does, so a reference mixer applies the library volume chain too.</summary>
	internal static AudioVoice AsEvent( this AudioVoice voice )
	{
		voice.UsesLibraryVolume = true;
		return voice;
	}
}
