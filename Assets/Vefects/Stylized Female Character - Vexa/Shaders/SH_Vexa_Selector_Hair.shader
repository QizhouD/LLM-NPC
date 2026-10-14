// URP replacement for Amplify Built-in SH_Vexa_Selector_Hair (original kept as .shader.bak)
Shader "SH_Vexa_Selector_Hair"
{
    Properties
    {
        [Space(13)][Header(Multiply Color)][Space(13)]_MultTexture("Mult Texture", 2D) = "white" {}
        [Normal][Space(13)][Header(Normal)][Space(13)]_NormalTexture("Normal Texture", 2D) = "bump" {}
        _NormalIntensity("Normal Intensity", Float) = 1
        [Space(13)][Header(Ambient Occlusion)][Space(13)]_AmbientOcclusionTexture("Ambient Occlusion Texture", 2D) = "white" {}
        _AOIntensity("AO Intensity", Float) = 1
        [Space(13)][Header(Metallic)][Space(13)]_MetallicTexture("Metallic Texture", 2D) = "white" {}
        _MetallicMin("Metallic Min", Float) = 0
        _MetallicMax("Metallic Max", Float) = 1
        [Space(13)][Header(Roughness)][Space(13)]_RoughnessTexture("Roughness Texture", 2D) = "white" {}
        _RoughnessMin("Roughness Min", Float) = 0
        _RoughnessMax("Roughness Max", Float) = 1
        [Space(13)][Header(Selector)][Space(13)]_ID01("ID 01", 2D) = "white" {}
        _HairColor("Hair Color", Color) = (0.08490568,0.08490568,0.08490568,0)
        _ClothAccentColor("Cloth Accent Color", Color) = (1,0,0.5400758,0)
        _ClothColor("Cloth Color", Color) = (0.3000541,0.1556604,1,0)
        _ClothSecondaryColor("Cloth Secondary Color", Color) = (0.4245283,0.4245283,0.4245283,0)
        [HideInInspector] _texcoord( "", 2D ) = "white" {}
        [HideInInspector] __dirty( "", Int ) = 1

    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }
        LOD 300
        Cull Back

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"


            TEXTURE2D(_MultTexture); SAMPLER(sampler_MultTexture);
            TEXTURE2D(_NormalTexture); SAMPLER(sampler_NormalTexture);
            TEXTURE2D(_AmbientOcclusionTexture); SAMPLER(sampler_AmbientOcclusionTexture);
            TEXTURE2D(_MetallicTexture); SAMPLER(sampler_MetallicTexture);
            TEXTURE2D(_RoughnessTexture); SAMPLER(sampler_RoughnessTexture);
            TEXTURE2D(_ID01); SAMPLER(sampler_ID01);


            CBUFFER_START(UnityPerMaterial)
                float4 _MultTexture_ST;
                float4 _NormalTexture_ST;
                float4 _AmbientOcclusionTexture_ST;
                float4 _MetallicTexture_ST;
                float4 _RoughnessTexture_ST;
                float4 _ID01_ST;
                float4 _HairColor;
                float4 _ClothAccentColor;
                float4 _ClothColor;
                float4 _ClothSecondaryColor;
                float _NormalIntensity;
                float _AOIntensity;
                float _MetallicMin;
                float _MetallicMax;
                float _RoughnessMin;
                float _RoughnessMax;
            CBUFFER_END


            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 tangentWS  : TEXCOORD3;
                float fogFactor   : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrmInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = nrmInputs.normalWS;
                real sign = input.tangentOS.w * GetOddNegativeScale();
                output.tangentWS = float4(nrmInputs.tangentWS, sign);
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uvNrm = input.uv * _NormalTexture_ST.xy + _NormalTexture_ST.zw;
                float2 uvMult = input.uv * _MultTexture_ST.xy + _MultTexture_ST.zw;
                float2 uvAO = input.uv * _AmbientOcclusionTexture_ST.xy + _AmbientOcclusionTexture_ST.zw;
                float2 uvMet = input.uv * _MetallicTexture_ST.xy + _MetallicTexture_ST.zw;
                float2 uvRgh = input.uv * _RoughnessTexture_ST.xy + _RoughnessTexture_ST.zw;

                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalTexture, sampler_NormalTexture, uvNrm), 1.0h);
                nTS = lerp(half3(0, 0, 1), nTS, (half)_NormalIntensity);
                float3 bitangent = input.tangentWS.w * cross(input.normalWS.xyz, input.tangentWS.xyz);
                half3x3 tbn = half3x3(input.tangentWS.xyz, bitangent, input.normalWS.xyz);
                half3 normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(nTS, tbn));


                float2 uvID01 = input.uv * _ID01_ST.xy + _ID01_ST.zw;
                half4 id01 = SAMPLE_TEXTURE2D(_ID01, sampler_ID01, uvID01);
                half4 tint = lerp(_ClothColor, _HairColor, id01.r);
                tint = lerp(tint, _ClothAccentColor, id01.g);
                tint = lerp(tint, _ClothSecondaryColor, id01.b);
                half3 albedo = (SAMPLE_TEXTURE2D(_MultTexture, sampler_MultTexture, uvMult) * tint).rgb;
                half3 emission = half3(0, 0, 0);


                half metallic = saturate(lerp((half)_MetallicMin, (half)_MetallicMax, SAMPLE_TEXTURE2D(_MetallicTexture, sampler_MetallicTexture, uvMet).r));
                half smoothness = saturate(lerp((half)_RoughnessMin, (half)_RoughnessMax, SAMPLE_TEXTURE2D(_RoughnessTexture, sampler_RoughnessTexture, uvRgh).r));
                half ao = lerp(1.0h, SAMPLE_TEXTURE2D(_AmbientOcclusionTexture, sampler_AmbientOcclusionTexture, uvAO).r, (half)_AOIntensity);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                inputData.shadowCoord = ComputeScreenPos(input.positionCS);
                #else
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.metallic = metallic;
                surfaceData.smoothness = smoothness;
                surfaceData.normalTS = nTS;
                surfaceData.occlusion = ao;
                surfaceData.alpha = 1;
                surfaceData.emission = emission;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 GetShadowPositionHClip(Attributes input)
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }
            Varyings vert(Attributes input)
            {
                Varyings output; UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = GetShadowPositionHClip(input); return output;
            }
            half4 frag(Varyings input) : SV_TARGET { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            Varyings vert(Attributes input)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz); return o;
            }
            half4 frag(Varyings i) : SV_TARGET { return 0; }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

