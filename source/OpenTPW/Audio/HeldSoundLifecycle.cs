namespace OpenTPW;

/// <summary>The loaded quality controls. Missing host options use the loaded default, not a guessed saved original preference.</summary>
internal sealed record SoundFadeSettings( int DefaultQuality = 50, int FadeThreshold = 30 )
{
	// [DATA:sound.sam:SoundInfo.DEFAULTQUALITY,SoundInfo.FADETOGGLE] supplied defaults 50/30; descriptors 9/10 bind 0x78591C/0x785920.
	internal static SoundFadeSettings Read( SamSettings settings ) => new(
		Math.Clamp( settings.GetInt( "SoundInfo.DEFAULTQUALITY", 50, optional: true ), 0, 100 ),
		Math.Clamp( settings.GetInt( "SoundInfo.FADETOGGLE", 30, optional: true ), 0, 100 ) );
	// a missing host SoundQuality takes the loaded default; the original user's saved preference is not recoverable (see AUDIO-040)
	internal int Resolve( int? saved ) => Math.Clamp( saved ?? DefaultQuality, 0, 100 );
}

/// <summary>A placeholder's logical sample, which survives host segment/decode gaps. All mutations run on the audio host thread.</summary>
internal sealed class HeldSoundLifetime
{
	private readonly AudioMixer mixer;
	private readonly float[] groups = { 1, 1, 1, 1 };
	private AudioVoice? output;
	internal HeldSoundLifetime( AudioMixer mixer, SoundEventPlayer player, uint groupMask )
	{
		this.mixer = mixer;
		Player = player;
		GroupMask = groupMask & 15;
		RefreshGroups();
	}

	internal SoundEventPlayer Player { get; }
	internal uint GroupMask { get; }
	internal bool IsContainer => Player is SoundEventPlayer.OneShotSentence or SoundEventPlayer.OneShotBranchingSentence;
	internal bool HasSample { get; private set; }
	internal bool Fading { get; private set; }
	internal bool StopPending { get; private set; }
	internal bool Terminal { get; private set; }
	internal bool Suspended { get; set; }
	internal bool ReplyAvailable { get; set; } = true;
	internal int OwnVolume { get; private set; }
	internal int DeviceVolume { get; private set; }
	internal bool CanAdmit => !Terminal && !StopPending;
	internal bool PreserveVolume => Fading && HasSample && ReplyAvailable && !IsContainer;

	internal void PostFade()
	{
		if ( CanAdmit ) Fading = true;
	}

	internal void Admit( AudioVoice voice, int volume )
	{
		if ( !CanAdmit ) { voice.Stop(); return; }
		if ( IsContainer ) return; // Children have samples; the container parent never does.
		output = voice;
		voice.HasDeviceGain = true;
		if ( !PreserveVolume )
		{
			OwnVolume = Math.Max( 0, volume );
			DeviceVolume = EffectiveVolume();
		}
		HasSample = ReplyAvailable = true;
		WriteDevice();
	}

	internal void SetVolume( int volume )
	{
		if ( Terminal || StopPending || Fading ) return;
		OwnVolume = Math.Max( 0, volume );
		DeviceVolume = EffectiveVolume();
		WriteDevice();
	}

	internal void RefreshGroups()
	{
		var changed = false;
		for ( var index = 0; index < 4; index++ )
		{
			var value = mixer.GetGroupFactor( index );
			if ( !float.IsFinite( value ) ) value = 0; // Malformed host gains cannot reach the integer conversion.
			changed |= (GroupMask & (1u << index)) != 0 && groups[index] != value;
			groups[index] = value;
		}
		if ( changed && HasSample && !Terminal )
		{
			DeviceVolume = EffectiveVolume(); // A group write never replaces own volume.
			WriteDevice();
		}
	}

	// [BIN:TPW-EXE:0x6BB860 effective volume] the own volume (+0x18) times the product of the enabled group floats (mask +0x24: bit 0 master, 1 music, 2 speech, 3 effects, read in that order from the table +0x34, starting at the float 0x70A7DC) is formed in the x87 registers without a store between the multiplications, stored once as a float (fstp dword) and converted with fistp (round to nearest even); no mask gives the own volume unchanged
	// [BIN:TPW-EXE:0x6B6260,0x6B9E30,0x67E050 fade thread] the released-tail poll (0x6B6260 sends every fading placeholder its fade update, vf+0x2c = 0x6BCB80 -> 0x6C50D0 -> 0x6C570B -> 0x6BB860) runs on the sound library's own worker, started with the CRT's _beginthread (0x6B9EF0); the manager tick 0x6B62B0 calls the same update only when the manager byte +0x50 is set, and 0x6B9F66 clears it before the worker starts
	// [BIN:TPW-EXE:0x00576EC0 control word] the only code that rewrites the x87 control word besides the CRT's save-and-restore wrappers (_ftol 0x67A830) is the font generator's pair 0x591220 / 0x591240 (its only callers are at 0x576FA7, 0x577304 and 0x577310, nowhere near the sound library); the main thread is at 0x7F (24-bit precision) after DDSCL_FPUSETUP at 0x563914 and the D3DIM device creation (docs/COASTERS.md), but the worker is a new thread: it keeps the operating system's default 0x27F (53-bit precision, round to nearest even), and QMixer.dll only restores the word around its own truncating conversions (0x18018179, 0x180181DD)
	// main-thread callers (a group change, a sample's start) multiply at 24 bits, so every intermediate is a float; the poll multiplies at 53 bits, so the group product and the volume product are doubles and only the final store rounds to a float
	private int EffectiveVolume( bool pollThread = false )
	{
		if ( GroupMask == 0 ) return OwnVolume;
		if ( pollThread )
		{
			var wide = 1.0;
			for ( var index = 0; index < 4; index++ )
				if ( (GroupMask & (1u << index)) != 0 ) wide *= groups[index];
			return (int)MathF.Round( (float)(OwnVolume * wide), MidpointRounding.ToEven );
		}
		var product = 1f;
		for ( var index = 0; index < 4; index++ )
			if ( (GroupMask & (1u << index)) != 0 ) product *= groups[index];
		return (int)MathF.Round( OwnVolume * product, MidpointRounding.ToEven );
	}

	/// <summary>The volume the fade poll computes (the worker thread's 53-bit arithmetic), exposed for the tests.</summary>
	internal int PollEffectiveVolume() => EffectiveVolume( pollThread: true );

	/// <summary>The volume a main-thread caller computes (24-bit arithmetic), exposed for the tests.</summary>
	internal int MainEffectiveVolume() => EffectiveVolume();

	/// <summary>An explicit host range stop releases the output; unlike a normal segment gap it has no live reply.</summary>
	internal void LoseOutputReply()
	{
		ReplyAvailable = false;
		output = null;
	}

	// [BIN:TPW-EXE:0x6BCB80,0x6C570B] feedback uses effective integer volume; the device receives the result directly.
	internal void Poll()
	{
		if ( !Fading || Terminal ) return;
		if ( !HasSample || Suspended || !ReplyAvailable ) { MarkStop(); return; }
		OwnVolume = EffectiveVolume( pollThread: true ) * 60 / 100;
		DeviceVolume = OwnVolume;
		WriteDevice();
		if ( OwnVolume == 0 ) MarkStop();
	}

	internal void MarkStop()
	{
		if ( Terminal ) return;
		Fading = false;
		StopPending = true;
	}

	internal void End()
	{
		Terminal = true;
		Fading = StopPending = HasSample = false;
		output?.Stop();
		output = null;
	}

	private void WriteDevice()
	{
		if ( output is { IsFinished: false } ) output.Gain = DeviceVolume / 100f;
	}
}

/// <summary>One shared poll phase, independent of simulation time and of each fade's posting time.</summary>
internal sealed class HeldSoundService
{
	private readonly Func<double> milliseconds;
	private readonly List<HeldSound> sounds = new();
	private int quality = 50;
	private SoundFadeSettings settings = new();
	internal HeldSoundService( Func<double> milliseconds )
	{
		this.milliseconds = milliseconds;
		NextPoll = milliseconds();
		Poll(); // The worker scans immediately at service startup, then sleeps.
	}
	internal double NextPoll { get; private set; }
	internal IReadOnlyList<HeldSound> Sounds => sounds;
	internal int Quality => quality;
	internal bool FadeEnabled => quality > settings.FadeThreshold;
	internal void Configure( SoundFadeSettings configuration, int? selected )
	{
		var wasEnabled = FadeEnabled;
		settings = configuration;
		quality = settings.Resolve( selected );
		if ( wasEnabled && !FadeEnabled )
			foreach ( var sound in sounds )
				if ( sound.Lifetime.Fading ) sound.Lifetime.MarkStop();
	}
	internal HeldSound Add( HeldSound sound ) { sounds.Add( sound ); return sound; }
	internal void Fade( HeldSound sound )
	{
		if ( sound.IsFinished ) return;
		if ( FadeEnabled ) sound.Lifetime.PostFade(); else sound.Stop();
	}
	internal void RefreshGroups()
	{
		foreach ( var sound in sounds ) sound.Lifetime.RefreshGroups();
	}
	internal void Poll()
	{
		var now = milliseconds();
		if ( now < NextPoll ) return;
		var count = 0;
		foreach ( var sound in sounds )
		{
			if ( sound.IsFinished ) { sound.Lifetime.End(); continue; }
			if ( !sound.Lifetime.Fading ) continue;
			count++;
			sound.Lifetime.RefreshGroups();
			sound.Lifetime.Poll();
		}
		// [BIN:TPW-EXE:0x6B9E30] post-scan Sleep(100/250), counting fades before their update.
		// [EXT:fade-poll-host-timing] (user-approved 2026-10-30, AUDIO-040 converted: the original's phase depends on the host operating system's scheduler and, for a missing host SoundQuality, on the user's saved preference, which OpenTPW does not read) host policy: one scan per due callback, then delay from now; no catch-up burst. The original's worker Sleeps 100 or 250 ms (0x6B9E30) at the mercy of the operating system's scheduler, so its phase against the main thread's group refresh and admissions has no rule to port
		NextPoll = now + (count > 0 ? 100 : 250);
	}
	internal void ProcessStops()
	{
		foreach ( var sound in sounds )
			if ( sound.Lifetime.StopPending || sound.IsFinished ) sound.Stop();
		sounds.RemoveAll( sound => sound.IsFinished );
	}
	internal void StopAll( AudioChannel? channel = null )
	{
		foreach ( var sound in sounds )
			if ( channel == null || sound.Channel == channel ) sound.Stop();
		sounds.RemoveAll( sound => sound.IsFinished );
	}
}
