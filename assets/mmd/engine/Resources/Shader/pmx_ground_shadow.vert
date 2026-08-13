#version 300 es

in vec3 in_Pos;

uniform mat4 u_WVP;

void main() {
    gl_Position = u_WVP * vec4(in_Pos, 1.0);
    // Match the Vulkan ground-shadow pass: move the projected shadow a tiny
    // fixed amount toward the camera in clip space for stable contact depth.
    gl_Position.z -= 0.0001 * gl_Position.w;
}
