#version 450

layout(binding = 0) uniform UniformBufferObject {
    mat4 model;
    mat4 view;
    mat4 proj;
    vec4 someOtherData;
} ubo;

layout(binding = 1) uniform sampler2D texSampler;

layout(std430, binding = 2) buffer MyStorageBuffer {
    float data[];
} storageBuffer;

layout(binding = 3, rgba8) uniform image2D storageImage;

layout(location = 0) in vec3 fragColor;
layout(location = 1) in vec2 fragTexCoord;

layout(location = 0) out vec4 outColor;

void main() {
    vec4 sampledTextureColor = texture(texSampler, fragTexCoord);
    vec4 uboProcessedColor = sampledTextureColor * ubo.someOtherData;
    float sb_value = storageBuffer.data[0];

    ivec2 image_coords = ivec2(fragTexCoord * 1024.0);
    vec4 si_color = imageLoad(storageImage, image_coords);
    
    outColor = uboProcessedColor * si_color * vec4(sb_value);
}