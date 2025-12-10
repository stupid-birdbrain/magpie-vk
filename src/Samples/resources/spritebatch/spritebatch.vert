#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec4 inColor;
layout(location = 2) in vec2 inTexCoord;

layout(location = 0) out vec4 vColor;
layout(location = 1) out vec2 vTexCoord;

layout(push_constant) uniform PushConstants {
    mat4 Transform;
} pc;

void main() {
    vColor = inColor;
    vTexCoord = inTexCoord;
    gl_Position = pc.Transform * vec4(inPosition, 1.0);
}
