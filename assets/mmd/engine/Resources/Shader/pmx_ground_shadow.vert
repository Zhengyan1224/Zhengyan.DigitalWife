#version 300 es

in vec3 in_Pos;

uniform mat4 u_WVP;

void main() {
    gl_Position = u_WVP * vec4(in_Pos, 1.0);
}
