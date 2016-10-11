Shader "Hidden/RetroPost"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_InvScreenWidth ("InvScreenWidth", Float) = 0
		_InvScreenHeight ("InvScreenHeight", Float) = 0
		_LightDirection ("LightDirection", Vector) = (0, 0, 0, 0)
	}
	SubShader
	{
		// No culling or depth
		Cull Off ZWrite Off ZTest Always

		Pass
		{
			CGPROGRAM
			#pragma enable_d3d11_debug_symbols
			#pragma vertex vert
			#pragma fragment frag
			
			#include "UnityCG.cginc"

			float _InvScreenWidth;
			float _InvScreenHeight;
			float4 _LightDirection;

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
			};

			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = mul(UNITY_MATRIX_MVP, v.vertex);
				o.uv = v.uv;
				return o;
			}
			
			sampler2D _MainTex;
			//sampler2D _LastCameraDepthTexture;
			sampler2D _CameraDepthNormalsTexture;

			float depthDiff(float depthA, float3 normalA, float depthB, float3 normalB)
			{
				const float normalWeight = 10.0f;
				float normalFactor = normalWeight + 1.0f - normalWeight * dot( normalA, normalB );
				return (abs(DECODE_EYEDEPTH( depthB )) - depthA) * normalFactor;
			}

			fixed4 frag (v2f i) : SV_Target
			{
				fixed4 col = tex2D(_MainTex, i.uv);

				// TODO - need DECODE_EYEDEPTH?
				// Note - need to abs(DECODE_EYEDEPTH) because unset pixels will have raw depth value >1, DECODE_EYEDEPTH
				// basically returns 1 / (1 - rawDepth) so they will get a huge negative value when we really want a huge positive value
				// Depth/normal sample at pixel
				float rawDepth; 
				float3 normal;
				DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, i.uv), rawDepth, normal);
				float depth = abs(DECODE_EYEDEPTH(rawDepth));

				float dLeft, dRight, dUp, dDown;
				float3 nLeft, nRight, nUp, nDown;
				DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, i.uv - float2(_InvScreenWidth, 0)), dLeft, nLeft);
				DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, i.uv + float2(_InvScreenWidth, 0)), dRight, nRight);
				DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, i.uv - float2(0, _InvScreenHeight)), dUp, nUp);
				DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, i.uv + float2(0, _InvScreenHeight)), dDown, nDown);

				float dMax = max(
					max(depthDiff(depth, normal, dLeft, nLeft), depthDiff(depth, normal, dRight, nRight)), 
					max(depthDiff(depth, normal, dUp, nUp), depthDiff(depth, normal, dDown, nDown)));

				const float threshold = 0.001f;
				const float darken = 0.3f;
				float edgeFactor = saturate(floor(dMax / threshold));

				// TODO are the normals from the depth-normal texture normalized?
				//float shade = floor(saturate(-dot(normal, _LightDirection) * 1.0f / shadeThreshold));
				//edgeFactor += shadeFactor;

				return fixed4(saturate(col.xyz * (1 - edgeFactor * darken)), 1);
			}
			ENDCG
		}
	}
}
