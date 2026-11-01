using System.Linq;
using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>SAVE-013: the saved sprite scripts (tp.exe 0x475730) read back into the guest, staff and balloon sprite state.</summary>
[TestClass]
public class SavedSpriteScriptsTests
{
	private const uint SavedClock = 5000;

	private static byte[] Record( int slot, uint counter, uint entry, uint state = 2, uint wake = SavedClock + 20, uint period = 101, Action<byte[]>? edit = null )
	{
		var body = new byte[SaveScriptRecord.Bytes];
		void Put( int offset, uint value ) => BitConverter.TryWriteBytes( body.AsSpan( offset ), value );
		Put( 4, (uint)slot );
		Put( 8, counter );
		Put( 0x0C, entry );
		Put( 0x10, counter );
		Put( 0x14, SaveScriptRecord.ProgramTable );
		Put( 0x18, state );
		Put( 0x1C, 20 );
		Put( 0x7C, wake );
		Put( 0x80, period );
		edit?.Invoke( body );
		return body;
	}

	private static SavedSpriteScripts Saved( params byte[][] records )
	{
		var slots = new uint[100];
		foreach ( var record in records )
			slots[BitConverter.ToUInt32( record, 4 )] = 1;
		var bytes = SaveEngineBlocksTests.ScriptsBlock( slots, records );
		return new SavedSpriteScripts( SaveScriptsBlock.Parse( bytes, 0 ), SavedClock );
	}

	[TestMethod]
	public void TheProgramWordsOfTheStaffProgramsRunToTheNextProgramPlusOnePaddingWord()
	{
		// every saved program is followed by one unused word, so the widths of the decoded instructions must end one word before the next program's start
		var chain = new[] { StaffSpriteProgram.Walk, StaffSpriteProgram.Run, StaffSpriteProgram.Stand };
		for ( var index = 0; index + 1 < chain.Length; index++ )
			Assert.AreEqual( SpriteProgramWords.StartOf( chain[index + 1] ), EndOf( chain[index] ) + 1, chain[index].ToString() );
		var later = new[] { StaffSpriteProgram.Dangling, StaffSpriteProgram.Sweep, StaffSpriteProgram.Repair, StaffSpriteProgram.State6, StaffSpriteProgram.Strike, StaffSpriteProgram.Catch, StaffSpriteProgram.Hold, StaffSpriteProgram.Template24 };
		for ( var index = 0; index + 1 < later.Length; index++ )
			Assert.AreEqual( SpriteProgramWords.StartOf( later[index + 1] ), EndOf( later[index] ) + 1, later[index].ToString() );
	}

	private static int EndOf( StaffSpriteProgram program )
	{
		var word = SpriteProgramWords.StartOf( program );
		for ( var index = 0; StaffSpritePrograms.Builtin.TryRead( new StaffSpritePc( program, index ), out var instruction ); index++ )
			word += SpriteProgramWords.Width( instruction.Op );
		return word;
	}

	[TestMethod]
	public void TheCorpusGuestProgramPositionsMap()
	{
		// the shapes every guest record of the corpus has: a stand on its JMP (the counter of the next run), a walk on its next-to-last or last frame (the counter is the instruction that runs next)
		Assert.AreEqual( (VisitorSpriteTemplate.Stand, 0), SpriteProgramWords.VisitorPosition( 97 ) );
		Assert.AreEqual( (VisitorSpriteTemplate.Walk, 7), SpriteProgramWords.VisitorPosition( 61 ) );
		Assert.AreEqual( (VisitorSpriteTemplate.Walk, 0), SpriteProgramWords.VisitorPosition( 63 ) );
		Assert.AreEqual( (VisitorSpriteTemplate.Walk, 6), SpriteProgramWords.VisitorPosition( 59 ) );
		Assert.AreEqual( VisitorSpriteTemplate.Stand, SpriteProgramWords.VisitorTemplateAt( 90 ) );
		Assert.AreEqual( VisitorSpriteTemplate.Walk, SpriteProgramWords.VisitorTemplateAt( 42 ) );
		Assert.IsNull( SpriteProgramWords.VisitorPosition( 5 ) );
	}

	[TestMethod]
	public void AGuestRecordBecomesTheBodyPlayerWithItsWakeTimeRebased()
	{
		var saved = Saved( Record( 3, 97, 90, wake: SavedClock - 7, period: 59, edit: body =>
		{
			BitConverter.TryWriteBytes( body.AsSpan( 0x114 ), 1u );
			BitConverter.TryWriteBytes( body.AsSpan( 0xB4 ), 0u );
			BitConverter.TryWriteBytes( body.AsSpan( 0xB8 ), 0u );
		} ) );
		var snapshot = saved.Visitor( 3, 0, 0, 100_000, out var gap );
		Assert.IsNotNull( snapshot, gap );
		var player = snapshot.Player!;
		Assert.AreEqual( VisitorSpriteTemplate.Stand, player.ActiveTemplate );
		Assert.AreEqual( 59, player.Interval );
		Assert.AreEqual( 100_000 - 7, player.NextDue );
		Assert.AreEqual( new VisitorSpriteFrame( 0, 0 ), player.Output );
	}

	[TestMethod]
	public void AGuestWalkOnItsLastFrameKeepsTheFrameIndex()
	{
		var saved = Saved( Record( 4, 59, 42, period: 76, edit: body =>
		{
			BitConverter.TryWriteBytes( body.AsSpan( 0x114 ), 1u );
			BitConverter.TryWriteBytes( body.AsSpan( 0xB4 ), 1u );
			BitConverter.TryWriteBytes( body.AsSpan( 0xB8 ), 5u );
		} ) );
		var snapshot = saved.Visitor( 4, 0, 0, 0, out var gap );
		Assert.IsNotNull( snapshot, gap );
		Assert.AreEqual( 6, snapshot.Player!.NextFrame );
		Assert.AreEqual( new VisitorSpriteFrame( 1, 5 ), snapshot.Player.Output );
		Assert.AreEqual( 20, snapshot.Player.NextDue );
	}

	[TestMethod]
	public void ARecordTheGuestPlayerCannotRepresentIsReportedNotGuessed()
	{
		var saved = Saved( Record( 3, 11, 90 ), Record( 5, 97, 90, period: 0 ) );
		Assert.IsNull( saved.Visitor( 3, 0, 0, 0, out var gap ) );
		StringAssert.Contains( gap, "guest program position" );
		Assert.IsNull( saved.Visitor( 5, 0, 0, 0, out gap ) );
		Assert.IsNull( saved.Visitor( 9, 0, 0, 0, out gap ) );
		StringAssert.Contains( gap, "no saved record" );
		Assert.IsNull( saved.Visitor( 0, 0, 0, 0, out _ ) );
	}

	[TestMethod]
	public void AStaffRecordOnTheWalkProgramBecomesASource()
	{
		var start = (uint)SpriteProgramWords.StartOf( StaffSpriteProgram.Walk );
		var saved = Saved( Record( 7, start, start, state: 1, period: 40 ) );
		var snapshot = saved.Staff( 7, 0, 0, 0, out var gap );
		Assert.IsNotNull( snapshot, gap );
		Assert.AreEqual( new StaffSpritePc( StaffSpriteProgram.Walk, 0 ).Word, snapshot.Source!.CurrentPc );
		Assert.AreEqual( 20, snapshot.Source.Due );
		Assert.AreEqual( 40, snapshot.Source.Interval );
		Assert.AreEqual( 20, snapshot.Source.StackPointer );
	}

	[TestMethod]
	public void AStaffRecordInTheMiddleOfAnInstructionIsRefused()
	{
		var start = (uint)SpriteProgramWords.StartOf( StaffSpriteProgram.Walk );
		var saved = Saved( Record( 7, start + 1, start ) );
		Assert.IsNull( saved.Staff( 7, 0, 0, 0, out var gap ) );
		StringAssert.Contains( gap, "staff program position" );
	}

	[TestMethod]
	public void AHeldBalloonRecordKeepsItsDeadline()
	{
		var held = (uint)SpriteProgramWords.BalloonHeld;
		var saved = Saved( Record( 8, held, held, state: 2, wake: SavedClock + 30, period: 62, edit: body => BitConverter.TryWriteBytes( body.AsSpan( 0x114 ), 1u ) ) );
		var balloon = saved.Balloon( 8, 12, 1000, 4, out var gap );
		Assert.IsNotNull( balloon, gap );
		Assert.AreEqual( 1030, balloon.Deadline );
		Assert.IsTrue( balloon.Visible );
		Assert.AreEqual( 12, balloon.OwnerId );
		Assert.AreEqual( 4, balloon.Identity );
	}

	[TestMethod]
	public void ABalloonRecordOnAnotherProgramIsNotAHeldBalloon()
	{
		var saved = Saved( Record( 8, 97, 90 ) );
		Assert.IsNull( saved.Balloon( 8, 12, 1000, 4, out var gap ) );
		StringAssert.Contains( gap, "held balloon" );
	}

	[TestMethod]
	public void TheWakeTickRebaseIsTheSignedDistanceFromTheSavedClock()
	{
		var saved = Saved();
		Assert.AreEqual( 100, saved.Rebase( SavedClock, 100 ) );
		Assert.AreEqual( 93, saved.Rebase( SavedClock - 7, 100 ) );
		Assert.AreEqual( unchecked( int.MaxValue + 5 ), saved.Rebase( SavedClock + 5, int.MaxValue ) );
	}

	[TestMethod]
	public void EveryRecordOfTheCorpusSavesIsAGuestBodyThatMaps()
	{
		var folder = Environment.GetEnvironmentVariable( "OPENTPW_TPWS_SAVES" );
		if ( string.IsNullOrWhiteSpace( folder ) )
			Assert.Inconclusive( "OPENTPW_TPWS_SAVES is not set." );
		var mapped = 0;
		foreach ( var path in System.IO.Directory.EnumerateFiles( folder!, "*", System.IO.SearchOption.AllDirectories ).Where( file => System.IO.Path.GetExtension( file ).ToUpperInvariant() is ".TPWS" or ".INTS" ).OrderBy( file => file, StringComparer.Ordinal ) )
		{
			using var reader = new SaveReader( path );
			var payload = reader.ReadFile();
			var layout = SavePayloadLayout.Parse( payload );
			var scripts = SaveScriptsBlock.Parse( payload, layout );
			var clock = SaveRuntimeBlocks.Read( payload, layout ).ClockWords[0];
			var saved = new SavedSpriteScripts( scripts, clock );
			foreach ( var record in scripts.Records )
			{
				var snapshot = saved.Visitor( (uint)record.Slot, 0, 0, 1_000_000, out var gap );
				Assert.IsNotNull( snapshot, $"{path} slot {record.Slot}: {gap}" );
				mapped++;
			}
		}
		Console.WriteLine( $"{mapped} saved guest sprite records map" );
	}
}
