#version 300 es

in vec3 in_Pos;

uniform mat4 u_WorldLightViewProjection;

void main()
{
    gl_Position = u_WorldLightViewProjection * vec4(in_Pos, 1.0);
}
