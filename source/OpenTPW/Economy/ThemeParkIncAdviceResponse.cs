namespace OpenTPW;

/// <summary>
/// An advisor response the queue can play so far: its speech event in the global <c>cat_speech</c> catalogue, its tag and the tag's <c>TAG_SYSTEM.str</c> entry (0 for none). The response
/// table (<c>0xBEF1F0</c>, 993 rows) comes with the presentation (P2); these are the rows of the messages posted today, the four forecasts and the three notification letters.
/// </summary>
// [BIN:TPI-EXE:0x70F59E forecast response rows] [BIN:TPI-EXE:0x7104A0 notification response rows] rows of 0x715570 (tools/ppc-analysis/oracle_x86/tpi_advisor_forecast_oracle.py):
// response, speech +4, tag +0x1C; [BIN:TPI-EXE:0x6365A0 plain caption] tag field +8 is the caption's entry
public readonly record struct ThemeParkIncAdviceResponse( int Response, uint SpeechEvent, int Tag, int TextEntry )
{
	/// <summary>The tag of a response that broadcasts no caption (<c>0x97</c>).</summary>
	public const int NoTag = 0x97;

	private static readonly ThemeParkIncAdviceResponse[] Known =
	{
		new( 642, 763, 62, 127 ), new( 645, 766, 65, 130 ), new( 648, 769, 68, 133 ), new( 649, 938, 71, 156 ),
		new( 748, 868, NoTag, 0 ), new( 749, 869, 0x5E, 166 ), new( 750, 870, 0x5F, 0 ),
	};

	/// <summary>The row of <paramref name="response"/>, or null for a response OpenTPW cannot play yet.</summary>
	public static ThemeParkIncAdviceResponse? For( int response ) => Array.FindIndex( Known, row => row.Response == response ) is var index and >= 0 ? Known[index] : null;

	/// <summary>The forecast message the weather posts for a spell kind (1 heatwave, 2 rain, 3 storm, 4 snow), 0 for none.</summary>
	// [BIN:TPI-EXE:0x6420AF forecast dispatch] the newly rolled spell's kind picks 0x154, 0x157, 0x15B or 0x15A
	public static int ForecastMessage( int kind ) => kind switch { 1 => 0x154, 2 => 0x157, 3 => 0x15B, 4 => 0x15A, _ => 0 };

	/// <summary>Whether the response is one of the four forecasts, whose caption OpenTPW shows on the advice line.</summary>
	public bool Forecast => Response is 642 or 645 or 648 or 649;
}

/// <summary>
/// The speech duration the advisor's playback window holds (the value <c>0x716230</c> returns), read from the speech data and never from an audio device, so every run of the
/// simulation sees the same windows. Lengths are kept per response for the park's life.
/// </summary>
// [EXT:tpi-capability-gates] the native duration is the advisor's clip sequence for the response (GenerateAnimSequence 0x715AB0 or a canned sequence 0x715BC0, clip durations
// from the advisor model), the closing clip and 1000 ms; the sequences, the model and their presentation random source come with the presentation (P2). Until then the duration is the
// response's voice length, decoded from its one sample as the client plays it, which is what the window held before; a response without a voice has duration 0, the original's
// no-voice path, so its window is the 1000 ms the update adds
internal sealed class ThemeParkIncAdvisorSpeech
{
	private readonly Dictionary<int, uint> lengths = new();
	private SoundCategory? speech;
	private bool loaded;

	public uint Duration( int response )
	{
		if ( lengths.TryGetValue( response, out var known ) )
			return known;
		return lengths[response] = Measure( response );
	}

	private uint Measure( int response )
	{
		if ( ThemeParkIncAdviceResponse.For( response ) is not { } row || FileSystem is not { } files )
			return 0;
		if ( !loaded )
		{
			loaded = true;
			speech = SoundCategory.TryLoad( files, "/global/Speech", "cat_speech" );
		}
		if ( speech?.Catalog.Find( row.SpeechEvent ) is not { Sounds: [{ Samples: [var sample] }] } )
			return 0;
		try
		{
			if ( speech.GetBank( sample.BankNumber ) is not { } bank || bank.GetMpeg( sample.SampleNumber, Mp2Decoder.MaximumInputBytes, out _, out var playbackBytes ) is not { } mpeg
				|| Mp2Decoder.Decode( mpeg, playbackBytes ) is not { SampleRate: > 0 } audio )
				return 0;
			return (uint)(audio.SampleFrames * 1000L / audio.SampleRate);
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException )
		{
			Log.Warning( $"Theme Park Inc advisor: the voice of response {response} does not decode ({exception.Message}); its duration is 0." );
			return 0;
		}
	}
}
