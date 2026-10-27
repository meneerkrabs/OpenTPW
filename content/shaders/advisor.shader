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
        vec3 g_vLightColor;
        vec3 g_vCameraPos;
        float g_flTime;
        float g_flViewDistanceScale;
    } g_oUbo;

    // A struct, not an interface block: Veldrid.SPIRV's HLSL passes a block as its own main() parameter, so the vertex output
    // and pixel input signatures list the block, the flat varyings and SV_Position in different register orders, and Direct3D 11
    // fed the pixel shader the wrong registers (issue #754: one flat colour per face). A struct keeps both in location order.
    layout(location = 0) out struct VS_OUT {
        vec2 vTexCoords;
        vec3 vNormal;
        vec3 vPosition;
        vec3 vWorldPosition;
        vec3 vLight;
    } vs_out;

    layout(location = 5) out flat int outTexIndex;
    layout(location = 6) out flat uint outMatFlags;

    void main() {
        vs_out.vTexCoords = texCoords;
        vs_out.vTexCoords.y = 1.0 - vs_out.vTexCoords.y;
        vs_out.vNormal = normal;
        vs_out.vPosition = vec3(g_oUbo.g_mModel * vec4(position, 1.0));

        vec4 pos = g_oUbo.g_mModel * vec4(position, 1.0);
        vs_out.vWorldPosition = vec3(g_oUbo.g_mView * pos);
        gl_Position = g_oUbo.g_mProj * g_oUbo.g_mView * pos;

        // [BIN:TPW-EXE:0x005741B0 vertex lighting] the original lights each vertex: ambient plus the sum over the directional lights of
        // colour x max(0, -L.N), clamped per channel. The advisor has one directional light of colour 1.0 (0x00429BA0) and sees the default
        // ambient 0.15 grey (0x00573FD0, which nothing in an ordinary park changes); the lit colour then modulates the texture.
        vec3 N = normalize(normal);
        vec3 L = normalize(g_oUbo.g_vLightPos - vs_out.vWorldPosition);
        vs_out.vLight = clamp(vec3(0.15) + max(dot(N, L), 0.0) * g_oUbo.g_vLightColor, 0.0, 1.0);

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
        vec3 vLight;
    } vs_out;

    layout(location = 5) in flat int texIndex;
    layout(location = 6) in flat uint outMatFlags;

    layout(location = 0) out vec4 fragColor;

    layout(set = 0, binding = 0) uniform ObjectUniformBuffer {
        mat4 g_mModel;
        mat4 g_mView;
        mat4 g_mProj;
        vec3 g_vLightPos;
        vec3 g_vLightColor;
        vec3 g_vCameraPos;
        float g_flTime;
        float g_flViewDistanceScale;
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
    const uint FLAG_CLAMP = 0x80000000u;
    // TextureSlotAddressing.SelfLitFlag: a corner of a self-lit material (material word +0xC bit 0x1), which the original draws with the constant colour of the draw instead of the vertex lighting colour (tp.exe 0x580C51)
    const uint FLAG_SELF_LIT = 0x04000000u;

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

    void main() 
    {
        vec2 finalTexCoords = vs_out.vTexCoords;

        /*if ((outMatFlags & FLAG_X_AXIS_ANIM) != 0) {
            finalTexCoords.x = finalTexCoords.x + g_oUbo.g_flTime * 0.1;
        }

        if ((outMatFlags & FLAG_Y_AXIS_ANIM) != 0) {
            finalTexCoords.y = finalTexCoords.y + g_oUbo.g_flTime * 0.1;
        }*/

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

        // a self-lit material takes no light: its corners carry the draw's constant colour, white (tp.exe 0x580C51; the advisor's models go through the same vertex fill, 0x581DB0 -> 0x580530)
        vec3 vShading = ( outMatFlags & FLAG_SELF_LIT ) != 0u ? vec3( 1.0 ) : vs_out.vLight;
        vec3 vOutColor;

        // Apply Gouraud shading if the flag is set, otherwise use flat shading
        if ((outMatFlags & FLAG_GOURAUD_SHADED) != 0) {
            vOutColor = vTextureSample.xyz * vShading;
        } else {
            vOutColor = vTextureSample.xyz * vShading;
        }

        // Handle opaque alpha testing
        if (vTextureSample.a < 0.1) discard;

        fragColor = vec4(vOutColor, vTextureSample.a);
    }
}