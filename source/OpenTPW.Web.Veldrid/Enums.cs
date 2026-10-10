// Generated from the public enums of Veldrid 4.9.0, Veldrid.SDL2 4.8.0 and Veldrid.SPIRV 1.0.14
// (MIT, Copyright (c) 2017 Eric Mellino and Veldrid contributors) with the same names and values,
// so game code compiles unchanged against the browser renderer (docs/WEB.md).

namespace Veldrid
{
	public enum BlendFactor : byte
	{
		Zero = 0,
		One = 1,
		SourceAlpha = 2,
		InverseSourceAlpha = 3,
		DestinationAlpha = 4,
		InverseDestinationAlpha = 5,
		SourceColor = 6,
		InverseSourceColor = 7,
		DestinationColor = 8,
		InverseDestinationColor = 9,
		BlendFactor = 10,
		InverseBlendFactor = 11,
	}

	public enum BlendFunction : byte
	{
		Add = 0,
		Subtract = 1,
		ReverseSubtract = 2,
		Minimum = 3,
		Maximum = 4,
	}

	[System.Flags]
	public enum BufferUsage : byte
	{
		VertexBuffer = 1,
		IndexBuffer = 2,
		UniformBuffer = 4,
		StructuredBufferReadOnly = 8,
		StructuredBufferReadWrite = 16,
		IndirectBuffer = 32,
		Dynamic = 64,
		Staging = 128,
	}

	[System.Flags]
	public enum ColorWriteMask : int
	{
		None = 0,
		Red = 1,
		Green = 2,
		Blue = 4,
		Alpha = 8,
		All = 15,
	}

	public enum ComparisonKind : byte
	{
		Never = 0,
		Less = 1,
		Equal = 2,
		LessEqual = 3,
		Greater = 4,
		NotEqual = 5,
		GreaterEqual = 6,
		Always = 7,
	}

	public enum FaceCullMode : byte
	{
		Back = 0,
		Front = 1,
		None = 2,
	}

	public enum FrontFace : byte
	{
		Clockwise = 0,
		CounterClockwise = 1,
	}

	public enum GraphicsBackend : byte
	{
		Direct3D11 = 0,
		Vulkan = 1,
		OpenGL = 2,
		Metal = 3,
		OpenGLES = 4,
	}

	public enum IndexFormat : byte
	{
		UInt16 = 0,
		UInt32 = 1,
	}

	public enum Key : int
	{
		Unknown = 0,
		ShiftLeft = 1,
		LShift = 1,
		ShiftRight = 2,
		RShift = 2,
		ControlLeft = 3,
		LControl = 3,
		ControlRight = 4,
		RControl = 4,
		AltLeft = 5,
		LAlt = 5,
		AltRight = 6,
		RAlt = 6,
		WinLeft = 7,
		LWin = 7,
		WinRight = 8,
		RWin = 8,
		Menu = 9,
		F1 = 10,
		F2 = 11,
		F3 = 12,
		F4 = 13,
		F5 = 14,
		F6 = 15,
		F7 = 16,
		F8 = 17,
		F9 = 18,
		F10 = 19,
		F11 = 20,
		F12 = 21,
		F13 = 22,
		F14 = 23,
		F15 = 24,
		F16 = 25,
		F17 = 26,
		F18 = 27,
		F19 = 28,
		F20 = 29,
		F21 = 30,
		F22 = 31,
		F23 = 32,
		F24 = 33,
		F25 = 34,
		F26 = 35,
		F27 = 36,
		F28 = 37,
		F29 = 38,
		F30 = 39,
		F31 = 40,
		F32 = 41,
		F33 = 42,
		F34 = 43,
		F35 = 44,
		Up = 45,
		Down = 46,
		Left = 47,
		Right = 48,
		Enter = 49,
		Escape = 50,
		Space = 51,
		Tab = 52,
		BackSpace = 53,
		Back = 53,
		Insert = 54,
		Delete = 55,
		PageUp = 56,
		PageDown = 57,
		Home = 58,
		End = 59,
		CapsLock = 60,
		ScrollLock = 61,
		PrintScreen = 62,
		Pause = 63,
		NumLock = 64,
		Clear = 65,
		Sleep = 66,
		Keypad0 = 67,
		Keypad1 = 68,
		Keypad2 = 69,
		Keypad3 = 70,
		Keypad4 = 71,
		Keypad5 = 72,
		Keypad6 = 73,
		Keypad7 = 74,
		Keypad8 = 75,
		Keypad9 = 76,
		KeypadDivide = 77,
		KeypadMultiply = 78,
		KeypadSubtract = 79,
		KeypadMinus = 79,
		KeypadAdd = 80,
		KeypadPlus = 80,
		KeypadDecimal = 81,
		KeypadPeriod = 81,
		KeypadEnter = 82,
		A = 83,
		B = 84,
		C = 85,
		D = 86,
		E = 87,
		F = 88,
		G = 89,
		H = 90,
		I = 91,
		J = 92,
		K = 93,
		L = 94,
		M = 95,
		N = 96,
		O = 97,
		P = 98,
		Q = 99,
		R = 100,
		S = 101,
		T = 102,
		U = 103,
		V = 104,
		W = 105,
		X = 106,
		Y = 107,
		Z = 108,
		Number0 = 109,
		Number1 = 110,
		Number2 = 111,
		Number3 = 112,
		Number4 = 113,
		Number5 = 114,
		Number6 = 115,
		Number7 = 116,
		Number8 = 117,
		Number9 = 118,
		Tilde = 119,
		Grave = 119,
		Minus = 120,
		Plus = 121,
		BracketLeft = 122,
		LBracket = 122,
		BracketRight = 123,
		RBracket = 123,
		Semicolon = 124,
		Quote = 125,
		Comma = 126,
		Period = 127,
		Slash = 128,
		BackSlash = 129,
		NonUSBackSlash = 130,
		LastKey = 131,
	}

	public enum MapMode : byte
	{
		Read = 0,
		Write = 1,
		ReadWrite = 2,
	}

	[System.Flags]
	public enum ModifierKeys : int
	{
		None = 0,
		Alt = 1,
		Control = 2,
		Shift = 4,
		Gui = 8,
	}

	public enum MouseButton : int
	{
		Left = 0,
		Middle = 1,
		Right = 2,
		Button1 = 3,
		Button2 = 4,
		Button3 = 5,
		Button4 = 6,
		Button5 = 7,
		Button6 = 8,
		Button7 = 9,
		Button8 = 10,
		Button9 = 11,
		LastButton = 12,
	}

	public enum PixelFormat : byte
	{
		R8_G8_B8_A8_UNorm = 0,
		B8_G8_R8_A8_UNorm = 1,
		R8_UNorm = 2,
		R16_UNorm = 3,
		R32_G32_B32_A32_Float = 4,
		R32_Float = 5,
		BC3_UNorm = 6,
		D24_UNorm_S8_UInt = 7,
		D32_Float_S8_UInt = 8,
		R32_G32_B32_A32_UInt = 9,
		R8_G8_SNorm = 10,
		BC1_Rgb_UNorm = 11,
		BC1_Rgba_UNorm = 12,
		BC2_UNorm = 13,
		R10_G10_B10_A2_UNorm = 14,
		R10_G10_B10_A2_UInt = 15,
		R11_G11_B10_Float = 16,
		R8_SNorm = 17,
		R8_UInt = 18,
		R8_SInt = 19,
		R16_SNorm = 20,
		R16_UInt = 21,
		R16_SInt = 22,
		R16_Float = 23,
		R32_UInt = 24,
		R32_SInt = 25,
		R8_G8_UNorm = 26,
		R8_G8_UInt = 27,
		R8_G8_SInt = 28,
		R16_G16_UNorm = 29,
		R16_G16_SNorm = 30,
		R16_G16_UInt = 31,
		R16_G16_SInt = 32,
		R16_G16_Float = 33,
		R32_G32_UInt = 34,
		R32_G32_SInt = 35,
		R32_G32_Float = 36,
		R8_G8_B8_A8_SNorm = 37,
		R8_G8_B8_A8_UInt = 38,
		R8_G8_B8_A8_SInt = 39,
		R16_G16_B16_A16_UNorm = 40,
		R16_G16_B16_A16_SNorm = 41,
		R16_G16_B16_A16_UInt = 42,
		R16_G16_B16_A16_SInt = 43,
		R16_G16_B16_A16_Float = 44,
		R32_G32_B32_A32_SInt = 45,
		ETC2_R8_G8_B8_UNorm = 46,
		ETC2_R8_G8_B8_A1_UNorm = 47,
		ETC2_R8_G8_B8_A8_UNorm = 48,
		BC4_UNorm = 49,
		BC4_SNorm = 50,
		BC5_UNorm = 51,
		BC5_SNorm = 52,
		BC7_UNorm = 53,
		R8_G8_B8_A8_UNorm_SRgb = 54,
		B8_G8_R8_A8_UNorm_SRgb = 55,
		BC1_Rgb_UNorm_SRgb = 56,
		BC1_Rgba_UNorm_SRgb = 57,
		BC2_UNorm_SRgb = 58,
		BC3_UNorm_SRgb = 59,
		BC7_UNorm_SRgb = 60,
	}

	public enum PolygonFillMode : byte
	{
		Solid = 0,
		Wireframe = 1,
	}

	public enum PrimitiveTopology : byte
	{
		TriangleList = 0,
		TriangleStrip = 1,
		LineList = 2,
		LineStrip = 3,
		PointList = 4,
	}

	public enum ResourceBindingModel : int
	{
		Default = 0,
		Improved = 1,
	}

	public enum ResourceKind : byte
	{
		UniformBuffer = 0,
		StructuredBufferReadOnly = 1,
		StructuredBufferReadWrite = 2,
		TextureReadOnly = 3,
		TextureReadWrite = 4,
		Sampler = 5,
	}

	[System.Flags]
	public enum ResourceLayoutElementOptions : int
	{
		None = 0,
		DynamicBinding = 1,
	}

	public enum SamplerAddressMode : byte
	{
		Wrap = 0,
		Mirror = 1,
		Clamp = 2,
		Border = 3,
	}

	public enum SamplerBorderColor : byte
	{
		TransparentBlack = 0,
		OpaqueBlack = 1,
		OpaqueWhite = 2,
	}

	public enum SamplerFilter : byte
	{
		MinPoint_MagPoint_MipPoint = 0,
		MinPoint_MagPoint_MipLinear = 1,
		MinPoint_MagLinear_MipPoint = 2,
		MinPoint_MagLinear_MipLinear = 3,
		MinLinear_MagPoint_MipPoint = 4,
		MinLinear_MagPoint_MipLinear = 5,
		MinLinear_MagLinear_MipPoint = 6,
		MinLinear_MagLinear_MipLinear = 7,
		Anisotropic = 8,
	}

	public enum ShaderConstantType : int
	{
		Bool = 0,
		UInt16 = 1,
		Int16 = 2,
		UInt32 = 3,
		Int32 = 4,
		UInt64 = 5,
		Int64 = 6,
		Float = 7,
		Double = 8,
	}

	[System.Flags]
	public enum ShaderStages : byte
	{
		None = 0,
		Vertex = 1,
		Geometry = 2,
		TessellationControl = 4,
		TessellationEvaluation = 8,
		Fragment = 16,
		Compute = 32,
	}

	public enum StencilOperation : byte
	{
		Keep = 0,
		Zero = 1,
		Replace = 2,
		IncrementAndClamp = 3,
		DecrementAndClamp = 4,
		Invert = 5,
		IncrementAndWrap = 6,
		DecrementAndWrap = 7,
	}

	public enum TextureSampleCount : byte
	{
		Count1 = 0,
		Count2 = 1,
		Count4 = 2,
		Count8 = 3,
		Count16 = 4,
		Count32 = 5,
	}

	public enum TextureType : int
	{
		Texture1D = 0,
		Texture2D = 1,
		Texture3D = 2,
	}

	[System.Flags]
	public enum TextureUsage : byte
	{
		Sampled = 1,
		Storage = 2,
		RenderTarget = 4,
		DepthStencil = 8,
		Cubemap = 16,
		Staging = 32,
		GenerateMipmaps = 64,
	}

	public enum VertexElementFormat : byte
	{
		Float1 = 0,
		Float2 = 1,
		Float3 = 2,
		Float4 = 3,
		Byte2_Norm = 4,
		Byte2 = 5,
		Byte4_Norm = 6,
		Byte4 = 7,
		SByte2_Norm = 8,
		SByte2 = 9,
		SByte4_Norm = 10,
		SByte4 = 11,
		UShort2_Norm = 12,
		UShort2 = 13,
		UShort4_Norm = 14,
		UShort4 = 15,
		Short2_Norm = 16,
		Short2 = 17,
		Short4_Norm = 18,
		Short4 = 19,
		UInt1 = 20,
		UInt2 = 21,
		UInt3 = 22,
		UInt4 = 23,
		Int1 = 24,
		Int2 = 25,
		Int3 = 26,
		Int4 = 27,
		Half1 = 28,
		Half2 = 29,
		Half4 = 30,
	}

	public enum VertexElementSemantic : byte
	{
		Position = 0,
		Normal = 1,
		TextureCoordinate = 2,
		Color = 3,
	}

	public enum WindowState : int
	{
		Normal = 0,
		FullScreen = 1,
		Maximized = 2,
		Minimized = 3,
		BorderlessFullScreen = 4,
		Hidden = 5,
	}
}

namespace Veldrid.Sdl2
{
	[System.Flags]
	public enum SDL_WindowFlags : uint
	{
		Fullscreen = 1,
		OpenGL = 2,
		Shown = 4,
		Hidden = 8,
		Borderless = 16,
		Resizable = 32,
		Minimized = 64,
		Maximized = 128,
		InputGrabbed = 256,
		InputFocus = 512,
		MouseFocus = 1024,
		FullScreenDesktop = 4097,
		Foreign = 2048,
		AllowHighDpi = 8192,
		MouseCapture = 16384,
		AlwaysOnTop = 32768,
		SkipTaskbar = 65536,
		Utility = 131072,
		Tooltip = 262144,
		PopupMenu = 524288,
	}
}

namespace Veldrid.SPIRV
{
	public enum CrossCompileTarget : uint
	{
		HLSL = 0,
		GLSL = 1,
		ESSL = 2,
		MSL = 3,
	}
}
