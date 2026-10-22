namespace OpenTPW;

public static partial class OpcodeHandlers
{
	/// <summary>Writes a hooked opcode's result to <paramref name="dest"/> and sets flags from it.</summary>
	private static void EffectResult( RideVM vm, Opcode opcode, Operand dest, params Operand[] operands )
	{
		var result = vm.Effect( opcode, operands );
		dest.Value = result;
		vm.SetFlags( result );
	}

	/// <summary>Objects, particles, sound, screams and the park clock. All are pure game effects (inputs only) unless noted.</summary>
	public static class Effects
	{
		[OpcodeHandler( Opcode.ADDOBJ, RideOpcodeStatus.Hooked, "TPW-EXE 0x551F5F (`type key id slot`, each resolved: a variable's value or the literal sign-extended from 16 bits): one 0x1C-byte record on the script's object list (+0xB0, newest first) keyed by the slot; the dispatcher 0x5573D0(type, key, id, control 1000) via 0x557970 gives its handle, a zero handle frees the record again; types 1-2 are particle effects, 3-9 sounds by category, 10 the object type's own sound list; a negative key (the corpus's 0xFFFF) is the footprint middle" )]
		public static void AddObj( RideVM vm, Operand type, Operand parameter, Operand id, Operand slot ) => vm.Effect( Opcode.ADDOBJ, new[] { type, parameter, id, slot } );

		[OpcodeHandler( Opcode.ADDOBJ_EXT, RideOpcodeStatus.Hooked, "TPW-EXE 0x5520E8 (`type key id control slot`, each resolved): ADDOBJ with the control operand in place of 1000; no corpus use" )]
		public static void AddObjExt( RideVM vm, Operand type, Operand key, Operand effect, Operand control, Operand slot ) => vm.Effect( Opcode.ADDOBJ_EXT, new[] { type, key, effect, control, slot } );

		[OpcodeHandler( Opcode.KILLOBJ, RideOpcodeStatus.Hooked, "TPW-EXE 0x5522C5 (`slot`, resolved): every record with the slot ends and is freed: types 1-2 through 0x51FF70(handle, -2), 3-10 through 0x51C2C0, any other type logs; flags untouched (`ADD VAR_COUNT 0xFFFF; KILLOBJ 200; BRANCH_PV`)" )]
		public static void KillObj( RideVM vm, Operand slot ) => vm.Effect( Opcode.KILLOBJ, new[] { slot } );

		[OpcodeHandler( Opcode.FADEOBJ, RideOpcodeStatus.Hooked, "TPW-EXE 0x5523D0 (`slot`, resolved): as KILLOBJ, but types 3-10 end through 0x51C300 (types 1-2 take the same 0x51FF70(handle, -2)); flags untouched" )]
		public static void FadeObj( RideVM vm, Operand slot ) => vm.Effect( Opcode.FADEOBJ, new[] { slot } );

		[OpcodeHandler( Opcode.SETOBJPARAM, RideOpcodeStatus.Hooked, "TPW-EXE 0x5524D9 (`slot param value`, each resolved): for every record with the slot, types 3-10 call 0x51BC40(handle, param, value) and keep the handle it returns, types 1-2 do nothing; the corpus sets param 20 to 0 or 75 on slot 1" )]
		public static void SetObjParam( RideVM vm, Operand slot, Operand parameter, Operand value ) => vm.Effect( Opcode.SETOBJPARAM, new[] { slot, parameter, value } );

		[OpcodeHandler( Opcode.EVENT, RideOpcodeStatus.Hooked, "TPW-EXE 0x552615 (`type key id`, each resolved): the dispatcher 0x5573D0(type, key, id, control 1000), its handle dropped; types 1-2 are particle effects (PARTICLES.md), 3-9 sounds by category (AUDIO.md), 10 the object type's own sound list (0x557686); the corpus uses types 1-6 and key 0xFFFF (-1, the footprint middle) 341 times" )]
		public static void Event( RideVM vm, Operand type, Operand unknown, Operand id ) => vm.Effect( Opcode.EVENT, new[] { type, unknown, id } );

		[OpcodeHandler( Opcode.EVENT_EXT, RideOpcodeStatus.Hooked, "TPW-EXE 0x552707 (`type key id control`, each resolved): EVENT with the control operand in place of 1000 (the dispatcher 0x5573D0 applies a control other than 1000 to a particle effect of types 1-2 through 0x51FFD0; the sound types ignore it); no corpus use" )]
		public static void EventExt( RideVM vm, Operand type, Operand unknown, Operand id, Operand control ) => vm.Effect( Opcode.EVENT_EXT, new[] { type, unknown, id, control } );

		[OpcodeHandler( Opcode.SETREVERB, RideOpcodeStatus.Hooked, "TPW-EXE 0x55649C (`level`, resolved): stores the operand as a dword at +0xD8 and nothing else; the reader is the main loop's 0x55A470, which sets the sound service's reverb preset (0x51C8D0, an index into SndReverb.map) from the root script of the ride being ridden whose level is not 0 (built: ReverbSelector; the mixed reverb is OpenTPW's stand-in by design, EXT:audio-reverb); the corpus uses 0, 1, 3, 8 and 9" )]
		public static void SetReverb( RideVM vm, Operand level )
		{
			vm.ReverbLevel = level.Value;
			vm.Effect( Opcode.SETREVERB, new[] { level } );
		}

		[OpcodeHandler( Opcode.DIPMUSIC, RideOpcodeStatus.Hooked, "TPW-EXE 0x5564C0 (`on`, resolved): stores the low byte at +0xB9 and calls 0x51E710(on), which writes the global duck request word 0x803AD0 (and the sound service's dirty flag 0x803AC8); the sound service (0x51BD70, from the main loop) then sets the music channel's volume to 0 while the word is non-zero (0x51BE5A..0x51BE9B, 0x51BDC2..0x51BDEC; built: AudioMixer.MusicDuckRequest); the script's destruction writes 0 again when the byte was set (0x558513); End.RSE and Gates.RSE use it (8 uses)" )]
		public static void DipMusic( RideVM vm, Operand value )
		{
			vm.MusicDuckByte = value.Value & 0xFF;
			vm.Effect( Opcode.DIPMUSIC, new[] { value } );
		}

		[OpcodeHandler( Opcode.SCREAMLEVEL, RideOpcodeStatus.Hooked, "TPW-EXE 0x555FDA (`level`, resolved): 0x551290(handle +0xD0, level, script speed word) sets the held voice's parameter 6 to clamp((level + speed) / 2, 0, 100), truncating toward zero, and does nothing without a handle (built; 10-100 occur)" )]
		public static void ScreamLevel( RideVM vm, Operand level ) => vm.Effect( Opcode.SCREAMLEVEL, new[] { level } );

		[OpcodeHandler( Opcode.STARTSCREAM, RideOpcodeStatus.Hooked, "TPW-EXE 0x555E5E (`band level`, resolved): 0x551130(handle +0xD0, band, level, script speed word, object position in whole units) gives the handle (kept at +0xD0 and in the flags); band 0 plays nothing, a start while a handle is held is refused; all 40 corpus uses pass VAR_ONRIDE as the band" )]
		public static void StartScream( RideVM vm, Operand visitor, Operand unknown ) => vm.Effect( Opcode.STARTSCREAM, new[] { visitor, unknown } );

		[OpcodeHandler( Opcode.STOPSCREAM, RideOpcodeStatus.Hooked, "TPW-EXE 0x555EF7 (no operands): with a handle (+0xD0) calls 0x51C300(handle), then clears the handle" )]
		public static void StopScream( RideVM vm ) => vm.Effect( Opcode.STOPSCREAM, Array.Empty<Operand>() );

		[OpcodeHandler( Opcode.SINGLESCREAM, RideOpcodeStatus.Hooked, "TPW-EXE 0x555F1B (`band level`, resolved): a one-shot at the object's position (0x556AF0): level below 0 (the corpus's 0xFFFF, 44 of 46 uses) calls 0x551560, else 0x551320(band, level, script speed word); no handle kept" )]
		public static void SingleScream( RideVM vm, Operand visitor, Operand unknown ) => vm.Effect( Opcode.SINGLESCREAM, new[] { visitor, unknown } );

		[OpcodeHandler( Opcode.REPAIREFFECT, RideOpcodeStatus.Hooked, "TPW-EXE 0x5562A6 (`on`, resolved): 0x556DE0(on) switches the type's repair effect (non-zero starts it, 0 ends it); then, from the compare at 0x5562C5, a non-zero value plays events 0x6F and 0xD3 of the global ride category at the object and 0 jumps to 0x55635F: with more than five variables 0x551320(variable 5, 1, 1, position), a band one-shot (silent for band 0)" )]
		public static void RepairEffect( RideVM vm, Operand value ) => vm.Effect( Opcode.REPAIREFFECT, new[] { value } );

		// [BIN:TPW-EXE:0x5564ED SPARK] four resolved operands: the first two are stored as words at +0xDC and +0xDE, the key nodes they name (class 0x100, 0x556B90) are looked up and logged (0x5DA3C0); the third and fourth are read and dropped. Nothing reads the two words (the other +0xDC and +0xDE readers belong to the tour and bump controllers), so the opcode has no effect
		[OpcodeHandler( Opcode.SPARK, RideOpcodeStatus.Implemented, "TPW-EXE 0x5564ED (`a b c d`, resolved): stores a and b as words at +0xDC and +0xDE, looks the two key nodes (class 0x100) up and logs them; c and d are dropped; nothing reads the words, so no game effect (1 corpus use, Plasma)" )]
		public static void Spark( RideVM vm, Operand a, Operand b, Operand c, Operand d ) { }

		// [EXT:object-lights] the four light opcodes act on the light dummies (class 0x20000) of an OpenTPW object's own model (docs/LIGHTS.md): no shipped model has one and the original's instance builder never creates the light (0x44A870), so the original would fault where a model has none; here a missing light is a logged no-op
		[OpcodeHandler( Opcode.ENABLELIGHT, RideOpcodeStatus.Hooked, "TPW-EXE 0x555C7A (`light`, resolved): looks the light dummy (class 0x20000, number `light`) up in the object's model (0x44B220), takes its light record (0x4587B0) and clears the record's off flag 0x10000000 (0x458910); flags untouched; no corpus use, no light in any shipped model; OpenTPW switches the light of the object's own light dummy, and a missing light is a logged no-op (EXT:object-lights, docs/LIGHTS.md)" )]
		public static void EnableLight( RideVM vm, Operand light ) => vm.Effect( Opcode.ENABLELIGHT, new[] { light } );

		[OpcodeHandler( Opcode.DISABLELIGHT, RideOpcodeStatus.Hooked, "TPW-EXE 0x555CCD (`light`, resolved): as ENABLELIGHT but sets the record's off flag 0x10000000 (0x458940); flags untouched; no corpus use; OpenTPW: EXT:object-lights" )]
		public static void DisableLight( RideVM vm, Operand light ) => vm.Effect( Opcode.DISABLELIGHT, new[] { light } );

		[OpcodeHandler( Opcode.SETLIGHT, RideOpcodeStatus.Hooked, "TPW-EXE 0x555D20 (`light level`, resolved): the light record's colour becomes the light's base colour times level x 0.01 (0x4587E0: red, green and blue, alpha 1.0, and the packed ARGB word); flags untouched; no corpus use; OpenTPW: EXT:object-lights" )]
		public static void SetLight( RideVM vm, Operand light, Operand level ) => vm.Effect( Opcode.SETLIGHT, new[] { light, level } );

		[OpcodeHandler( Opcode.COLOURLIGHT, RideOpcodeStatus.Hooked, "TPW-EXE 0x555D9A (`light red green blue`, resolved): each channel times 0.01 becomes the light record's colour (alpha 1.0, packed ARGB word with each channel times 255 truncated, 0x458890); flags untouched; no corpus use; OpenTPW: EXT:object-lights" )]
		public static void ColourLight( RideVM vm, Operand light, Operand red, Operand green, Operand blue ) => vm.Effect( Opcode.COLOURLIGHT, new[] { light, red, green, blue } );

		// [BIN:TPW-EXE:0x5563D5 GETCUSTPTCLCODE] reads its destination raw, resolves the second operand and drops it, then stores the constant 0 (the interpreter's zero register) in the result word and the variable destination; the object type's custom particle code (0x4145E0 reads another field, the sound list EVENT type 10 uses) is not consulted
		[OpcodeHandler( Opcode.GETCUSTPTCLCODE, RideOpcodeStatus.Implemented, "TPW-EXE 0x5563D5 (`dest in`): the second operand is resolved and dropped, the result is the constant 0 in the flags and a variable destination (a stub in the original); no corpus use" )]
		public static void GetCustomParticleCode( RideVM vm, Operand dest, Operand input )
		{
			vm.SetFlags( 0 );
			dest.Value = 0;
		}

		[OpcodeHandler( Opcode.TURBO, RideOpcodeStatus.Implemented, "TPW-EXE 0x5542B9: stores the low byte of the raw operand word in the script's TURBO byte (a variable operand stores its index byte); nothing else" )]
		public static void Turbo( RideVM vm, Operand value ) => vm.TurboByte = value.Raw & 0xFF;

		[OpcodeHandler( Opcode.HOUR, RideOpcodeStatus.Hooked, "host-local civil hour: tp.exe 0x5566AC calls time/localtime and stores tm_hour in the result register; a variable operand receives it, a literal only sets the flags; the TPW runtime samples host time (PPC-clock.md)" )]
		public static void Hour( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.HOUR, dest, dest );

		[OpcodeHandler( Opcode.MIN, RideOpcodeStatus.Hooked, "host-local civil minute (tm_min), as HOUR: tp.exe 0x5566FF calls time/localtime and stores tm_min in the result register; corpus `MIN 0; BRANCH_NZ` uses a literal dest for flags only" )]
		public static void Minute( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.MIN, dest, dest );

		[OpcodeHandler( Opcode.SEC, RideOpcodeStatus.Hooked, "host-local civil second (tm_sec), as HOUR: tp.exe 0x55674E calls time/localtime and stores tm_sec in the result register; corpus `SEC 0; BRANCH_NZ`" )]
		public static void Second( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.SEC, dest, dest );

		[OpcodeHandler( Opcode.YEAR, RideOpcodeStatus.Hooked, "host-local civil year minus 1900 (tm_year; tp.exe 0x5565B2 and PPC-clock.md); TPW runtime samples host time" )]
		public static void Year( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.YEAR, dest, dest );

		[OpcodeHandler( Opcode.MONTH, RideOpcodeStatus.Hooked, "host-local civil month 1..12 (tm_mon + 1; tp.exe 0x556605 and PPC-clock.md); TPW runtime samples host time" )]
		public static void Month( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.MONTH, dest, dest );

		[OpcodeHandler( Opcode.DAY, RideOpcodeStatus.Hooked, "host-local civil day 1..31 (tm_mday; tp.exe 0x556659 and PPC-clock.md); TPW runtime samples host time" )]
		public static void Day( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.DAY, dest, dest );
	}
}
