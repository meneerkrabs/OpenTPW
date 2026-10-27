vertex {
    layout(location = 0) in vec3 position;
    layout(location = 1) in vec3 normal;
    layout(location = 2) in vec2 texCoords;
    layout(location = 3) in int texIndex;
    layout(location = 4) in uint matFlags;

    layout(set = 0, binding = 0) uniform ObjectUniformBuffer {
        mat4 g_mModel;
        mat4 g_mView;
        mat4 g_mProj;
        vec3 g_vLightPos;
        float g_flAmbientLevel; // [EXT:evening] the share of the original 0.4 ambient that is left (0 = unchanged: the extension is off or it is day); the padding after the sun position
        vec3 g_vLightColor;
        float g_flLightCount; // [EXT:point-lights] the number of lights after the block (the padding after the light colour)
        vec3 g_vCameraPos;
        float g_flTime;
        float g_flViewDistanceScale;
        float g_flAmbientR; // [EXT:evening] the tinted ambient (the free padding after the view distance); all zero = unchanged
        float g_flAmbientG;
        float g_flAmbientB;
        vec3 g_vFogColor;
        float g_flSnowCover; // [EXT:winter-snow] the settled snow of this draw: 0 = none (the padding after the fog colour), 0..1 = white, negative = slush; see snowBlend
        vec4 g_vPointLight[32]; // [EXT:point-lights] 16 lights x 2 vec4, from byte 272; only the lights of a draw are uploaded
    } g_oUbo;

    // A struct, not an interface block: Veldrid.SPIRV's HLSL passes a block as its own main() parameter, so the vertex output
    // and pixel input signatures list the block, the flat varyings and SV_Position in different register orders, and Direct3D 11
    // fed the pixel shader the wrong registers (issue #754: one flat colour per face). A struct keeps both in location order.
    layout(location = 0) out struct VS_OUT {
        vec2 vTexCoords;
        vec3 vNormal;
        vec3 vPosition;
        vec3 vWorldPosition;
        vec3 vLocalPosition; // [EXT:winter-snow] the model-space position: the snow noise of a moving part stays on the part
    } vs_out;

    layout(location = 5) out flat int outTexIndex;
    layout(location = 6) out flat uint outMatFlags;

    void main() {
        vs_out.vTexCoords = texCoords;
        vs_out.vTexCoords.y = 1.0 - vs_out.vTexCoords.y;
        vs_out.vNormal = normal;
        vs_out.vPosition = vec3(g_oUbo.g_mModel * vec4(position, 1.0));
        vs_out.vLocalPosition = position;

        vec4 pos = g_oUbo.g_mModel * vec4(position, 1.0);
        vs_out.vWorldPosition = vec3(g_oUbo.g_mView * pos);
        gl_Position = g_oUbo.g_mProj * g_oUbo.g_mView * pos;

        outTexIndex = texIndex;
        outMatFlags = matFlags;
    }
}

fragment {
    layout(location = 0) in struct VS_OUT {
        vec2 vTexCoords;
        vec3 vNormal;
        vec3 vPosition;
        vec3 vWorldPosition;
        vec3 vLocalPosition; // [EXT:winter-snow] the model-space position: the snow noise of a moving part stays on the part
    } vs_out;

    layout(location = 5) in flat int texIndex;
    layout(location = 6) in flat uint outMatFlags;

    layout(location = 0) out vec4 fragColor;

    layout(set = 0, binding = 0) uniform ObjectUniformBuffer {
        mat4 g_mModel;
        mat4 g_mView;
        mat4 g_mProj;
        vec3 g_vLightPos;
        float g_flAmbientLevel; // [EXT:evening] the share of the original 0.4 ambient that is left (0 = unchanged: the extension is off or it is day); the padding after the sun position
        vec3 g_vLightColor;
        float g_flLightCount; // [EXT:point-lights] the number of lights after the block (the padding after the light colour)
        vec3 g_vCameraPos;
        float g_flTime;
        float g_flViewDistanceScale;
        float g_flAmbientR; // [EXT:evening] the tinted ambient (the free padding after the view distance); all zero = unchanged
        float g_flAmbientG;
        float g_flAmbientB;
        vec3 g_vFogColor;
        float g_flSnowCover; // [EXT:winter-snow] the settled snow of this draw: 0 = none (the padding after the fog colour), 0..1 = white, negative = slush; see snowBlend
        vec4 g_vPointLight[32]; // [EXT:point-lights] 16 lights x 2 vec4, from byte 272; only the lights of a draw are uploaded
    } g_oUbo;

    layout( set = 1, binding = 0 ) uniform texture2D Color0;
    layout( set = 1, binding = 1 ) uniform texture2D Color1;
    layout( set = 1, binding = 2 ) uniform texture2D Color2;
    layout( set = 1, binding = 3 ) uniform texture2D Color3;
    layout( set = 1, binding = 4 ) uniform texture2D Color4;
    layout( set = 1, binding = 5 ) uniform texture2D Color5;
    layout( set = 1, binding = 6 ) uniform texture2D Color6;
    layout( set = 1, binding = 7 ) uniform texture2D Color7;
    layout( set = 1, binding = 8 ) uniform texture2D Color8;
    layout( set = 1, binding = 9 ) uniform texture2D Color9;
    layout( set = 1, binding = 10 ) uniform texture2D Color10;
    layout( set = 1, binding = 11 ) uniform texture2D Color11;
    layout( set = 1, binding = 12 ) uniform texture2D Color12;
    layout( set = 1, binding = 13 ) uniform texture2D Color13;
    layout( set = 1, binding = 14 ) uniform texture2D Color14;
    layout( set = 1, binding = 15 ) uniform texture2D Color15;
    layout( set = 1, binding = 16 ) uniform sampler s_Color;

    const uint FLAG_BASE = 1;
    const uint FLAG_TRANSPARENT = 2;
    const uint FLAG_UNK = 4;
    const uint FLAG_X_AXIS_ANIM = 8;
    const uint FLAG_Y_AXIS_ANIM = 16;
    const uint FLAG_GOURAUD_SHADED = 32;
        
    // TextureSlotAddressing.ClampFlag: an original MD2 slot without flag 0x10/0x20, which tp.exe clamps instead of wrapping.
    const uint FLAG_SLUSH = 0x40000000u; // [EXT:winter-snow] OpenTPW's vertex flag for a path-like material (TextureSlotAddressing.SlushFlag)
    const uint FLAG_CLAMP = 0x80000000u;
    // WorldTranslucency.AlphaTestLowFlag: a corner of a translucent texture's face, which the original draws with state word 0x556 (an alpha test at 0x10/255, tp.exe 0x567620 and 0x590C40)
    const uint FLAG_ALPHA_TEST_LOW = 0x20000000u;
    // WorldTranslucency.CutoutFlag: a corner of a cut-out texture's face (an alpha plane with an alpha byte of 0), which the original draws with state word 0x2756 (an alpha test at 0xF0/255)
    const uint FLAG_CUTOUT = 0x10000000u;
    // WorldTranslucency.OpaqueSlotFlag: a corner of a slot with the runtime flag 0x800 (a ride's edge floor art), which the original draws with state word 0x1256: no alpha test, no blend (tp.exe 0x581790)
    const uint FLAG_OPAQUE_SLOT = 0x08000000u;
    // TextureSlotAddressing.SelfLitFlag: a corner of a self-lit material (material word +0xC bit 0x1), which the original draws with the constant colour of the draw instead of the vertex lighting colour (tp.exe 0x580C51)
    const uint FLAG_SELF_LIT = 0x04000000u;
    // TextureSlotAddressing.LightSourceFlag: a corner of a self-lit material that is a light source itself (glow, flame, fire, torch, lamp, light, coal, lava by its name): at night it stays full-bright [EXT:evening]
    const uint FLAG_LIGHT_SOURCE = 0x02000000u;
    // TextureSlotAddressing.LandDimFlag: a corner of a terrain cell in unowned land, whose vertex colour the original halves (tp.exe 0x56FB0E: shr 1, and 0x7F7F7F, unless the LIGHTING option cleared it)
    const uint FLAG_LAND_DIM = 0x01000000u;
    // TextureSlotAddressing.TerrainUnlitFlag: a corner of the terrain while the LIGHTING option is 0: tp.exe 0x574530 then copies one constant colour (white, 0xFFFFFFFF) into every terrain colour and skips the lights [BIN:TPW-EXE:0x0057453A terrain colour table]
    const uint FLAG_TERRAIN_UNLIT = 0x00800000u;

    // CLAMP on the wrapping sampler: the coordinates stay half a texel of the coarsest mip the footprint can select
    // inside the edges (that mip's real size, floor(size / 2^level) but at least 1, so odd sizes do not bleed either),
    // so no tap reaches the opposite edge, and textureGrad keeps the unclamped derivatives so the filter and mip choice
    // stay those of the unclamped coordinates. The margin is never wider than the footprint, so where the sampler
    // stops at a finer mip (mipmaps off) the edge texel grows by about one screen pixel at most.
    vec2 ClampToEdge( vec2 uv, vec2 size, vec2 dx, vec2 dy )
    {
        float footprint = max( length( dx * size ), length( dy * size ) );
        vec2 levelSize = max( floor( size / exp2( ceil( log2( max( footprint, 1.0 ) ) ) ) ), vec2( 1.0 ) );
        vec2 margin = 0.5 / levelSize;
        return clamp( uv, margin, 1.0 - margin );
    }

    #define SAMPLE_SLOT( tex ) ( clampEdges ? textureGrad( sampler2D( tex, s_Color ), ClampToEdge( finalTexCoords, vec2( textureSize( sampler2D( tex, s_Color ), 0 ) ), uvDx, uvDy ), uvDx, uvDy ) : texture( sampler2D( tex, s_Color ), finalTexCoords ) )

    // [EXT:point-lights] The park's point lights (docs/LIGHTS.md): what the lit uniform block carries after g_vFogColor, g_flLightCount lights of two vec4 each (the position and the squared
    // radius; the colour and one over the squared radius). The original engine's falloff, (r2 - d2) x (1 / r2) added to the lit colour with no normal term, full at the centre and nothing
    // at the radius (tp.exe 0x5741B0). The world position is the model-space position the shader gets from g_mModel. With no light (count 0) nothing is added, so such a draw shades as before.
    vec3 pointLights( vec3 positionWs )
    {
      vec3 sum = vec3( 0.0 );
      int count = min( int( g_oUbo.g_flLightCount ), 16 );
      for ( int i = 0; i < count; i++ )
      {
        vec4 a = g_oUbo.g_vPointLight[i * 2];
        vec4 b = g_oUbo.g_vPointLight[i * 2 + 1];
        vec3 d = positionWs - a.xyz;
        float share = ( a.w - dot( d, d ) ) * b.w;
        sum += b.xyz * max( share, 0.0 );
      }
      return sum;
    }

    // [EXT:winter-snow] Settling snow (docs/WINTER.md): procedural noise on up-facing surfaces, no texture and no binding. g_flSnowCover is the park's cover times the draw's surface factor (the CPU
    // writes 0 for everything that takes no snow, and then none of this runs, so the picture is the original's). Two octaves of value noise on the world position make the snow start in patches and
    // melt back into them; the slope term keeps walls and cliffs bare (up is the world's Z axis). A negative value is slush: a wet, greyish brown at a lower ceiling (paths).
    // sin-free hash (Dave Hoskins, hash12): the sin( dot ) hash bands on ANGLE/D3D and mobile GPUs at world positions of 1e5. The lattice cell is wrapped (period 2048) so the argument stays small and exact.
    float snowHash( vec2 p )
    {
      vec3 p3 = fract( vec3( p.xyx ) * 0.1031 );
      p3 += dot( p3, p3.yzx + 33.33 );
      return fract( ( p3.x + p3.y ) * p3.z );
    }

    float snowNoise( vec2 p )
    {
      vec2 i = mod( floor( p ), 2048.0 );
      vec2 f = fract( p );
      f = f * f * ( 3.0 - 2.0 * f );
      return mix( mix( snowHash( i ), snowHash( i + vec2( 1.0, 0.0 ) ), f.x ), mix( snowHash( i + vec2( 0.0, 1.0 ) ), snowHash( i + vec2( 1.0, 1.0 ) ), f.x ), f.y );
    }

    // g_flSnowCover above 1.5 means the part moves (a ride's node): 2 is added to its cover and the noise is taken in model space, so the patches turn with the part instead of crawling over it.
    // A corner whose material is path-like (FLAG_SLUSH, set by the object loader from the texture name: the entrance plaza, path slabs of objects) takes slush like the path layer does.
    vec3 snowBlend( vec3 albedo, vec3 normal, vec3 positionWs, vec3 positionLocal, float cover, float lightLevel, bool pathLike )
    {
      bool local = cover > 1.5;
      if ( local )
        cover -= 2.0;
      bool slush = cover < 0.0 || pathLike;
      // a slush draw's surface factor is at most 0.35: normalise it so its patches fill in like the snow's do
      float amount = cover < 0.0 ? min( -cover / 0.35, 1.0 ) : min( cover, 1.0 );
      float up = normalize( mat3( g_oUbo.g_mModel ) * normal ).z;
      float slope = smoothstep( 0.55, 0.85, up );
      vec2 q = ( local ? positionLocal.xy : positionWs.xy ) * 0.9;
      float n = snowNoise( q ) * 0.65 + snowNoise( q * 2.7 + vec2( 7.3, 3.1 ) ) * 0.35;
      float m = smoothstep( n - 0.08, n + 0.08, amount * slope * 1.15 );
      float luma = dot( albedo, vec3( 0.299, 0.587, 0.114 ) );
      // full cover keeps a trace of the surface under it: the snow's brightness follows the albedo's relief (planks, stones, tufts show as a soft shade), a fine grain breaks up the flat white,
      // and a twentieth of the albedo still shows through (the cover mix tops out below 1), so a covered meadow or kart track does not read as one featureless plane
      float grain = snowNoise( q * 11.0 );
      float relief = clamp( luma * 1.6, 0.0, 1.0 );
      vec3 snowColour = slush
        ? vec3( 0.46, 0.46, 0.43 ) * ( 0.75 + 0.5 * luma ) / lightLevel
        : vec3( 0.90, 0.93, 1.0 ) * ( 0.76 + 0.24 * relief ) * ( 0.93 + 0.12 * n ) * ( 0.96 + 0.08 * grain ) / lightLevel;
      return mix( albedo, snowColour, m * ( slush ? 0.8 : 0.95 ) );
    }

    void main() 
    {
        vec2 finalTexCoords = vs_out.vTexCoords;

        /*if ((outMatFlags & FLAG_X_AXIS_ANIM) != 0) {
            finalTexCoords.x = finalTexCoords.x + g_oUbo.g_flTime * 0.1;
        }

        if ((outMatFlags & FLAG_Y_AXIS_ANIM) != 0) {
            finalTexCoords.y = finalTexCoords.y + g_oUbo.g_flTime * 0.1;
        }*/

        vec3 N = normalize(vs_out.vNormal);
        vec3 L = normalize(g_oUbo.g_vLightPos - vs_out.vWorldPosition);
        
        vec3 vDiffuse = max(dot(N, L), 0.0) * g_oUbo.g_vLightColor;
        // [EXT:evening] the evening extension lowers the ambient at dusk and night (g_flAmbientLevel; 0 keeps the original's 0.4 exactly)
        vec3 vAmbientTint = vec3( g_oUbo.g_flAmbientR, g_oUbo.g_flAmbientG, g_oUbo.g_flAmbientB );
        vec3 vAmbient = vec3(0.4) * ( vAmbientTint.x + vAmbientTint.y + vAmbientTint.z > 0.0 ? vAmbientTint : vec3( g_oUbo.g_flAmbientLevel > 0.0 ? g_oUbo.g_flAmbientLevel : 1.0 ) );

        vec4 vTextureSample = vec4(1, 0, 1, 1);
        bool clampEdges = (outMatFlags & FLAG_CLAMP) != 0u;
        vec2 uvDx = dFdx( finalTexCoords );
        vec2 uvDy = dFdy( finalTexCoords );

        if ( texIndex == 0 ) vTextureSample = SAMPLE_SLOT( Color0 );
        if ( texIndex == 1 ) vTextureSample = SAMPLE_SLOT( Color1 );
        if ( texIndex == 2 ) vTextureSample = SAMPLE_SLOT( Color2 );
        if ( texIndex == 3 ) vTextureSample = SAMPLE_SLOT( Color3 );
        if ( texIndex == 4 ) vTextureSample = SAMPLE_SLOT( Color4 );
        if ( texIndex == 5 ) vTextureSample = SAMPLE_SLOT( Color5 );
        if ( texIndex == 6 ) vTextureSample = SAMPLE_SLOT( Color6 );
        if ( texIndex == 7 ) vTextureSample = SAMPLE_SLOT( Color7 );
        if ( texIndex == 8 ) vTextureSample = SAMPLE_SLOT( Color8 );
        if ( texIndex == 9 ) vTextureSample = SAMPLE_SLOT( Color9 );
        if ( texIndex == 10 ) vTextureSample = SAMPLE_SLOT( Color10 );
        if ( texIndex == 11 ) vTextureSample = SAMPLE_SLOT( Color11 );
        if ( texIndex == 12 ) vTextureSample = SAMPLE_SLOT( Color12 );
        if ( texIndex == 13 ) vTextureSample = SAMPLE_SLOT( Color13 );
        if ( texIndex == 14 ) vTextureSample = SAMPLE_SLOT( Color14 );
        if ( texIndex == 15 ) vTextureSample = SAMPLE_SLOT( Color15 );

        vec3 vShading = vDiffuse + vAmbient;
        // [EXT:point-lights] lights in reach add to the shading like the sun and the ambient (0x5741B0 sums them into one vertex colour)
        if ( g_oUbo.g_flLightCount > 0.5 )
            vShading += pointLights( vs_out.vPosition );
        // a self-lit material takes no light: its corners carry the draw's constant colour, white (tp.exe 0x580C51: the vertex fill skips the lighting colour of 0x574660 for material bit 0x1)
        bool selfLit = ( outMatFlags & FLAG_SELF_LIT ) != 0u;
        if ( selfLit )
            vShading = vec3( 1.0 );
        // the terrain at LIGHTING 0: the constant white replaces the whole lighting colour, the sun, the ambient and the point lights (the lights are summed by 0x5741B0, which the constant branch does not call)
        bool terrainUnlit = ( outMatFlags & FLAG_TERRAIN_UNLIT ) != 0u;
        if ( terrainUnlit )
            vShading = vec3( 1.0 );
        // the land outside the park: the original's vertex colour is each channel fistp( light * 255.0 ) clamped to 0..255 (the terrain colour table, 0x574530), and the draw halves it with 'shr 1; and 0x7F7F7F'
        // (0x56FB0E); so the light is rounded to a level (round to nearest even, the FPU's default: roundEven), clamped to 255, and its integer half taken [BIN:TPW-EXE:0x0056FB0E terrain colour]
        if ( ( outMatFlags & FLAG_LAND_DIM ) != 0u )
            vShading = floor( clamp( roundEven( vShading * 255.0 ), 0.0, 255.0 ) * 0.5 ) / 255.0;
        // [EXT:winter-snow] snow and slush on the albedo, after the light is known (so a lit white lands near 0.9 instead of burning out, and the evening's dark ambient still darkens it) and before
        // the evening tint and the fog act on the result (g_flSnowCover 0 skips it)
        if ( g_oUbo.g_flSnowCover != 0.0 && ( outMatFlags & FLAG_TRANSPARENT ) == 0u )
            vTextureSample.xyz = snowBlend( vTextureSample.xyz, N, vs_out.vPosition, vs_out.vLocalPosition, g_oUbo.g_flSnowCover, max( dot( vShading, vec3( 0.333 ) ), 1.0 ), ( outMatFlags & FLAG_SLUSH ) != 0u );

        vec3 vOutColor;

        // Apply Gouraud shading if the flag is set, otherwise use flat shading
        if ((outMatFlags & FLAG_GOURAUD_SHADED) != 0) {
            vOutColor = vTextureSample.xyz * vShading;
        } else {
            vOutColor = vTextureSample.xyz * vShading;
        }

        // [EXT:evening] at dusk and night (the tinted ambient is set only then) a self-lit material that is no light source, and the terrain while LIGHTING is 0 (its constant white is unlit content too: the extension darkens it on top), takes the tint of the extension's other unlit content (the pool water's), which
        // ModelEntity writes into the camera position this shader never reads; glow, flame, fire, torch and lamp textures stay full-bright, and at day nothing is multiplied
        if ( ( ( selfLit && ( outMatFlags & FLAG_LIGHT_SOURCE ) == 0u ) || terrainUnlit ) && vAmbientTint.x + vAmbientTint.y + vAmbientTint.z > 0.0 )
            vOutColor *= g_oUbo.g_vCameraPos;

        // [EXT:evening] at dusk and night (the tinted ambient is set only then) a soft shoulder keeps a near-white surface lit by a lamp, such as the ticket booths' panels, from burning out to flat white;
        // the colour is untouched up to a brightest channel of 0.4, above it only 25 % of the excess is kept, scaling all three channels so the lamp's warm hue stays (day and extension-off draws skip this: the tint is all zero).
        // It fades in with the falling ambient (none at the first dusk frame, whole at full night: the ambient left goes from 1 to 0.35), so a lit surface never jumps between two frames
        if ( !selfLit && !terrainUnlit && vAmbientTint.x + vAmbientTint.y + vAmbientTint.z > 0.0 )
        {
            float peak = max( vOutColor.x, max( vOutColor.y, vOutColor.z ) );
            vec3 vShouldered = vOutColor * ( peak > 0.4 ? ( 0.4 + ( peak - 0.4 ) * 0.25 ) / peak : 1.0 );
            vOutColor = mix( vOutColor, vShouldered, clamp( ( 1.0 - g_oUbo.g_flAmbientLevel ) / 0.65, 0.0, 1.0 ) );
        }

        // Handle opaque alpha testing: the engine's 0.1, 0xF0/255 for the faces of the original's cut-out state, or 0x10/255 for the faces of its translucent state
        float alphaReference = ( outMatFlags & FLAG_CUTOUT ) != 0u ? 240.0 / 255.0 : ( outMatFlags & FLAG_ALPHA_TEST_LOW ) != 0u ? 16.0 / 255.0 : 0.1;
        // the opaque slot state 0x1256 has neither a test nor a blend: the texture's own alpha is ignored
        if ( ( outMatFlags & FLAG_OPAQUE_SLOT ) != 0u )
            vTextureSample.a = 1.0;
        else if (vTextureSample.a < alphaReference) discard;

        fragColor = vec4(vOutColor, vTextureSample.a);

        // the original's linear fog in the projection's depth (UI-107, RenderFog): g_flViewDistanceScale carries the range (start x 8 x 4096 + end x 8, 1/8 view units; 0 = no fog)
        float fogFactor = 0.0;
        if ( g_oUbo.g_flViewDistanceScale > 0.0 )
        {
            float startQ = floor( g_oUbo.g_flViewDistanceScale / 4096.0 );
            float fogStart = startQ * 0.125;
            float fogEnd = ( g_oUbo.g_flViewDistanceScale - startQ * 4096.0 ) * 0.125;
            fogFactor = clamp( ( 1.0 / gl_FragCoord.w - fogStart ) / max( fogEnd - fogStart, 0.001 ), 0.0, 1.0 );
        }

        // Mix with fog
        fragColor.xyz = mix(fragColor.xyz, g_oUbo.g_vFogColor, fogFactor);
    }
}