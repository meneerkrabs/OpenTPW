namespace OpenTPW;

public sealed partial class OriginalObjectRuntime
{
	/// <summary>One sound an <c>ADDOBJ</c> of type 3-9 keeps: the script that added it, the record's slot (tp.exe record +0x18), type (+8) and key (+0x10), and the held sound (its handle, +0xC).</summary>
	internal sealed record HeldSoundRecord( int ScriptId, int Slot, int Type, int Key, HeldSound Sound );

	/// <summary>The sound records of this object's scripts, newest first as the original links them (the script's object list, +0xB0). Sounds are presentation: they are in no hash and no saved state.</summary>
	private readonly List<HeldSoundRecord> heldSounds = new();
	private long heldFollowPass = -1;
	private long standaloneFollowPass;

	/// <summary>The live sound records (tests).</summary>
	internal IReadOnlyList<HeldSoundRecord> HeldSounds => heldSounds;

	/// <summary>
	/// <c>ADDOBJ</c> and <c>ADDOBJ_EXT</c> with a sound type (3-9): the event is submitted exactly as an <c>EVENT</c> of that type would be, at the key's place, and the handle is kept under the slot operand
	/// so <c>KILLOBJ</c>, <c>FADEOBJ</c> and <c>SETOBJPARAM</c> reach it; a submit that returns no handle (an unknown key or event, no sound system) keeps no record.
	/// </summary>
	// [BIN:TPW-EXE:0x551F5F ADDOBJ] operands type, key, event, slot; one 0x1C-byte record on the script's list; 0x557970 calls the dispatcher 0x5573D0, which for types 3-9 resolves the key (0x556B90, selector 0x200) and submits 0x51BFC0(0, the type's category, event, the place truncated to whole units), and keeps the record only for a non-zero handle (0x5579F9; a zero handle frees it again, 0x557A0A)
	// [BIN:TPW-EXE:0x5520E8 ADDOBJ_EXT] the control operand is read in place of 1000 and ignored by the sound types (only the particle types apply it, 0x5575B6)
	internal void ObserveHeldSound( RideEffectCall call )
	{
		var extended = call.Opcode == Opcode.ADDOBJ_EXT;
		if ( stopped || call.OperandCount < (extended ? 5 : 4) )
			return;
		var type = Word( call, 0 );
		if ( type is < 3 or > 9 )
			return;
		PruneHeldSounds();
		var key = Word( call, 1 );
		if ( ResolveEventAnchor( key, out var place ) != EventAnchorStatus.Play )
			return;
		var sound = GameAudio.StartHeldRseEvent( type, unchecked((uint)Word( call, 2 )), place, SoundSource );
		if ( sound != null )
			heldSounds.Insert( 0, new HeldSoundRecord( call.VM.ScriptId, call.Argument( extended ? 4 : 3 ), type, key, sound ) );
	}

	/// <summary>
	/// The object pass moves the kept sounds of a script with a key to that key's node (0x51C270, the position truncated to whole units), once in every eighth scheduler pass: the one whose number
	/// matches the script's id in the low three bits (the second gate of the pass, 0x551A1F). Records with a negative key stay where they started.
	/// </summary>
	// [BIN:TPW-EXE:0x551A1C sound follow] the object pass 0x5516B0 walks the records of every script after its slice; for types 3-10 with key (+0x10) >= 0 and ((script id +8) xor pass counter 0x8791A4) & 7 == 0 it reads the key's node matrix (+0x30/+0x34/+0x38, the node found at ADDOBJ time in +0x14) and calls 0x51C270(handle, ftol(x), ftol(y), ftol(z)); a key below 0 is skipped (0x5518A0), and the particle types follow on every pass (FollowParticleObjects)
	private void FollowHeldSounds( SimulationSubstep? substep )
	{
		if ( heldSounds.Count == 0 )
			return;
		// a standalone object has no scheduler pass of its own: each tick counts as one
		var pass = substep != null ? scriptWorld.PassCounter : ++standaloneFollowPass;
		if ( pass == heldFollowPass )
			return;
		heldFollowPass = pass;
		PruneHeldSounds();
		foreach ( var record in heldSounds.ToArray() )
		{
			if ( record.Key < 0 || ((record.ScriptId ^ (int)pass) & 7) != 0 )
				continue;
			if ( ResolveEventAnchor( record.Key, out var place ) == EventAnchorStatus.Play && place is { } moved )
				record.Sound.MoveTo( moved );
		}
	}

	/// <summary>
	/// <c>KILLOBJ</c> and <c>FADEOBJ</c> on a slot: every sound record of the script with the slot ends and is freed.
	/// </summary>
	// [BIN:TPW-EXE:0x5522C5 KILLOBJ] types 3-10 call 0x51C2C0(handle): the library finds the voice by its handle (0x6B6830) and stops it at once (the type-3 message, 0x6B89B0), a handle already gone does nothing
	// [BIN:TPW-EXE:0x5523D0 FADEOBJ] types 3-10 call 0x51C300(handle): message 0x700BAC {handle, 0x3C} runs 0x6B8930, which takes the "fade" call (0x6B65A0 -> the voice's vf+0x30 = 0x6B7130) only while bit 0x400 of the sound library's settings word (+0x28 of the object at library +0x24) is set, and otherwise the stop above (the library's vf+0x18 = 0x6B89B0, the very call KILLOBJ makes); particle types take 0x51FF70(handle, -2) as KILLOBJ does
	// [BIN:TPW-EXE:0x6B85A0 library flag 0x400] the bit is the library's "SoundQuality above the threshold" mode: 0x51B920(SoundQuality) sends it as `level > [0x785920]` (message 0x700B48, 0x51BA77..0x51BAA9) from the options apply (0x4237F0, the `SoundQuality` key of the options file, 0x424530) and from the sound start (0x54DE0E, default [0x78591C]); none of the thresholds is ever written (BSS), so the bit is set exactly while SoundQuality is above 0
	// [BIN:TPW-EXE:0x6B7130 voice fade call] the "fade" is not a ramp: it stores 0x3C as the voice's fixed volume byte (placeholder +0x44) and sets the fixed-volume flag 0x400 (+0x30); the manager tick (0x6B62B0) then re-applies that percentage to the playing sample every tick through vf+0x2C (0x6BCB80) while the manager byte +0x50 is set, and the record, and with it the handle, is freed, so the voice plays on at 60 % until its sample ends
	// [APPROX:AUDIO-040] FADEOBJ stops the sound as KILLOBJ does, which is the original's behaviour at SoundQuality 0 (the value the options object starts with, 0x4236E0 reading the unwritten 0x78591C); OpenTPW has no SoundQuality option, and a player at SoundQuality 1 or more would hear the voice dimmed to 60 % where it plays on instead of ending
	internal void EndHeldSounds( int scriptId, int slot )
	{
		for ( var index = heldSounds.Count - 1; index >= 0; index-- )
		{
			var record = heldSounds[index];
			if ( record.ScriptId != scriptId || record.Slot != slot )
				continue;
			record.Sound.Stop();
			heldSounds.RemoveAt( index );
		}
	}

	/// <summary><c>SETOBJPARAM</c>: the parameter call reaches the sound of every record of the script with the slot.</summary>
	// [BIN:TPW-EXE:0x5524D9 SETOBJPARAM] types 3-10 call 0x51BC40(handle, code, value) -> 0x6B6620, which finds the voice by its handle and calls its vf+0x28 (0x6BBB80, SoundSentence.SetParameter); the record keeps the handle the call returns (0 when the voice is gone), types 1-2 are skipped
	internal void SetHeldSoundParameter( int scriptId, int slot, int code, int value )
	{
		foreach ( var record in heldSounds )
			if ( record.ScriptId == scriptId && record.Slot == slot )
				record.Sound.SetParameter( code, value );
	}

	/// <summary>The script ended or was stopped: its sound records end with it.</summary>
	// [BIN:TPW-EXE:0x558703 script destructor] every record the script holds ends (types 3-10 through 0x51C2C0) and is freed
	internal void EndScriptSounds( int scriptId )
	{
		for ( var index = heldSounds.Count - 1; index >= 0; index-- )
		{
			if ( heldSounds[index].ScriptId != scriptId )
				continue;
			heldSounds[index].Sound.Stop();
			heldSounds.RemoveAt( index );
		}
	}

	/// <summary>The object is removed: every record ends (the destructor of each of its scripts).</summary>
	private void StopHeldSounds()
	{
		foreach ( var record in heldSounds )
			record.Sound.Stop();
		heldSounds.Clear();
	}

	/// <summary>Drops the records of sounds that have played out: the library no longer finds their handles, so nothing a script does to them changes.</summary>
	private void PruneHeldSounds() => heldSounds.RemoveAll( record => record.Sound.IsFinished );
}
