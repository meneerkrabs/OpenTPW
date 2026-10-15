using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class RuntimeSoundSourcePolicyTests
{
	private RuntimeSoundInputPreflightTests fixture = null!;
	[TestInitialize]
	public void Setup() { fixture = new(); fixture.Setup(); }
	[TestCleanup]
	public void Cleanup() => fixture.Cleanup();
	private static byte[] BankBytes()
	{
		var payload = Mp2DecoderTests.ToneFrame(); var data = new byte[48 + payload.Length];
		BinaryPrimitives.WriteInt32LittleEndian( data, 1 ); BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 4 ), 8 );
		BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 8 ), 40 ); BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 12 ), payload.Length ); payload.CopyTo( data, 48 ); return data;
	}
	private SoundCategory Category( SoundBank bank ) => (SoundCategory)typeof( RuntimeSoundInputPreflightTests ).GetMethod( "Category", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( fixture, new object[] { bank } )!;

	[DataTestMethod, DataRow( false, "unsupported" ), DataRow( true, "unsupported" ), DataRow( false, "invalid" ), DataRow( true, "invalid" )]
	public async Task SourceFactoryFailureStillPropagatesAndDoesNotPoisonRetry( bool cooperative, string kind )
	{
		var data = BankBytes(); Exception? failure = null;
		var bank = new SoundBank( () => failure == null ? new MemoryStream( data, false ) : throw failure, data.Length );
		var category = Category( bank ); using var mixer = new AudioMixer( null ); var system = new SoundEventSystem( mixer, 7 ); var sample = new SoundSample( 1, 65535, 0, 1, 0 );
		failure = kind == "unsupported" ? new NotSupportedException( "Transient source capability" ) : new InvalidDataException( "Transient source container" );
		if ( cooperative )
		{
			if ( kind == "unsupported" ) await Assert.ThrowsExceptionAsync<NotSupportedException>( () => system.DecodeSentenceAsync( category, sample, cooperative: true ) );
			else await Assert.ThrowsExceptionAsync<InvalidDataException>( () => system.DecodeSentenceAsync( category, sample, cooperative: true ) );
		}
		else if ( kind == "unsupported" ) Assert.ThrowsException<NotSupportedException>( () => system.Decode( category, sample ) );
		else Assert.ThrowsException<InvalidDataException>( () => system.Decode( category, sample ) );
		failure = null; var audio = await system.DecodeSentenceAsync( category, sample, cooperative: cooperative ); Assert.IsNotNull( audio );
		CollectionAssert.AreEqual( Mp2Decoder.Decode( data.AsSpan( 48 ).ToArray() ).Samples, audio.Samples ); Assert.AreEqual( 0, mixer.Voices.Count );
	}
	[DataTestMethod, DataRow( false ), DataRow( true )]
	public async Task IoFailureRemainsRetryableAndSuccessfulCacheAvoidsFurtherSourceReads( bool cooperative )
	{
		var data = BankBytes(); Exception? failure = null;
		var bank = new SoundBank( () => failure == null ? new MemoryStream( data, false ) : throw failure, data.Length );
		var category = Category( bank ); using var mixer = new AudioMixer( null ); var system = new SoundEventSystem( mixer, 7 ); var sample = new SoundSample( 1, 65535, 0, 1, 0 );
		failure = new IOException( "Source unavailable" );
		await Assert.ThrowsExceptionAsync<IOException>( () => system.DecodeSentenceAsync( category, sample, cooperative: cooperative ) );
		failure = null; var audio = await system.DecodeSentenceAsync( category, sample, cooperative: cooperative ); Assert.IsNotNull( audio );
		failure = new IOException( "A successful cached sample must not reopen" ); Assert.AreSame( audio, await system.DecodeSentenceAsync( category, sample, cooperative: cooperative ) );
	}
	[TestMethod]
	public async Task WorkerSentenceUsesTheBoundBeforePayloadReads()
	{
		var type = typeof( RuntimeSoundInputPreflightTests ).GetNestedType( "SparseBank", BindingFlags.NonPublic )!;
		var sparse = Activator.CreateInstance( type, new object?[] { Mp2Decoder.MaximumInputBytes + 1, null } )!;
		var bank = (SoundBank)type.GetMethod( "OpenBank" )!.Invoke( sparse, null )!;
		var category = Category( bank ); using var mixer = new AudioMixer( null ); var system = new SoundEventSystem( mixer, 7 ); var sample = new SoundSample( 1, 65535, 0, 1, 0 );
		Assert.IsNull( await system.DecodeSentenceAsync( category, sample, cooperative: false ) );
		Assert.AreEqual( 0L, type.GetField( "PayloadBytes" )!.GetValue( sparse ) );
		var opens = type.GetField( "Opens" )!.GetValue( sparse );
		Assert.IsNull( await system.DecodeSentenceAsync( category, sample, cooperative: false ) ); Assert.AreEqual( opens, type.GetField( "Opens" )!.GetValue( sparse ) );
	}

	[TestMethod]
	public void TypedReadRefusesBeforePayloadWithOneMetadataReadAndWrappersKeepExactException()
	{
		var data = BankBytes(); var opens = 0;
		var bank = new SoundBank( () => { opens++; return new MemoryStream( data, false ); }, data.Length );
		var payload = data.Length - 48; var before = opens;
		Assert.IsNull( bank.GetMpeg( 1, payload - 1, out var rejected ) ); Assert.AreEqual( payload, rejected ); Assert.AreEqual( 1, opens - before );
		before = opens; var exception = Assert.ThrowsException<InvalidDataException>( () => bank.GetMpeg( 1, payload - 1 ) );
		Assert.AreEqual( $"Selected sound entry contains {payload} bytes, exceeding the caller's {payload - 1}-byte input bound.", exception.Message ); Assert.AreEqual( 1, opens - before );
		before = opens; CollectionAssert.AreEqual( data[48..], bank.GetMpeg( 1, payload, out rejected ) ); Assert.AreEqual( 0, rejected ); Assert.AreEqual( 2, opens - before );
		CollectionAssert.AreEqual( data[48..], bank.GetMpeg( 1 ) ); CollectionAssert.AreEqual( data[48..], bank.GetMpeg( 1, payload ) );
	}
	[TestMethod]
	public void TypedReadDoesNotMisclassifyMissingDamagedEmptyOrInvalidBound()
	{
		var data = BankBytes(); var bank = new SoundBank( data );
		foreach ( var number in new uint[] { 0, 2, uint.MaxValue } ) { Assert.IsNull( bank.GetMpeg( number, 0, out var rejected ) ); Assert.AreEqual( 0, rejected ); }
		BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 8 ), 8 ); bank = new SoundBank( data );
		Assert.IsNull( bank.GetMpeg( 1, 0, out var damaged ) ); Assert.AreEqual( 0, damaged ); Assert.IsNull( bank.GetMpeg( 1, 0 ) );
		BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 8 ), 40 ); BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 12 ), 0 ); bank = new SoundBank( data );
		Assert.AreEqual( 0, bank.GetMpeg( 1, 0, out var empty )!.Length ); Assert.AreEqual( 0, empty );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => bank.GetMpeg( 1, -1 ) ); Assert.ThrowsException<ArgumentOutOfRangeException>( () => bank.GetMpeg( 1, -1, out _ ) );
	}
	[DataTestMethod]
	[DataRow( "invalid" )]
	[DataRow( "unsupported" )]
	[DataRow( "io" )]
	public void AllPublicReadShapesPropagateOriginalSourceExceptionWithoutStatusSubstitution( string kind )
	{
		var data = BankBytes(); Exception? failure = null; var bank = new SoundBank( () => failure == null ? new MemoryStream( data, false ) : throw failure, data.Length );
		failure = kind == "invalid" ? new InvalidDataException( "Selected sound entry contains 999 bytes, exceeding the caller's 0-byte input bound." ) : kind == "unsupported" ? new NotSupportedException( "Source cannot open" ) : new IOException( "Source unavailable" );
		foreach ( var read in new Action[] { () => bank.GetMpeg( 1 ), () => bank.GetMpeg( 1, 0 ), () => bank.GetMpeg( 1, 0, out _ ) } )
		{
			try { read(); Assert.Fail( "Source failure was replaced by a refusal status." ); } catch ( Exception caught ) { Assert.AreSame( failure, caught ); }
		}
		failure = null; Assert.IsNotNull( bank.GetMpeg( 1, int.MaxValue, out var rejected ) ); Assert.AreEqual( 0, rejected );
	}

}
