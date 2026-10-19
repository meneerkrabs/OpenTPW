namespace OpenTPW;

/// <summary>
/// The mixer values an original save's Sound block carries (its master, effects, music and speech volumes and the three on-flags), which a park loaded from
/// that save plays at, as tp.exe does, until the player's options take over again. The player's options themselves (<see cref="GameOptions"/>, the per-install
/// <c>opentpw-options.json</c>) are never changed: the original keeps the profile's options in their own struct (<c>0x78D8E8</c>), which the save loader does not
/// touch, and writes the profile file only from that struct (docs/AUDIO.md, "Volumes of a loaded original save").
/// </summary>
// [BIN:TPW-EXE:0x51C400 sound loader] the loader copies the saved 84 bytes into the sound-service globals 0x803AA0, the volumes' shadows included; the tick 0x51BD70 pushes all seven mixer values (master +0x18, effects +0x1C, music +0x20, speech +0x24, the on-flags +0x38/+0x3C/+0x40) while the dirty word +0x28 is non-zero, and park entry sets it after the load (0x54F3AE)
// [BIN:TPW-EXE:0x423DD0 options applier] only the applier pushes the profile's volumes (0x78D8E8..0x78D8FC) to the shadows again; its callers are the options screen's OK (0x4237F0 from 0x4A2BF0), the profile file read and write (0x423E90, 0x4242C0; written at park exit, state 12 of 0x54E360 through 0x5C8650) and the defaults (0x423690), so a loaded save's volumes hold until one of those or a slider or toggle of that channel (0x4A2BF0 messages 0x800/0x801 call one setter)
public sealed class SavedSoundVolumes
{
	/// <summary>A channel's saved volume and on-flag, with the player's option values when the save was applied: a change of those ends the channel's saved value.</summary>
	private sealed record Held( int Volume, bool On, int OptionVolume, bool OptionOn );

	private readonly Held?[] channels = new Held?[3];

	private SavedSoundVolumes( SaveSoundSettings settings, GameOptions options )
	{
		Settings = settings;
		// [BIN:TPW-EXE:0x423DD0 channel pairs] effects = +0x1C with on-flag +0x38, music = +0x20 with +0x3C, speech = +0x24 with +0x40 (the applier pairs each on setter with its volume setter)
		channels[(int)AudioChannel.Effects] = new Held( settings.EffectsVolume, settings.EffectsOn != 0, options.SoundEffectsVolume, options.SoundEffectsOn );
		channels[(int)AudioChannel.Music] = new Held( settings.MusicVolume, settings.MusicOn != 0, options.MusicVolume, options.MusicOn );
		channels[(int)AudioChannel.Speech] = new Held( settings.SpeechVolume, settings.SpeechOn != 0, options.SpeechVolume, options.SpeechOn );
	}

	/// <summary>The save's sound-service values these were taken from.</summary>
	public SaveSoundSettings Settings { get; }

	/// <summary>The saved master volume in percent (<c>+0x18</c>).</summary>
	public int MasterVolume => Settings.MasterVolume;

	/// <summary>True while at least one channel still plays at its saved value.</summary>
	public bool Holding => channels.Any( channel => channel != null );

	/// <summary>
	/// The saved mixer values of <paramref name="sound"/>, taken against the player's current <paramref name="options"/>; null when the block holds no 84-byte settings.
	/// The saved dirty word does not matter: park entry sets it again after the load, so the next tick always pushes the copied shadows.
	/// </summary>
	// [BIN:TPW-EXE:0x54F3AE dirty after load] state 9 of 0x54E360 ends, after the load 0x5ACCF0 -> 0x414D40, with 0x51E6F0(0), which writes the dirty word 0x803AC8 = 1; the tick 0x51BD70 of state 10 then pushes all seven shadows (a restart reloads restart.INTS, written right after that call, whose dirty word is 1 in all three corpus files)
	public static SavedSoundVolumes? FromSave( SaveSoundData? sound, GameOptions options )
	{
		if ( sound?.SettingsView is not { } settings )
			return null;
		return new SavedSoundVolumes( settings, options );
	}

	/// <summary>
	/// The gain <paramref name="channel"/> plays at while its saved value holds, or null once the player's option for it (volume or on-flag) differs from the one
	/// the save was applied over: from then on the options drive that channel, as a slider or toggle's setter replaces the volume shadow in the original. A channel the
	/// save switched off stays silent until OK or leaving the park: the slider and the toggle only set the volume, never the on-flag.
	/// </summary>
	// [BIN:TPW-EXE:0x4A2BF0 slider setters] the sliders' messages 0x800 and 0x801 call only that channel's volume setter (0x51BBA0, 0x51BB80, 0x51BBC0), so the other channels keep the saved shadows; the 0x801 handler skips the setter while the channel's option on byte is clear (0x4A2D6B, 0x4A2DAF, 0x4A2DF3)
	// [BIN:TPW-EXE:0x4A2E90 volume rows] a slider value (message 0x800) and a toggle (0x4A3480: the profile's on byte, then 0x4A2E90) store the profile value and call the volume setter with it, or with 0 while the profile's on byte is clear; the on-flag setters 0x51BC00/0x51BBE0/0x51BC20 are called only by the applier 0x423DD0, so a saved on-flag of 0 keeps the mixer group off (0x6B9900) until then
	public float? Gain( AudioChannel channel, GameOptions options )
	{
		ref var held = ref channels[(int)channel];
		if ( held == null )
			return null;
		var (volume, on) = channel switch
		{
			AudioChannel.Effects => (options.SoundEffectsVolume, options.SoundEffectsOn),
			AudioChannel.Music => (options.MusicVolume, options.MusicOn),
			_ => (options.SpeechVolume, options.SpeechOn),
		};
		if ( volume != held.OptionVolume || on != held.OptionOn )
		{
			if ( !held.On )
				return 0;
			held = null;
			return null;
		}
		// the tick sends each percent and on-flag as saved (0x51BD70); OpenTPW's mixer takes a 0..1 gain, so a percent outside 0..100 is clamped ([BIN:TPW-EXE:0x4a4490 volume sliders] range 0..100)
		return GameOptions.Gain( held.Volume, held.On );
	}

	/// <summary>True when <paramref name="options"/> would play exactly what the save holds (master 100 and the same volumes and on-flags): applying the save is then inaudible.</summary>
	public bool MatchesOptions( GameOptions options ) =>
		Settings.MasterVolume == GameOptions.MaximumVolume
		&& Settings.EffectsVolume == options.SoundEffectsVolume && (Settings.EffectsOn != 0) == options.SoundEffectsOn
		&& Settings.MusicVolume == options.MusicVolume && (Settings.MusicOn != 0) == options.MusicOn
		&& Settings.SpeechVolume == options.SpeechVolume && (Settings.SpeechOn != 0) == options.SpeechOn;

	/// <summary>The import report's description of the values: master, effects, music and speech percent with their on-flags.</summary>
	public string Describe() =>
		$"master {Settings.MasterVolume}, effects {Settings.EffectsVolume} {OnOff( Settings.EffectsOn )}, music {Settings.MusicVolume} {OnOff( Settings.MusicOn )}, speech {Settings.SpeechVolume} {OnOff( Settings.SpeechOn )}";

	private static string OnOff( uint flag ) => flag != 0 ? "on" : "off";
}
