using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class AudioOptionControlTests
{
	[TestMethod]
	public void MuteControlsSilenceActualMixerChannelsWithoutLosingStoredVolumes()
	{
		using var mixer = new AudioMixer( null );
		var options = new GameOptions { SoundEffectsVolume = 80, MusicVolume = 60, SpeechVolume = 40,
			SoundEffectsOn = false, MusicOn = false, SpeechOn = false };
		GameAudio.ApplyOptions( mixer, options );
		Assert.AreEqual( 0f, mixer.GetChannelGain( AudioChannel.Effects ) );
		Assert.AreEqual( 0f, mixer.GetChannelGain( AudioChannel.Music ) );
		Assert.AreEqual( 0f, mixer.GetChannelGain( AudioChannel.Speech ) );
		options.SoundEffectsOn = options.MusicOn = options.SpeechOn = true;
		GameAudio.ApplyOptions( mixer, options );
		Assert.AreEqual( 0.8f, mixer.GetChannelGain( AudioChannel.Effects ) );
		Assert.AreEqual( 0.6f, mixer.GetChannelGain( AudioChannel.Music ) );
		Assert.AreEqual( 0.4f, mixer.GetChannelGain( AudioChannel.Speech ) );
	}
}
