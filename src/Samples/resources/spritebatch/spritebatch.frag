#version 450

layout(location = 0) in vec4 vColor;
layout(location = 1) in vec2 vTexCoord;

layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform sampler2D uTexture;

void main() {
    vec4 color = texture(uTexture, vTexCoord) * vColor;
    color.a = 1.0; // TODO: remove
    outColor = color;
}
