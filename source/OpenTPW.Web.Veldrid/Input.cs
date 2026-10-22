using System.Globalization;
using System.Numerics;
using OpenTPW;

namespace Veldrid
{
	public interface InputSnapshot
	{
		IReadOnlyList<KeyEvent> KeyEvents { get; }
		IReadOnlyList<MouseEvent> MouseEvents { get; }
		IReadOnlyList<char> KeyCharPresses { get; }
		Vector2 MousePosition { get; }
		float WheelDelta { get; }
		bool IsMouseDown( MouseButton button );
	}

	public readonly struct KeyEvent
	{
		public Key Key { get; }
		public bool Down { get; }
		public ModifierKeys Modifiers { get; }
		public bool Repeat { get; }

		public KeyEvent( Key key, bool down, ModifierKeys modifiers ) : this( key, down, modifiers, false ) { }

		public KeyEvent( Key key, bool down, ModifierKeys modifiers, bool repeat )
		{
			Key = key;
			Down = down;
			Modifiers = modifiers;
			Repeat = repeat;
		}
	}

	public readonly struct MouseEvent
	{
		public MouseButton MouseButton { get; }
		public bool Down { get; }

		public MouseEvent( MouseButton button, bool down )
		{
			MouseButton = button;
			Down = down;
		}
	}

	internal sealed class CanvasInputSnapshot : InputSnapshot, IOrderedInputSnapshot
	{
		private readonly List<OrderedInputEvent> orderedEvents = new();
		private long orderedSequence;
		public IReadOnlyList<OrderedInputEvent> OrderedEvents => orderedEvents;
		private readonly bool[] buttons = new bool[13];
		public List<KeyEvent> Keys { get; } = new();
		public List<MouseEvent> Mouse { get; } = new();
		public List<char> Characters { get; } = new();
		public IReadOnlyList<KeyEvent> KeyEvents => Keys;
		public IReadOnlyList<MouseEvent> MouseEvents => Mouse;
		public IReadOnlyList<char> KeyCharPresses => Characters;
		public Vector2 MousePosition { get; set; }
		public float WheelDelta { get; set; }
		public bool IsMouseDown( MouseButton button ) => buttons[(int)button];

		private readonly bool[] pressedThisFrame = new bool[13];
		private readonly List<MouseButton> deferredReleases = new();
		private readonly HashSet<Key> keysPressedThisFrame = new();
		private readonly List<KeyEvent> deferredKeyReleases = new();

		/// <summary>Starts the next frame's snapshot, applying releases held back from the last one.</summary>
		public void Clear()
		{
			// Deferred legacy releases below are not newly received platform events.
			orderedEvents.Clear();
			Keys.Clear();
			Mouse.Clear();
			Characters.Clear();
			WheelDelta = 0;
			Array.Clear( pressedThisFrame );
			foreach ( var button in deferredReleases )
				SetButton( button, false );
			deferredReleases.Clear();
			keysPressedThisFrame.Clear();
			Keys.AddRange( deferredKeyReleases );
			deferredKeyReleases.Clear();
		}

		/// <summary>Keys get the same treatment as <see cref="SetButton"/>: a tap within one frame still counts.</summary>
		public void AddKey( KeyEvent key )
		{
			if ( !key.Down && keysPressedThisFrame.Contains( key.Key ) )
			{
				deferredKeyReleases.Add( key );
				return;
			}
			if ( key.Down )
			{
				var held = deferredKeyReleases.FindIndex( release => release.Key == key.Key );
				if ( held >= 0 )
				{
					Keys.Add( deferredKeyReleases[held] );
					deferredKeyReleases.RemoveAt( held );
				}
				keysPressedThisFrame.Add( key.Key );
			}
			Keys.Add( key );
		}

		/// <summary>
		/// The game reads button state once per frame. A click whose press and release land in the
		/// same frame (likely with slow interpreted frames) stays down for this frame and is
		/// released in the next, so it is never lost.
		/// </summary>
		public void SetButton( MouseButton button, bool down )
		{
			if ( !down && pressedThisFrame[(int)button] )
			{
				deferredReleases.Add( button );
				return;
			}
			// Pressed again before the held-back release: it was released and pressed, and stays down.
			if ( down && deferredReleases.Remove( button ) )
				Mouse.Add( new MouseEvent( button, false ) );
			if ( down )
				pressedThisFrame[(int)button] = true;
			buttons[(int)button] = down;
			Mouse.Add( new MouseEvent( button, down ) );
		}

		/// <summary>
		/// Applies the events opentpw-gl.js queued, one per line:
		/// <c>k down key modifiers repeat [time]</c>, <c>b down button [x y [time]]</c>, <c>m x y [time]</c>, <c>w delta</c>, <c>c codepoint</c>, <c>r</c> (resized).
		/// The optional time is the DOM event's own <c>timeStamp</c> in milliseconds; a record without one (a blur-generated release) reuses the last
		/// time seen, so a long frame never stretches a click. Key names are Veldrid's <see cref="Key"/> names. Returns whether the canvas was resized.
		/// </summary>
		public bool Apply( string events )
		{
			var resized = false;
			foreach ( var line in events.Split( '\n', StringSplitOptions.RemoveEmptyEntries ) )
			{
				var parts = line.Split( ' ' );
				switch ( parts[0] )
				{
					case "k" when parts.Length >= 5 && Enum.TryParse<Key>( parts[2], out var key ):
						var keyEvent = new KeyEvent( key, parts[1] == "1", (ModifierKeys)int.Parse( parts[3], CultureInfo.InvariantCulture ), parts[4] == "1" );
						Record( keyEvent.Down ? OrderedInputEventKind.KeyDown : OrderedInputEventKind.KeyUp,
							key: (int)keyEvent.Key, modifiers: (int)keyEvent.Modifiers, repeat: keyEvent.Repeat, time: parts.Length >= 6 ? parts[5] : null );
						AddKey( keyEvent );
						break;
					case "b" when parts.Length >= 3:
						var button = (MouseButton)int.Parse( parts[2], CultureInfo.InvariantCulture );
						var down = parts[1] == "1";
						// Physical presses and releases include canvas-relative coordinates, even outside the canvas for a release.
						// Old/blur-generated records have none, so keep their position explicitly inexact.
						// Do not invent a motion event or change the legacy snapshot's final position.
						var exactPosition = parts.Length >= 5;
						var x = exactPosition ? float.Parse( parts[3], CultureInfo.InvariantCulture ) : MousePosition.X;
						var y = exactPosition ? float.Parse( parts[4], CultureInfo.InvariantCulture ) : MousePosition.Y;
						Record( down ? OrderedInputEventKind.ButtonDown : OrderedInputEventKind.ButtonUp,
							button: (int)button, exactPosition: exactPosition, x: x, y: y, time: parts.Length >= 6 ? parts[5] : null );
						SetButton( button, down );
						break;
					case "m" when parts.Length >= 3:
						MousePosition = new Vector2( float.Parse( parts[1], CultureInfo.InvariantCulture ), float.Parse( parts[2], CultureInfo.InvariantCulture ) );
						Record( OrderedInputEventKind.Motion, exactPosition: true, time: parts.Length >= 4 ? parts[3] : null );
						break;
					case "w" when parts.Length >= 2:
						WheelDelta += float.Parse( parts[1], CultureInfo.InvariantCulture );
						break;
					case "c" when parts.Length >= 2:
						Characters.AddRange( char.ConvertFromUtf32( int.Parse( parts[1], CultureInfo.InvariantCulture ) ) );
						break;
					case "r":
						resized = true;
						break;
				}
			}
			return resized;
		}

		private long lastTimestamp;

		private void Record( OrderedInputEventKind kind, int button = -1, int key = 0, int modifiers = 0, bool repeat = false, bool exactPosition = false, float? x = null, float? y = null, string? time = null )
		{
			if ( time != null && double.TryParse( time, NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds ) && double.IsFinite( milliseconds ) )
				lastTimestamp = (long)Math.Round( milliseconds );
			orderedEvents.Add( new OrderedInputEvent( ++orderedSequence, kind, x ?? MousePosition.X, y ?? MousePosition.Y,
				button, key, modifiers, repeat, lastTimestamp, OrderedInputEventSource.Browser, exactPosition ) );
		}
	}
}

namespace Veldrid.Sdl2
{
	public delegate void SDLEventHandler( ref SDL_Event ev );

	public struct SDL_Event { }

	public struct SDL_Window
	{
		public IntPtr NativePointer;
		public SDL_Window( IntPtr pointer ) => NativePointer = pointer;
		public static implicit operator IntPtr( SDL_Window window ) => window.NativePointer;
		public static implicit operator SDL_Window( IntPtr pointer ) => new( pointer );
	}

	public unsafe struct SDL_DisplayMode
	{
		public uint format;
		public int w;
		public int h;
		public int refresh_rate;
		public void* driverdata;
	}

	public readonly struct DragDropEvent
	{
		public string File { get; }
		public DragDropEvent( string file ) => File = file;
	}

	/// <summary>The page's canvas in the role of the SDL window.</summary>
	public class Sdl2Window
	{
		private readonly CanvasInputSnapshot snapshot = new();
		private bool visible;

		public Sdl2Window( string title, int x, int y, int width, int height, SDL_WindowFlags flags, bool threadedProcessing )
		{
			Title = title;
			visible = (flags & SDL_WindowFlags.Hidden) == 0;
		}

		public string Title { get; set; }
		public bool Exists { get; private set; } = true;
		public IntPtr SdlWindowHandle => new( 1 );
		public IntPtr Handle => SdlWindowHandle;
		public WindowState WindowState { get; set; } = WindowState.Normal;
		public bool Focused => true;
		public bool Resizable { get; set; } = true;
		public bool BorderVisible { get; set; }
		public bool CursorVisible { get; set; } = true;
		public int X { get; set; }
		public int Y { get; set; }

		/// <summary>Canvas size in CSS pixels (SDL's logical window size).</summary>
		public int Width
		{
			get => (int)Gl.Metrics()[0];
			set { }
		}

		public int Height
		{
			get => (int)Gl.Metrics()[1];
			set { }
		}

		public bool Visible
		{
			get => visible;
			set => visible = value;
		}

		public event Action? Resized;
		public event Action? Closed;
#pragma warning disable CS0067 // The browser has no file drops into the game yet.
		public event Action<DragDropEvent>? DragDrop;
#pragma warning restore CS0067

		/// <summary>The canvas drawing size in device pixels (browser only; SDL asks the driver instead).</summary>
		public (int Width, int Height) PixelSize
		{
			get
			{
				var metrics = Gl.Metrics();
				return ((int)Math.Round( metrics[0] * metrics[2] ), (int)Math.Round( metrics[1] * metrics[2] ));
			}
		}

		/// <summary>The screen size in CSS pixels (browser only).</summary>
		public (int Width, int Height) ScreenSize
		{
			get
			{
				var metrics = Gl.Metrics();
				return ((int)metrics[3], (int)metrics[4]);
			}
		}

		public InputSnapshot PumpEvents()
		{
			snapshot.Clear();
			if ( snapshot.Apply( Gl.TakeEvents() ) )
				Resized?.Invoke();
			return snapshot;
		}

		public void Close()
		{
			Exists = false;
			Closed?.Invoke();
		}
	}

	public static unsafe class Sdl2Native
	{
		public static void SDL_GetWindowSize( SDL_Window window, int* width, int* height )
		{
			var metrics = Gl.Metrics();
			*width = (int)metrics[0];
			*height = (int)metrics[1];
		}

		/// <summary>The canvas is always shown (there are no offscreen browser runs).</summary>
		public static SDL_WindowFlags SDL_GetWindowFlags( SDL_Window window ) => SDL_WindowFlags.Shown;

		public static int SDL_ShowCursor( int toggle )
		{
			if ( toggle >= 0 )
				Gl.SetCursorVisible( toggle != 0 );
			return 1;
		}

		public static int SDL_GetDesktopDisplayMode( int displayIndex, SDL_DisplayMode* mode )
		{
			var metrics = Gl.Metrics();
			mode->w = (int)metrics[3];
			mode->h = (int)metrics[4];
			return 0;
		}

		public static T LoadFunction<T>( string name ) => throw new EntryPointNotFoundException( $"{name} is not available in the browser." );
	}
}
