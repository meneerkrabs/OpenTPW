using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class RseScriptTests
{
	private const uint Op = 0x8000_0000;
	private const uint Str = 0x1000_0000;
	private const uint Branch = 0x2000_0000;
	private const uint Var = 0x4000_0000;

	// NAME "Test"; COPY VAR_A 0xFFFF; .label_4: TEST VAR_A; BRANCH_Z label_4; BRANCH label_0
	private static readonly uint[] SampleWords =
	{
		Op | 37, Str | 0,
		Op | 3, Var | 0, 0xFFFF,
		Op | 38, Var | 1,
		Op | 32, Branch | 5,
		Op | 31, Branch | 0
	};

	private static byte[] CreateScript( uint[] words, string[] strings, string[] variables, int? declaredVariables = null, uint? declaredWords = null )
	{
		var output = new MemoryStream();
		var writer = new BinaryWriter( output );
		writer.Write( new byte[] { (byte)'R', (byte)'S', (byte)'S', (byte)'E', (byte)'Q', 0x0F, 0x01, 0x00 } );
		writer.Write( declaredVariables ?? variables.Length );
		writer.Write( 4 ); // stack
		writer.Write( 50 ); // time slice
		writer.Write( 2 ); // limbo
		writer.Write( 3 ); // bounce
		writer.Write( 5 ); // walk
		writer.Write( "Pad Pad Pad Pad "u8.ToArray() );
		writer.Write( declaredWords ?? (uint)words.Length );
		foreach ( var word in words )
			writer.Write( word );
		var blob = strings.SelectMany( x => Encoding.ASCII.GetBytes( x + "\0" ) ).ToArray();
		writer.Write( blob.Length );
		writer.Write( blob );
		foreach ( var name in variables )
		{
			writer.Write( name.Length + 1 );
			writer.Write( Encoding.ASCII.GetBytes( name + "\0" ) );
		}
		return output.ToArray();
	}

	private static byte[] Sample() => CreateScript( SampleWords, new[] { "Test", "Child.rse" }, new[] { "VAR_A", "VAR_B" } );

	private static void AssertRejected( byte[] data ) => Assert.ThrowsException<InvalidDataException>( () => new RideScriptFile( new MemoryStream( data ) ) );

	[TestMethod]
	public void ParsesHeaderInstructionsStringsVariablesAndBranches()
	{
		using var stream = new MemoryStream( Sample() );
		var script = new RideScriptFile( stream );
		Assert.IsTrue( stream.CanRead );
		Assert.AreEqual( 2, script.VariableCount );
		Assert.AreEqual( 4, script.StackSize );
		Assert.AreEqual( 50, script.TimeSlice );
		Assert.AreEqual( 2, script.LimboSize );
		Assert.AreEqual( 3, script.BounceSize );
		Assert.AreEqual( 5, script.WalkSize );
		Assert.AreEqual( 11, script.CodeWordCount );
		CollectionAssert.AreEqual( new[] { "VAR_A", "VAR_B" }, script.VariableNames.ToArray() );
		Assert.AreEqual( "Test", script.Strings[0] );
		Assert.AreEqual( "Child.rse", script.Strings[5] );
		CollectionAssert.AreEqual( new ushort[] { 37, 3, 38, 32, 31 }, script.Instructions.Select( x => x.Opcode ).ToArray() );
		CollectionAssert.AreEqual( new[] { 0, 2, 5, 7, 9 }, script.Instructions.Select( x => x.WordOffset ).ToArray() );
		Assert.AreEqual( new RideScriptOperand( RideScriptOperandKind.Literal, 0xFFFF ), script.Instructions[1].Operands[1] );
		Assert.AreEqual( 2, script.GetInstructionIndexAtWord( 5 ) );
		Assert.AreEqual( -1, script.GetInstructionIndexAtWord( 6 ) );
	}

	[TestMethod]
	public void DisassemblesWithLabelsNamesAndUninterpretedHighLiterals()
	{
		var text = RideScriptAnalysis.Disassemble( new RideScriptFile( new MemoryStream( Sample() ) ) );
		StringAssert.Contains( text, ".label_0\n\tNAME \"Test\"" );
		StringAssert.Contains( text, "\tCOPY VAR_A 0xFFFF\n.label_5\n\tTEST VAR_B\n\tBRANCH_Z label_5\n\tBRANCH label_0" );
		var listing = RideScriptAnalysis.CanonicalListing( new RideScriptFile( new MemoryStream( Sample() ) ) );
		Assert.AreEqual( "0:37 S0\n2:3 V0 L65535\n5:38 V1\n7:32 B5\n9:31 B0\n", listing );
	}

	[TestMethod]
	public void EmptyCodeStringsAndVariablesAreValid()
	{
		var script = new RideScriptFile( new MemoryStream( CreateScript( Array.Empty<uint>(), Array.Empty<string>(), Array.Empty<string>() ) ) );
		Assert.AreEqual( 0, script.Instructions.Count );
		Assert.AreEqual( 0, script.Strings.Count );
		Assert.AreEqual( 0, script.VariableNames.Count );
	}

	[TestMethod]
	public void InventoryReportsUnknownOpcodesAndOperandCountMismatches()
	{
		var words = new uint[] { Op | 999, 1, Op | 999, Op | 44, Op | 3, Var | 0, 7, Op | 31, Branch | 0 };
		var script = new RideScriptFile( new MemoryStream( CreateScript( words, Array.Empty<string>(), new[] { "V" } ) ) );
		var inventory = RideScriptAnalysis.BuildInventory( new[] { script } );
		Assert.AreEqual( 2, inventory.UnknownOpcodes[999] );
		Assert.AreEqual( "OP_999", RideScriptAnalysis.GetOpcodeName( 999 ) );
		CollectionAssert.AreEqual( new[] { "WAIT documented 1, observed 0", "OP_999 documented nothing, observed 0/1" },
			inventory.OperandCountMismatches.OrderBy( x => x.StartsWith( "OP" ) ).ToArray() );
		Assert.AreEqual( "3:1,31:1,44:1,999:2", inventory.HistogramKey );
		Assert.AreEqual( 1, inventory.FinalOpcodes[31] );
	}

	[TestMethod]
	public void CurrentVmHandlerSetIsPinned()
	{
		var expected = new[] { "NOP", "CRIT_LOCK", "COPY", "SETLV", "SUB", "ENDSLICE", "GETTIME", "ADDOBJ", "RAND", "JSR", "RETURN",
			"BRANCH", "BRANCH_Z", "BRANCH_NZ", "BRANCH_NV", "BRANCH_PV", "DBGMSG", "NAME", "TEST", "CMP", "ADD",
			"BOUNCESETNODE", "BOUNCESETBASE", "BOUNCE", "UNBOUNCE", "FORCEUNBOUNCE", "BOUNCING" };
		CollectionAssert.AreEquivalent( expected, RideScriptAnalysis.ImplementedOpcodes.Select( x => x.ToString() ).ToArray() );
		Assert.AreEqual( 106, Enum.GetValues<Opcode>().Length );
		Assert.IsFalse( RideScriptAnalysis.DocumentedOperandCounts.ContainsKey( Opcode.CRIT_UNLOCK ) );
		Assert.AreEqual( 105, RideScriptAnalysis.DocumentedOperandCounts.Count );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 8 )]
	[DataRow( 47 )]
	[DataRow( 51 )]
	[DataRow( 60 )]
	[DataRow( 95 )]
	[DataRow( 100 )]
	public void RejectsTruncatedFile( int length )
	{
		var data = Sample();
		Array.Resize( ref data, length );
		AssertRejected( data );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 5 )]
	[DataRow( 7 )]
	[DataRow( 32 )]
	[DataRow( 47 )]
	public void RejectsBadMagicOrPadding( int offset )
	{
		var data = Sample();
		data[offset] ^= 0x01;
		AssertRejected( data );
	}

	[DataTestMethod]
	[DataRow( -1 )]
	[DataRow( 65537 )]
	[DataRow( 1 )]
	[DataRow( 3 )]
	public void RejectsVariableCountDisagreeingWithNames( int declared )
		=> AssertRejected( CreateScript( SampleWords, new[] { "Test" }, new[] { "VAR_A", "VAR_B" }, declaredVariables: declared ) );

	[DataTestMethod]
	[DataRow( 65537u )]
	[DataRow( uint.MaxValue )]
	[DataRow( 400u )]
	public void RejectsOversizedOrTruncatedCodeWordCount( uint declared )
		=> AssertRejected( CreateScript( SampleWords, new[] { "Test" }, new[] { "VAR_A", "VAR_B" }, declaredWords: declared ) );

	[DataTestMethod]
	[DataRow( 0x8001_0000u )]
	[DataRow( 0x3000_0000u )]
	[DataRow( 0x0001_0000u )]
	[DataRow( 0xC000_0000u )]
	public void RejectsUnknownWordFlags( uint word )
		=> AssertRejected( CreateScript( new[] { Op | 44, word }, Array.Empty<string>(), Array.Empty<string>() ) );

	[TestMethod]
	public void RejectsOperandBeforeFirstOpcode()
		=> AssertRejected( CreateScript( new uint[] { 5, Op | 44, 1 }, Array.Empty<string>(), Array.Empty<string>() ) );

	[DataTestMethod]
	[DataRow( 1u )]
	[DataRow( 4u )]
	[DataRow( 15u )]
	[DataRow( 0xFFFFu )]
	public void RejectsStringOffsetsThatDoNotStartAString( uint offset )
		=> AssertRejected( CreateScript( new[] { Op | 37, Str | offset }, new[] { "Test", "Child.rse" }, Array.Empty<string>() ) );

	[TestMethod]
	public void RejectsStringReferenceWithEmptyBlob()
		=> AssertRejected( CreateScript( new[] { Op | 37, Str }, Array.Empty<string>(), Array.Empty<string>() ) );

	[TestMethod]
	public void RejectsUnterminatedOrOverlongStringBlob()
	{
		var data = CreateScript( new[] { Op | 30 }, new[] { "ab" }, Array.Empty<string>() );
		data[^1] = (byte)'c';
		AssertRejected( data );
		BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 56 ), 4 );
		AssertRejected( data );
	}

	[TestMethod]
	public void RejectsUndeclaredVariableIndex()
		=> AssertRejected( CreateScript( new[] { Op | 38, Var | 2 }, Array.Empty<string>(), new[] { "A", "B" } ) );

	[DataTestMethod]
	[DataRow( 1u )]
	[DataRow( 2u )]
	[DataRow( 3u )]
	[DataRow( 0xFFFFu )]
	public void RejectsBranchTargetsThatAreNotOpcodeWords( uint target )
		=> AssertRejected( CreateScript( new[] { Op | 31, Branch | target }, Array.Empty<string>(), Array.Empty<string>() ) );

	[TestMethod]
	public void RejectsMalformedVariableNames()
	{
		var valid = CreateScript( new[] { Op | 30 }, Array.Empty<string>(), new[] { "AB" } );
		new RideScriptFile( new MemoryStream( valid ) );
		var zeroLength = (byte[])valid.Clone();
		BinaryPrimitives.WriteInt32LittleEndian( zeroLength.AsSpan( 60 ), 0 );
		AssertRejected( zeroLength );
		var overlong = (byte[])valid.Clone();
		BinaryPrimitives.WriteInt32LittleEndian( overlong.AsSpan( 60 ), 4 );
		AssertRejected( overlong );
		var unterminated = (byte[])valid.Clone();
		unterminated[^1] = (byte)'C';
		AssertRejected( unterminated );
		var embeddedNull = (byte[])valid.Clone();
		embeddedNull[^3] = 0;
		AssertRejected( embeddedNull );
		AssertRejected( valid.Concat( new byte[] { 1, 0 } ).ToArray() );
		AssertRejected( valid.Concat( new byte[] { 1, 0, 0, 0, 0 } ).ToArray() );
	}

	[TestMethod]
	public void RejectsOversizedInputWithoutClosingIt()
	{
		using var stream = new MemoryStream( new byte[RideScriptFile.MaximumFileBytes + 1] );
		AssertRejected( stream.ToArray() );
		Assert.ThrowsException<InvalidDataException>( () => new RideScriptFile( stream ) );
		Assert.IsTrue( stream.CanRead );
	}

	[TestMethod]
	public void ReadsNonseekableShortReadsWithoutClosingInput()
	{
		using var stream = new ShortReadStream( Sample() );
		Assert.AreEqual( 5, new RideScriptFile( stream ).Instructions.Count );
		Assert.IsTrue( stream.CanRead );
	}

	private sealed class ShortReadStream : MemoryStream
	{
		public ShortReadStream( byte[] data ) : base( data ) { }
		public override bool CanSeek => false;
		public override long Length => throw new NotSupportedException();
		public override int Read( byte[] readBuffer, int offset, int count ) => base.Read( readBuffer, offset, Math.Min( count, 3 ) );
	}

	// ---- Private original corpus (skipped without OPENTPW_GAME_PATH) ----

	private const string CorpusHistogram = "0:2,1:150,2:240,3:1243,5:74,6:396,7:172,8:644,10:235,11:113,12:20,13:527,15:17,16:74,17:547,18:210,19:133,21:4,23:63,25:1,27:15,28:56,29:70,30:33,31:725,32:787,33:943,34:55,35:84,37:277,38:1318,39:61,42:39,43:39,44:458,46:170,47:541,49:4,50:5,51:20,53:76,54:199,55:144,56:31,57:31,58:24,59:24,60:23,61:6,62:24,63:20,64:28,65:4,66:7,67:9,69:10,70:1,71:4,72:4,73:4,74:8,75:25,76:47,77:53,78:43,79:1,80:1,81:1,86:40,87:80,88:46,89:81,90:5,91:2,92:10,93:140,95:40,96:21,100:1,101:1,102:1,103:21,104:8,105:1";

	[TestMethod]
	public void AllOriginalRseMembersParseWithStableInventory()
	{
		var corpus = LoadCorpus();
		Assert.AreEqual( 308, corpus.Count );
		var inventory = RideScriptAnalysis.BuildInventory( corpus.Values );
		Assert.AreEqual( 30094, inventory.CodeWordCount );
		Assert.AreEqual( 11915, inventory.InstructionCount );
		Assert.AreEqual( 84, inventory.OpcodeHistogram.Count );
		Assert.AreEqual( CorpusHistogram, inventory.HistogramKey );
		Assert.AreEqual( 0, inventory.UnknownOpcodes.Count );
		Assert.AreEqual( 0, inventory.OperandCountMismatches.Count, string.Join( "; ", inventory.OperandCountMismatches ) );
		Assert.AreEqual( 308, inventory.TimeSliceValues[50] );
		Assert.AreEqual( 1, inventory.TimeSliceValues.Count );
		Assert.AreEqual( 294, inventory.FinalOpcodes[(ushort)Opcode.BRANCH] );
		Assert.AreEqual( 14, inventory.FinalOpcodes[(ushort)Opcode.RETURN] );
		Assert.AreEqual( 10549, inventory.OperandKindCounts[RideScriptOperandKind.Literal] );
		Assert.AreEqual( 330, inventory.OperandKindCounts[RideScriptOperandKind.String] );
		Assert.AreEqual( 2664, inventory.OperandKindCounts[RideScriptOperandKind.Branch] );
		Assert.AreEqual( 4636, inventory.OperandKindCounts[RideScriptOperandKind.Variable] );
		CollectionAssert.AreEquivalent( new ushort[] { 37, 63, 64, 90 }, inventory.StringReferences.Keys.ToArray() );
		Assert.AreEqual( 28, inventory.StringReferences[(ushort)Opcode.SPAWNSOUND]["EventMap.rse"] );
		Assert.AreEqual( 163, inventory.EventOperands.Count );
		Assert.AreEqual( 59, inventory.UsedWithoutHandler.Count() );
		CollectionAssert.AreEquivalent( new[] { Opcode.SETLV, Opcode.DBGMSG }, inventory.HandlerWithoutCorpusUse.ToArray() );
		var dump = Environment.GetEnvironmentVariable( "OPENTPW_RSE_INVENTORY_OUT" );
		if ( !string.IsNullOrEmpty( dump ) )
			File.WriteAllText( dump, inventory.Format() );
		var childReferences = 0;
		foreach ( var (name, script) in corpus )
		{
			Assert.AreEqual( script.VariableCount, script.VariableNames.Count, name );
			Assert.IsTrue( RideScriptAnalysis.Disassemble( script ).Length > 0, name );
			var archive = name[..(name.LastIndexOf( '/' ) + 1)];
			foreach ( var instruction in script.Instructions.Where( x => x.Opcode is (ushort)Opcode.SPAWNCHILD or (ushort)Opcode.SPAWNSOUND ) )
			{
				var child = archive + script.Strings[instruction.Operands[0].Value];
				Assert.IsTrue( corpus.ContainsKey( child ), $"{name} references missing {child}" );
				childReferences++;
			}
		}
		Assert.AreEqual( 48, childReferences );
	}

	[DataTestMethod]
	[DataRow( "jungle/rides/totem.wad/Totem.RSE", "5E1AB461C3692C32EAD298DEFB847EB623DB24CC4068CFE9760C53DD70233AAF", 357, 129, 17, "47CF3A92EF866D5D815BE3C5DEF1FE4CC9930EE7C6F6DC2CE005A44F67ECB269", "8:13,23:9,38:13,76:1" )]
	[DataRow( "hallow/rides/bumper.wad/bumper.RSE", "55F4FBB9820E9A4CF401BF1DCA64677B1371CA71C048550197F5033667EB1163", 254, 103, 17, "DA7928EBF4AD705CC73957B3124AFF7BEE2BDC73919F4FA1543CA2D02F722561", "32:15,38:15,54:12,63:1,64:1" )]
	[DataRow( "space/features/plasma.wad/Plasma.RSE", "A253E1A30011EF29FB24D4EF7472954BC42F6F89F67AF2CA4F9A97268F3E5FD8", 25, 8, 1, "2136DD00402C5E241519828C4908C1DADABA46D49637ED5679B9C5E6E4312CB1", "8:1,17:1,105:1" )]
	[DataRow( "hallow/sideshow/arcade.wad/gocatgo.RSE", "A6FF2FE14317D31C681D2C38CF5D44D2840A5E0C993DB2ACE352551A6A796863", 8, 3, 0, "60811902FE255C12CCFA4174C4C4DC7229EF9F30452B1E3AFD929919F700C948", "17:2,31:1" )]
	public void GoldenOriginalScriptsMatchIndependentListingHashes( string member, string fileHash, int words, int instructions, int variables, string listingHash, string opcodeCounts )
	{
		var data = LoadCorpusBytes()[member];
		Assert.AreEqual( fileHash, Convert.ToHexString( SHA256.HashData( data ) ) );
		var script = new RideScriptFile( new MemoryStream( data ) );
		Assert.AreEqual( words, script.CodeWordCount );
		Assert.AreEqual( instructions, script.Instructions.Count );
		Assert.AreEqual( variables, script.VariableCount );
		Assert.AreEqual( listingHash, Convert.ToHexString( SHA256.HashData( Encoding.ASCII.GetBytes( RideScriptAnalysis.CanonicalListing( script ) ) ) ) );
		foreach ( var pair in opcodeCounts.Split( ',' ) )
		{
			var parts = pair.Split( ':' ).Select( int.Parse ).ToArray();
			Assert.AreEqual( parts[1], script.Instructions.Count( x => x.Opcode == parts[0] ), $"{member} opcode {parts[0]}" );
		}
	}

	private static Dictionary<string, RideScriptFile> LoadCorpus()
		=> LoadCorpusBytes().ToDictionary( x => x.Key, x => new RideScriptFile( new MemoryStream( x.Value ) ), StringComparer.OrdinalIgnoreCase );

	private static Dictionary<string, byte[]>? cachedCorpus;

	/// <summary>Key: "theme/folder/archive.wad/member.RSE" relative to Data/levels.</summary>
	private static Dictionary<string, byte[]> LoadCorpusBytes()
	{
		if ( cachedCorpus != null )
			return cachedCorpus;
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original RSE corpus." );
		var dataPath = Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
		var levels = Directory.EnumerateDirectories( dataPath ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "levels", StringComparison.OrdinalIgnoreCase ) );
		if ( levels == null )
			Assert.Inconclusive( "The original levels directory is missing." );
		var corpus = new Dictionary<string, byte[]>( StringComparer.OrdinalIgnoreCase );
		foreach ( var wadPath in Directory.EnumerateFiles( levels!, "*", SearchOption.AllDirectories ).Where( x => x.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) ) )
		{
			using var archive = new WadArchive( wadPath );
			var relative = Path.GetRelativePath( levels!, wadPath ).Replace( Path.DirectorySeparatorChar, '/' );
			Collect( archive.Root, relative, corpus );
		}
		return cachedCorpus = corpus;
	}

	private static void Collect( ArchiveDirectory directory, string prefix, Dictionary<string, byte[]> corpus )
	{
		foreach ( var item in directory.Children )
		{
			if ( item is ArchiveDirectory child )
				Collect( child, $"{prefix}/{child.Name}", corpus );
			else if ( item is ArchiveFile file && file.Name!.EndsWith( ".rse", StringComparison.OrdinalIgnoreCase ) )
				corpus.Add( $"{prefix}/{file.Name}", file.GetData() );
		}
	}
}
