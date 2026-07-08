#version 330 core

in vec2 fUv;

uniform sampler2D uAlbedo;     // bound only for alpha-tested (foliage) casters
uniform int       uAlphaTest;  // 1 = discard by albedo alpha

void main()
{
    // Must match shaderPBR.frag's own discard threshold exactly, not the shadow caster's/
    // prepass's 0.5 — this pass's whole point is that Forward (DepthFunc(Lequal)/no writes)
    // trusts the depth written here as authoritative. If this discarded more aggressively than
    // the lit pass does, fragments in the gap between the two thresholds would get no real
    // depth written here at all, so overlapping leaf edges in that alpha band would have
    // nothing to depth-sort against each other — showing as flickery, arbitrarily-ordered
    // noise right at leaf silhouettes (the sky or whatever's behind bleeding through
    // inconsistently pixel to pixel).
    if (uAlphaTest == 1 && texture(uAlbedo, fUv).a < 0.05)
        discard;
}
