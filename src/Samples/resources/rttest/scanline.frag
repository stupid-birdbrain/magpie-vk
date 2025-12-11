#version 450

// jacked from shadertoy temporarily

layout(location = 0) in vec4 vColor;
layout(location = 1) in vec2 vTexCoord;

layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform sampler2D uTexture;

void main() {
    vec4 texColor = texture(uTexture, vTexCoord);

    float scan = 0.85 + 0.15 * sin(vTexCoord.y * 3.14159265 * 360.0);
    float vignette = length(vTexCoord - 0.5);
    float vignetteFactor = 1.0 - smoothstep(0.35, 0.7, vignette) * 0.5;

    vec3 filtered = texColor.rgb * scan * vignetteFactor;
    vec4 color = vec4(filtered, 1.0) * vColor;
    color.a = 1.0;
    outColor = color;
}
