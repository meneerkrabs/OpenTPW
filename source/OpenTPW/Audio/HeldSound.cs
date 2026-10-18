namespace OpenTPW;

/// <summary>
/// A sound a ride script keeps a record of (<c>ADDOBJ</c> types 3-9): the original keeps the handle of its submit (0x51BFC0) under the record's slot, so the script
/// can move it with the object (0x51C270), give it a parameter (<c>SETOBJPARAM</c>, 0x51BC40) and end it (<c>KILLOBJ</c>, <c>FADEOBJ</c>, 0x51C2C0 and 0x51C300). A repeating
/// event is a sentence here, a one-shot a placed sound that starts when its sample is decoded.
/// </summary>
public sealed class HeldSound
{
	private readonly SoundSentence? sentence;
	private readonly PlacedSound? placed;

	internal HeldSound( SoundSentence sentence, HeldSoundService service, AudioChannel channel )
	{ this.sentence = sentence; this.service = service; Channel = channel; }

	internal HeldSound( PlacedSound placed, HeldSoundService service, AudioChannel channel )
	{ this.placed = placed; this.service = service; Channel = channel; }

	private readonly HeldSoundService service;
	internal AudioChannel Channel { get; }
	internal HeldSoundLifetime Lifetime => (sentence?.Lifetime ?? placed?.Lifetime)!;
	/// <summary>Releases a quality-enabled tail to the sound service; posting does not change its volume.</summary>
	public void Fade() => service.Fade( this );

	/// <summary>The sentence of a repeating event, null for a one-shot.</summary>
	public SoundSentence? Sentence => sentence;

	/// <summary>The placed one-shot, null for a sentence.</summary>
	public PlacedSound? Placed => placed;

	/// <summary>The sound's current place: null for a flat sound.</summary>
	public Vector3? Position => sentence != null ? sentence.Position : placed!.Position;

	/// <summary>Whether the sound has ended (stopped, or a one-shot that has played out); the library then no longer finds its handle, so later calls do nothing.</summary>
	public bool IsFinished => Lifetime.Terminal || (sentence != null ? sentence.IsStopped : placed!.IsFinished);

	/// <summary>Moves the sound to <paramref name="place"/> (the original's 0x51C270, which hands the new place to the voice).</summary>
	public void MoveTo( Vector3 place )
	{
		if ( IsFinished )
			return;
		sentence?.MoveTo( place );
		placed?.MoveTo( place );
	}

	/// <summary>The original's parameter call (0x51BC40 → 0x6BBB80) on the held voice; nothing once the sound has ended.</summary>
	public void SetParameter( int code, int value )
	{
		if ( IsFinished )
			return;
		sentence?.SetParameter( code, value );
		placed?.SetParameter( code, value );
	}

	/// <summary>Ends the sound at once (0x51C2C0): the voice stops, no tail.</summary>
	public void Stop()
	{
		sentence?.Stop();
		placed?.Stop();
	}
}
