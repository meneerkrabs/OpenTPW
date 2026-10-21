using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The park build's long steps split into smaller ones (docs/BROWSER-PERFORMANCE.md, "Build steps"): the economy's settings and catalogues, the
/// gate sign's composition, the ride script handlers and the advisor's message tables. Each split must make what the work made at once.
/// </summary>
[TestClass, DoNotParallelize]
public class BuildStepsTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	[TestMethod]
	public void TheEconomyMadeInStepsIsTheEconomyMadeAtOnce()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "Original levels/Standard.sam is missing." );
		var original = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		try
		{
			foreach ( var kind in new[] { ParkStartKind.FullSimulation, ParkStartKind.InstantAction } )
			{
				var park = OriginalPark.Load( "jungle", readShippedSave: ParkStart.ReadsShippedSave( kind ) );
				var atOnce = ParkEconomyRuntime.ForOriginalLevel( park, kind );
				var steps = 0;
				ParkEconomyRuntime? stepped = null;
				foreach ( var created in ParkEconomyRuntime.ForOriginalLevelSteps( park, kind ) )
				{
					if ( created == null )
						steps++;
					else
						stepped = created;
				}
				Assert.IsNotNull( stepped );
				Assert.IsTrue( steps > 20, $"{kind}: the economy takes {steps} steps, one per object archive and more" );
				Assert.AreEqual( ParkSaveFile.Serialize( atOnce.Economy ), ParkSaveFile.Serialize( stepped!.Economy ), $"{kind}: the same economy" );
				Assert.AreEqual( atOnce.Start, stepped.Start );
				CollectionAssert.AreEqual( atOnce.Import?.Evidence.ToArray() ?? Array.Empty<string>(), stepped.Import?.Evidence.ToArray() ?? Array.Empty<string>() );
				Assert.AreEqual( atOnce.Economy.Catalog.Objects.Count, stepped.Economy.Catalog.Objects.Count );
			}
		}
		finally
		{
			FileSystem = original;
		}
	}

	[TestMethod]
	public void SignsComposedInStepsAndInterleavedAreTheSignsComposedAtOnce()
	{
		var data = StringTableTests.OriginalDataDirectory();
		using var stream = File.OpenRead( Path.Combine( data, "fonts.wad" ) );
		var fonts = SignFontLibrary.Load( stream );
		// the jungle gate and a sign of each other kind: their steps alternate, so no state may pass from one composition to another between steps
		var signs = SignFileTests.OriginalSignMembers( data ).Where( member => member.Path.Contains( "gates", StringComparison.OrdinalIgnoreCase ) ).Take( 2 )
			.Concat( SignFileTests.OriginalSignMembers( data ).Take( 2 ) ).Select( member => new SignFile( member.Data ) ).ToList();
		Assert.IsTrue( signs.Count >= 3 );
		var lines = new[] { "Theme Park", "Wereld" };
		var atOnce = signs.Select( sign => SignSurfaceComposer.Compose( sign, fonts, lines, new List<string>() ) ).ToList();
		var compositions = signs.Select( _ => new SignSurfaceComposer.Composition() ).ToList();
		var steps = signs.Select( ( sign, index ) => SignSurfaceComposer.ComposeSteps( sign, fonts, lines, new List<string>(), compositions[index] ).GetEnumerator() ).ToList();
		var counts = new int[signs.Count];
		for ( var more = true; more; )
		{
			more = false;
			for ( var index = 0; index < steps.Count; index++ )
				if ( steps[index].MoveNext() )
				{
					counts[index]++;
					more = true;
				}
		}
		for ( var index = 0; index < signs.Count; index++ )
		{
			CollectionAssert.AreEqual( atOnce[index].Left, compositions[index].Result.Left, $"sign {index}: left half" );
			CollectionAssert.AreEqual( atOnce[index].Right, compositions[index].Result.Right, $"sign {index}: right half" );
			Assert.AreEqual( atOnce[index].TextPixelCount, compositions[index].Result.TextPixelCount );
		}
		Assert.IsTrue( counts.Max() >= 8, $"a sign with text takes several steps ({string.Join( ", ", counts )})" );
	}

	[TestMethod]
	public void TheOpcodeTableBuiltInStepsIsTheTableBuiltAtOnce()
	{
		// the table as it was built at once: every OpcodeHandler method, in the assembly's order
		var expected = typeof( RideOpcodes ).Assembly.GetTypes().SelectMany( type => type.GetMethods( BindingFlags.Public | BindingFlags.Static ) )
			.Select( method => (Method: method, Attribute: method.GetCustomAttribute<OpcodeHandlerAttribute>()) ).Where( item => item.Attribute != null )
			.Select( item => (item.Attribute!.Opcode, item.Attribute.Status, item.Attribute.Evidence, Operands: item.Method.GetParameters().Length - 1) ).ToList();
		var builder = new RideOpcodes.TableBuilder();
		var steps = 0;
		while ( builder.Step() )
			steps++;
		Assert.IsTrue( steps > 5, $"the table takes {steps} steps" );
		Assert.IsFalse( builder.Step() );
		var built = builder.Result!.Values.Select( definition => (definition.Opcode, definition.Status, definition.Evidence, Operands: definition.OperandCount) ).ToList();
		CollectionAssert.AreEqual( expected, built );
		CollectionAssert.AreEqual( RideOpcodes.Definitions.Keys.ToList(), builder.Result.Keys.ToList(), "the shared table has the same order" );
		Assert.IsFalse( RideOpcodes.PrepareStep(), "a built table needs no step" );
	}

	[TestMethod]
	public void TheUnproducedMessagesAreTheDescriptorsNoMessageUses()
	{
		var produced = AdvisorTables.AllMessages.ToList();
		var expected = AdvisorDescriptorTable.Rows.Where( row => !produced.Any( message => message.MessageId == row.MessageId ) ).Select( row => row.MessageId ).ToList();
		CollectionAssert.AreEqual( expected, AdvisorTables.UnproducedMessages.Select( message => message.MessageId ).ToList() );
		Assert.AreEqual( 4, AdvisorTables.WarmSteps().Count() );
	}
}
