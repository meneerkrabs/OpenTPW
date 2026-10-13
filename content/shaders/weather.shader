vertex {
    //
    // Layout: the particle quads reuse the shared vertex format; the normal slot carries the
    // tint and texCoords.x the alpha (content/shaders/weather.shader, docs/AUDIO.md).
    //
    layout( location = 0 ) in vec3 position;
    layout( location = 1 ) in vec3 tint;
    layout( location = 2 ) in vec2 texCoords;

    layout( set = 0, binding = 0 ) uniform ObjectUniformBuffer {
        mat4 g_mModel;
        mat4 g_mView;
        mat4 g_mProj;
        vec3 g_vLightPos;
        vec3 g_vLightColor;
        vec3 g_vCameraPos;
        float g_flTime;
    } g_oUbo;

    layout( location = 0 ) out struct VS_OUT {
        vec3 vTint;
        float vAlpha;
    } vs_out;

    void main() {
        vs_out.vTint = tint;
        vs_out.vAlpha = texCoords.x;
        gl_Position = g_oUbo.g_mProj * g_oUbo.g_mView * vec4( position, 1.0 );
    }
}

fragment {
    layout( location = 0 ) in struct VS_OUT {
        vec3 vTint;
        float vAlpha;
    } vs_out;

    layout( location = 0 ) out vec4 fragColor;

    // Untextured camera-facing particles (rain streaks, snow, the bolt): tint and alpha only.
    void main()
    {
        fragColor = vec4( vs_out.vTint, vs_out.vAlpha );
    }
}
