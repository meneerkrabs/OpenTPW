using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace OpenTPW.Tests;

/// <summary>
/// Installs a sound system whose global categories hold a sound-less event for every id below 1024 and records which events the game asks it to play (<see cref="SoundEventSystem.Triggered"/>),
/// so a test can tell which sound events fire without an audio device or the original data.
/// </summary>
internal sealed class AudioTriggerProbe : IDisposable
{
	private static readonly BindingFlags Private = BindingFlags.Static | BindingFlags.NonPublic;
	private readonly Dictionary<FieldInfo, object?> old = new();
	private readonly AudioMixer mixer = new( null );
	private readonly Action<string?, uint, Vector3?>? oldHook = SoundEventSystem.Triggered;

	public List<(string? Category, uint Event, Vector3? Position)> Fired { get; } = new();

	public AudioTriggerProbe()
	{
		var system = new SoundEventSystem( mixer, 7 );
		Set( "events", system );
		foreach ( var name in new[] { "Ui", "Kids", "Rides", "Staff", "Ambient", "Speech" } )
			Set( $"<{name}>k__BackingField", Category( "cat_" + name.ToLowerInvariant() ) );
		SoundEventSystem.Triggered = ( category, id, position ) => Fired.Add( (category, id, position) );
	}

	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );

	private static SoundCategory Category( string name )
	{
		var category = Empty<SoundCategory>();
		var catalog = Empty<SoundCatalog>();
		typeof( SoundCatalog ).GetField( "<Events>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( catalog, Enumerable.Range( 0, 1024 ).Select( id => new SoundEvent( (uint)id, 0, 0, 0, Array.Empty<SoundEntry>() ) ).ToArray() );
		typeof( SoundCategory ).GetField( "<Name>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( category, name );
		typeof( SoundCategory ).GetField( "<Catalog>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( category, catalog );
		return category;
	}

	private void Set( string field, object value )
	{
		var info = typeof( GameAudio ).GetField( field, Private )!;
		old[info] = info.GetValue( null );
		info.SetValue( null, value );
	}

	/// <summary>The events recorded for <paramref name="category"/>, in firing order.</summary>
	public uint[] Events( string category ) => Fired.Where( item => item.Category == category ).Select( item => item.Event ).ToArray();

	public void Dispose()
	{
		foreach ( var (field, value) in old )
			field.SetValue( null, value );
		SoundEventSystem.Triggered = oldHook;
		mixer.Dispose();
	}
}
