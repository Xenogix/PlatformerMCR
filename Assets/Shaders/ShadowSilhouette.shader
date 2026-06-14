Shader "Custom/ShadowSilhouette"
{
    // A flat, translucent silhouette for the best-run shadows, dressed up as a glitchy TV artifact.
    //
    // Stencil masking keeps overlapping shadows from COMPOUNDING their alpha: the first fragment to
    // cover a pixel writes the stencil ref, and any later shadow fragment at that pixel is rejected
    // (Comp NotEqual) — so two (or ten) overlapping shadows read as one uniform silhouette.
    //
    // TV artifact = horizontal sway + occasional "tear" jump (vertex displacement in clip space, so
    // it stays screen-consistent under any camera) + a time-driven alpha flicker. All shadows share
    // this material, so they glitch in sync — like one bad signal. Tunables exposed for dialing in.
    Properties
    {
        _BaseColor      ("Color", Color)             = (0.04, 0.04, 0.07, 0.33)

        [Header(Horizontal Distortion)]
        _SwayAmount     ("Sway Amount", Range(0,0.2))   = 0.012
        _SwaySpeed      ("Sway Speed", Range(0,30))     = 6
        _SwayFreq       ("Sway Vertical Freq", Range(0,40)) = 18
        _TearAmount     ("Tear Jump Amount", Range(0,0.3)) = 0.13
        _TearRate       ("Tear Jumps / sec", Range(0,30))  = 11
        _TearChance     ("Tear Chance", Range(0,1))     = 0.42

        [Header(Flicker)]
        _FlickerAmount  ("Flicker Amount", Range(0,1)) = 0.7
        _FlickerSpeed   ("Flicker Speed", Range(0,40)) = 22
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ShadowSilhouette"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            // Ref 17 is an arbitrary, uncommon value to avoid colliding with URP's own stencil use.
            // First shadow fragment: stencil != 17 -> draws and writes 17. Overlapping fragments of
            // other shadows: stencil == 17 -> rejected, so the union is drawn once at a flat alpha.
            Stencil
            {
                Ref 17
                Comp NotEqual
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings  { float4 positionHCS : SV_POSITION; };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _SwayAmount;
                float _SwaySpeed;
                float _SwayFreq;
                float _TearAmount;
                float _TearRate;
                float _TearChance;
                float _FlickerAmount;
                float _FlickerSpeed;
            CBUFFER_END

            // cheap 1D hash -> [0,1)
            float Hash11(float p) { return frac(sin(p * 127.1) * 43758.5453); }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);

                float t = _Time.y;

                // Smooth horizontal sway, varying with height so the silhouette leans/wobbles.
                float sway = sin(t * _SwaySpeed + IN.positionOS.y * _SwayFreq) * _SwayAmount;

                // Occasional whole-shape horizontal "tear" jump, re-rolled a few times per second.
                float slice = floor(t * _TearRate);
                float jump = (Hash11(slice) < _TearChance)
                    ? (Hash11(slice * 1.7 + 3.1) - 0.5) * _TearAmount
                    : 0.0;

                // Offset in clip space, scaled by w so the screen-space shift is depth-independent.
                OUT.positionHCS.x += (sway + jump) * OUT.positionHCS.w;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 col = _BaseColor;

                // Time-driven flicker: mostly bright, with random dips (an unstable signal).
                float fseed = floor(_Time.y * _FlickerSpeed);
                float flick = lerp(1.0, Hash11(fseed * 0.913), _FlickerAmount);
                col.a *= flick;

                return col;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
