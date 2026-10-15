using System;
using System.Buffers.Binary;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class RuntimeSoundCorrectedReviewTests
{
	private RuntimeSoundInputPreflightTests fixture = null!;
	[TestInitialize]
	public void Setup() { fixture = new(); fixture.Setup(); }
	[TestCleanup]
	public void Cleanup() => fixture.Cleanup();
	private SoundCategory Category( SoundBank bank ) => (SoundCategory)typeof( RuntimeSoundInputPreflightTests ).GetMethod( "Category", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( fixture, new object[] { bank } )!;
	private static byte[] BankBytes()
	{
		var payload = Mp2DecoderTests.ToneFrame(); var data = new byte[48 + payload.Length];
		BinaryPrimitives.WriteInt32LittleEndian( data, 1 ); BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 4 ), 8 );
		BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 8 ), 40 ); BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 12 ), payload.Length ); payload.CopyTo( data, 48 ); return data;
	}
	private static Task<Mp2Audio?> Decode( SoundEventSystem system, SoundCategory category, SoundSample sample, int route )
		=> route == 0 ? Task.FromResult( system.Decode( category, sample ) ) : system.DecodeSentenceAsync( category, sample, cooperative: route == 2 );

	[DataTestMethod]
	[DataRow( 0, "invalid", false )]
	[DataRow( 0, "invalid", true )]
	[DataRow( 0, "unsupported", false )]
	[DataRow( 0, "unsupported", true )]
	[DataRow( 0, "io", false )]
	[DataRow( 0, "io", true )]
	[DataRow( 0, "cancel", false )]
	[DataRow( 0, "cancel", true )]
	[DataRow( 1, "invalid", false )]
	[DataRow( 1, "invalid", true )]
	[DataRow( 1, "unsupported", false )]
	[DataRow( 1, "unsupported", true )]
	[DataRow( 1, "io", false )]
	[DataRow( 1, "io", true )]
	[DataRow( 1, "cancel", false )]
	[DataRow( 1, "cancel", true )]
	[DataRow( 2, "invalid", false )]
	[DataRow( 2, "invalid", true )]
	[DataRow( 2, "unsupported", false )]
	[DataRow( 2, "unsupported", true )]
	[DataRow( 2, "io", false )]
	[DataRow( 2, "io", true )]
	[DataRow( 2, "cancel", false )]
	[DataRow( 2, "cancel", true )]
	public async Task SourceExceptionIdentitySurvivesBothReadStagesAndAllRuntimeRoutes( int route, string kind, bool payloadStage )
	{
		var data = BankBytes(); var opens = 0; var failAt = int.MaxValue; Exception? failure = null;
		var bank = new SoundBank( () => ++opens == failAt ? throw failure! : new MemoryStream( data, false ), data.Length );
		var category = Category( bank ); using var mixer = new AudioMixer( null ); var system = new SoundEventSystem( mixer, 7 ); var sample = new SoundSample( 1, 65535, 0, 1, 0 );
		var message = $"Selected sound entry contains {Mp2Decoder.MaximumInputBytes + 1} bytes, exceeding the caller's {Mp2Decoder.MaximumInputBytes}-byte input bound.";
		failure = kind switch { "invalid" => new InvalidDataException( message ), "unsupported" => new NotSupportedException( message ), "io" => new IOException( message ), _ => new OperationCanceledException( message ) };
		failAt = opens + (payloadStage ? 2 : 1); Exception? caught = null;
		try { await Decode( system, category, sample, route ); } catch ( Exception exception ) { caught = exception; }
		Assert.AreSame( failure, caught, "A source exception must not become an owned refusal or another exception." );
		failure = null; failAt = int.MaxValue; var audio = await Decode( system, category, sample, route ); Assert.IsNotNull( audio );
		CollectionAssert.AreEqual( Mp2Decoder.Decode( data[48..] ).Samples, audio.Samples ); var cachedOpens = opens;
		Assert.AreSame( audio, await Decode( system, category, sample, route ) ); Assert.AreEqual( cachedOpens, opens ); Assert.AreEqual( 0, mixer.Voices.Count );
	}

	[DataTestMethod, DataRow( 0 ), DataRow( 1 ), DataRow( 2 )]
	public async Task ForgetClearsSizeRefusalAndRetiredCategoryDoesNotReinsertIt( int route )
	{
		var type = typeof( RuntimeSoundInputPreflightTests ).GetNestedType( "SparseBank", BindingFlags.NonPublic )!;
		var sparse = Activator.CreateInstance( type, new object?[] { Mp2Decoder.MaximumInputBytes + 1, null } )!;
		var bank = (SoundBank)type.GetMethod( "OpenBank" )!.Invoke( sparse, null )!; var category = Category( bank );
		using var mixer = new AudioMixer( null ); var system = new SoundEventSystem( mixer, 7 ); var sample = new SoundSample( 1, 65535, 0, 1, 0 );
		Assert.IsNull( await Decode( system, category, sample, route ) ); var opens = (int)type.GetField( "Opens" )!.GetValue( sparse )!;
		Assert.IsNull( await Decode( system, category, sample, route ) ); Assert.AreEqual( opens, type.GetField( "Opens" )!.GetValue( sparse ) );
		system.Forget( category );
		for ( var attempt = 1; attempt <= 2; attempt++ )
		{
			Assert.IsNull( await Decode( system, category, sample, route ) ); Assert.AreEqual( opens + attempt, type.GetField( "Opens" )!.GetValue( sparse ) );
		}
		Assert.AreEqual( 0L, type.GetField( "PayloadBytes" )!.GetValue( sparse ) ); Assert.AreEqual( 0L, system.RetainedSentenceBytes );
	}

	[TestMethod]
	public void ChangedSourceLengthWinsOverApparentlyOversizedEntryMetadata()
	{
		var type = typeof( RuntimeSoundInputPreflightTests ).GetNestedType( "SparseBank", BindingFlags.NonPublic )!;
		var sparse = Activator.CreateInstance( type, new object?[] { Mp2Decoder.MaximumInputBytes + 1, null } )!;
		var bank = (SoundBank)type.GetMethod( "OpenBank" )!.Invoke( sparse, null )!;
		var length = type.GetField( "CurrentLength" )!; length.SetValue( sparse, (long)length.GetValue( sparse )! + 1 );
		var rejected = -1; Assert.ThrowsException<IOException>( () => bank.GetMpeg( 1, Mp2Decoder.MaximumInputBytes, out rejected ) );
		Assert.AreEqual( 0, rejected ); Assert.AreEqual( 0L, type.GetField( "PayloadBytes" )!.GetValue( sparse ) );
	}
}
