using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The shipped ride scripts' object records (ADDOBJ, KILLOBJ, FADEOBJ, SETOBJPARAM) against the real sound categories: none of them is an unimplemented effect any more.</summary>
[TestClass]
[DoNotParallelize]
public class HeldSoundCorpusTests
{
	private readonly Dictionary<FieldInfo, object?> saved = new();
	private BaseFileSystem? previousFileSystem;
	private string? previousBonus;
	private AudioMixer? mixer;

	[TestInitialize]
	public void Setup()
	{
		previousFileSystem = FileSystem;
		previousBonus = ObjectCatalog.BonusDataRoot;
		Log ??= new();
		var data = StringTableTests.OriginalDataDirectory();
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		ObjectCatalog.BonusDataRoot = null;
		mixer = new AudioMixer( null );
		SetField( "events", new SoundEventSystem( mixer, seed: 7 ) );
		foreach ( var (property, category) in new[] { ("Rides", "cat_rides"), ("Ambient", "cat_ambient"), ("Kids", "cat_kids"), ("Staff", "cat_staff"), ("Ui", "cat_ui") } )
			SetField( $"<{property}>k__BackingField", Load( "/global/sound", category ) );
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
	}

	private SoundCategory Load( string directory, string category )
	{
		var loaded = SoundCategory.TryLoad( FileSystem, directory, category );
		Assert.IsNotNull( loaded, $"{directory} {category}" );
		return loaded!;
	}

	private void SetField( string name, object? value )
	{
		var field = typeof( GameAudio ).GetField( name, BindingFlags.NonPublic | BindingFlags.Static )!;
		if ( !saved.ContainsKey( field ) )
			saved.Add( field, field.GetValue( null ) );
		field.SetValue( null, value );
	}

	[TestCleanup]
	public void Cleanup()
	{
		mixer?.Dispose();
		foreach ( var (field, value) in saved )
			field.SetValue( null, value );
		FileSystem = previousFileSystem!;
		ObjectCatalog.BonusDataRoot = previousBonus;
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
	}

	[TestMethod]
	public void EveryShippedObjectRecordOpcodeIsServed()
	{
		var served = new Dictionary<Opcode, int>();
		var soundAdds = 0;
		var kept = 0;
		var checkedScripts = 0;
		foreach ( var level in new[] { "jungle", "space", "fantasy", "hallow" } )
		{
			SetField( "<LevelRides>k__BackingField", Load( $"/levels/{level}/Sound", "cat_rides" ) );
			SetField( "<LevelAmbient>k__BackingField", Load( $"/levels/{level}/Sound", "cat_ambient" ) );
			foreach ( var item in ObjectCatalog.Load( level ).Entries.Where( item => item.ScriptPath != null ) )
			foreach ( var scriptPath in item.Scripts.Prepend( item.ScriptPath! ).Distinct( StringComparer.OrdinalIgnoreCase ) )
			{
				RideScriptFile script;
				try
				{
					using var original = FileSystem.OpenRead( scriptPath );
					script = new RideScriptFile( original );
				}
				catch ( InvalidDataException ) { continue; }
				var wanted = new[] { Opcode.ADDOBJ, Opcode.ADDOBJ_EXT, Opcode.KILLOBJ, Opcode.FADEOBJ, Opcode.SETOBJPARAM };
				var owner = new OriginalObjectRuntime( item, seed: 1 );
				try
				{
					checkedScripts++;
					foreach ( var instruction in script.Instructions.Where( instruction => wanted.Contains( (Opcode)instruction.Opcode ) ) )
					{
						var opcode = (Opcode)instruction.Opcode;
						served[opcode] = served.GetValueOrDefault( opcode ) + 1;
						var words = new[] { 0x8000_0000u | instruction.Opcode }
							.Concat( instruction.Operands.Select( operand => ((uint)operand.Kind << 16) | operand.Value ) )
							.Append( 0x8000_0000u | (uint)Opcode.ENDSLICE ).ToArray();
						using var injected = new MemoryStream( RseScriptTests.CreateScript( words, Array.Empty<string>(), script.VariableNames.ToArray() ) );
						var vm = new RideVM( injected, new RideVMOptions { Effects = owner.Script!.Effects, Seed = 1 } );
						var before = owner.HeldSounds.Count;
						vm.Advance( FixedStepClock.TickDuration );
						Assert.AreNotEqual( RideVMState.Faulted, vm.State, $"{scriptPath} {opcode}" );
						Assert.AreEqual( 0, vm.UnimplementedEffects.Count, $"{scriptPath}: {string.Join( ", ", vm.UnimplementedEffects )}" );
						var type = instruction.Operands[0];
						if ( opcode is Opcode.ADDOBJ or Opcode.ADDOBJ_EXT && type.Kind == RideScriptOperandKind.Literal && type.Value is >= 3 and <= 9 )
						{
							soundAdds++;
							if ( owner.HeldSounds.Count > before )
								kept++;
						}
						vm.Stop();
						Assert.AreEqual( before, owner.HeldSounds.Count, "stopping the script ends the records it added" );
					}
				}
				finally { owner.Stop(); }
			}
		}
		Console.WriteLine( $"Object record instructions served in {checkedScripts} scripts: {string.Join( ", ", served.OrderBy( pair => pair.Key ).Select( pair => $"{pair.Key} {pair.Value}" ) )}; sound ADDOBJ {soundAdds}, {kept} kept a sound." );
		Assert.IsTrue( soundAdds >= 330, $"the corpus holds the sound ADDOBJ types 3-9 (334 in the shipped scripts), found {soundAdds}" );
		Assert.IsTrue( kept >= soundAdds / 2, $"most of them resolve their event in the loaded categories: {kept} of {soundAdds}" );
		Assert.IsTrue( served.GetValueOrDefault( Opcode.FADEOBJ ) > 100 && served.GetValueOrDefault( Opcode.KILLOBJ ) > 100 && served.GetValueOrDefault( Opcode.SETOBJPARAM ) >= 4 );
	}
}
