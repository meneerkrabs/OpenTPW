namespace OpenTPW;

public sealed class MP2File : ArchiveFile
{
	public enum SoundTypes
	{
		NONE = 0, //  on blanks
		WAV = 2, // on wav
		WAV_OLD = 3, // used before 1.7, like in the german cd version
		MP2_MONO = 36, // on mp2 (64kbit/s mono)
		MP2_STEREO = 37 // on mp2 (112kbit/s stereo)
	}

	public string Name { get; set; }
	public int Header { get; set; }
	public byte[] SoundData { get; set; }
	public byte[] Data { get; set; }
	/// <summary>Validated first-frame rate, or the supplied container value when unknown.</summary>
	public int SampleRate { get; set; }
	/// <summary>Validated first-frame channels, or a container type hint; zero means unknown.</summary>
	public int Channels { get; set; }
	public int BitsPerSample { get; set; }

	public SoundTypes SoundType { get; set; }

	/// <summary>
	/// Raw 32-bit value at entry offset +32: the number of 16-bit PCM bytes (all channels) the game plays when the
	/// type byte has bit 0x04, which every shipped entry has (<see cref="SdtEntryHeader.PlaybackBytes"/>).
	/// </summary>
	public int RawSampleField { get; set; }

	/// <summary>The playback length in PCM bytes, or null when the type byte lacks bit 0x04.</summary>
	public int? PlaybackBytes => ((int)SoundType & SdtEntryHeader.PlaybackLengthFlag) != 0 && RawSampleField >= 0 ? RawSampleField : null;

	public MP2File( int header, string name, byte[] soundData, int sampleRate, int bitsPerSample, int soundType, int rawSampleField, byte[] data )
	{
		Header = header;
		Name = name;
		SoundData = soundData;
		SampleRate = sampleRate;
		BitsPerSample = bitsPerSample;
		SoundType = (SoundTypes)soundType;
		RawSampleField = rawSampleField;
		Data = data;
		Channels = SoundType switch
		{
			SoundTypes.MP2_MONO => 1,
			SoundTypes.MP2_STEREO => 2,
			_ => 0,
		};
		if ( Header >= 0 && Header <= Data.Length &&
			Mp2Decoder.TryReadFrameFormat( Data.AsSpan( Header ), out var frameRate, out var frameChannels ) )
		{
			SampleRate = frameRate;
			Channels = frameChannels;
		}
	}

	/// <summary>
	/// The MPEG Layer I or II frame stream after the entry header (the suffix may be .mp2 for either). <see cref="Header"/> (the first
	/// header word, 40 in every shipped entry) gives its offset.
	/// </summary>
	public byte[] FrameData => Data[Math.Min( Header, Data.Length )..];

	public override byte[] GetData()
	{
		return Data;
	}
}
