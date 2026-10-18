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
        vec4 g_vPointLight[32]; // [EXT:point-lights] 16 lights x 2 vec4, from byte 272; only the lights of a draw are uploaded
    } g_oUbo;

    layout(location = 0) out VS_OUT {
        vec2 vTexCoords;
        vec3 vNormal;
        vec3 vPosition;
        vec3 vWorldPosition;
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

        outTexIndex = texIndex;
        outMatFlags = matFlags;
    }
}

fragment {
    layout(location = 0) in VS_OUT {
        vec2 vTexCoords;
        vec3 vNormal;
        vec3 vPosition;
        vec3 vWorldPosition;
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
    const uint FLAG_CLAMP = 0x80000000u;

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
        vec3 vOutColor;

        // Apply Gouraud shading if the flag is set, otherwise use flat shading
        if ((outMatFlags & FLAG_GOURAUD_SHADED) != 0) {
            vOutColor = vTextureSample.xyz * vShading;
        } else {
            vOutColor = vTextureSample.xyz * vShading;
        }

        // [EXT:evening] at dusk and night (the tinted ambient is set only then) a soft shoulder keeps a near-white surface lit by a lamp, such as the ticket booths' panels, from burning out to flat white;
        // the colour is untouched up to a brightest channel of 0.4, above it only 25 % of the excess is kept, scaling all three channels so the lamp's warm hue stays (day and extension-off draws skip this: the tint is all zero)
        if ( vAmbientTint.x + vAmbientTint.y + vAmbientTint.z > 0.0 )
        {
            float peak = max( vOutColor.x, max( vOutColor.y, vOutColor.z ) );
            vOutColor *= peak > 0.4 ? ( 0.4 + ( peak - 0.4 ) * 0.25 ) / peak : 1.0;
        }

        // Handle opaque alpha testing
        if (vTextureSample.a < 0.1) discard;

        fragColor = vec4(vOutColor, vTextureSample.a);

        // Calculate fog using view space depth
        // g_flViewDistanceScale stretches the fog distance (graphics settings); 0 from older callers means 1.
        float viewDistanceScale = g_oUbo.g_flViewDistanceScale > 0.0 ? g_oUbo.g_flViewDistanceScale : 1.0;
        float viewSpaceDepth = length(vs_out.vWorldPosition) / viewDistanceScale;
        float fogFactor = exp(viewSpaceDepth * 0.01) * 0.025;
        fogFactor = clamp( fogFactor, 0, 1 );
        
        // Mix with fog
        fragColor.xyz = mix(fragColor.xyz, g_oUbo.g_vFogColor, fogFactor);
    }
}