#version 450

// jacked from shadertoy

layout(location = 0) in vec4 vColor;
layout(location = 1) in vec2 vTexCoord;

layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform sampler2D uTexture;

void main() {
    vec4 color = texture(uTexture, vTexCoord);
    vec3 shifted = vec3(color.b, mix(color.r, color.g, 0.4), color.r * 0.6 + color.g * 0.2 + color.b * 0.4);
    float glow = smoothstep(0.1, 1.0, color.a);
    vec3 tinted = mix(shifted, vec3(0.2, 0.5, 1.0) * (shifted.r + shifted.g + shifted.b), 0.35 * glow);
    outColor = vec4(clamp(tinted, 0.0, 1.0), color.a) * vColor;
}
