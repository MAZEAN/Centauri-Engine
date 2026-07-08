#version 330 core

in vec2 fUv;

uniform sampler2D uAlbedo;     // bound only for alpha-tested (foliage) casters
uniform int       uAlphaTest;  // 1 = discard by albedo alpha

// Tunable (RenderConfig.FoliageAlphaCutoff). Must match shaderPBR.frag's own threshold exactly,
// not the shadow caster's/prepass's fixed 0.5 — this pass's whole point is that Forward
// (DepthFunc(Lequal)/no writes) trusts the depth written here as authoritative. If this
// discarded more aggressively than the lit pass does, fragments in the gap between the two
// thresholds would get no real depth written here at all, so overlapping leaf edges in that
// alpha band would have nothing to depth-sort against each other — showing as flickery,
// arbitrarily-ordered noise/fringing right at leaf silhouettes.
uniform float uFoliageAlphaCutoff;

void main()
{
    if (uAlphaTest == 1 && texture(uAlbedo, fUv).a < uFoliageAlphaCutoff)
        discard;
}
