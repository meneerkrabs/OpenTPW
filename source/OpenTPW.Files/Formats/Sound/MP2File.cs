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
	public int SampleRate { get; set; }
	public int BitsPerSample { get; set; }

	public SoundTypes SoundType { get; set; }

	/// <summary>
	/// Raw 32-bit value at entry offset +32. Not proven to be a sample count: it is about
	/// twice the frame-derived sample count for mono entries and about four times for stereo,
	/// but with no exact relation, so it is kept unconverted.
	/// </summary>
	public int RawSampleField { get; set; }

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
	}

	/// <summary>
	/// The MPEG frame stream after the entry header. <see cref="Header"/> (the first
	/// header word, 40 in every shipped entry) gives its offset.
	/// </summary>
	public byte[] FrameData => Data[Math.Min( Header, Data.Length )..];

	public override byte[] GetData()
	{
		return Data;
	}
}
