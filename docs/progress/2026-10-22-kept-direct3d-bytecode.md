## Faster start on Windows: Direct3D shaders are compiled once (October 22, 2026)

On Windows the game still compiled its shaders for Direct3D 11 at every start, by our estimate about a second on the PC of issue #754. The compiled Direct3D bytecode is now kept beside the other compiled shaders in the user's cache folder and reused until the shader, the compiler or Windows' own shader compiler changes; it is exactly the bytecode Veldrid made before, so the picture is the same ([ENGINE-PERFORMANCE.md](../ENGINE-PERFORMANCE.md), "Kept Direct3D 11 bytecode").
