vertex {

layout(location = 0) in vec2 vPosition;
layout(location = 1) in vec2 vTexCoords;
layout(location = 2) in vec4 vColor;

layout(location = 0) out vec2 vs_texCoords;
layout(location = 1) out vec4 vs_color;

void main() {
    // Positions are already in normalized device coordinates
    gl_Position = vec4(vPosition, 0.0, 1.0);
    vs_texCoords = vTexCoords;
    vs_color = vColor;
}

}

fragment {

layout(set = 0, binding = 0) uniform texture2D Atlas;
layout(set = 0, binding = 1) uniform sampler s_Atlas;

layout(location = 0) in vec2 fs_texCoords;
layout(location = 1) in vec4 fs_color;
layout(location = 0) out vec4 fragColor;

void main() {
    // Single-channel BF4 coverage atlas: red holds alpha 0-255 (coverage x 17)
    float coverage = texture(sampler2D(Atlas, s_Atlas), fs_texCoords).r;
    fragColor = vec4(fs_color.rgb, fs_color.a * coverage);
}

}
