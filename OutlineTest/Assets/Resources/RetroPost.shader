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
				const float nFactor = 100.0f;
				return abs(depthA - DECODE_EYEDEPTH(depthB)) * (nFactor + 1.0f - nFactor * dot(normalA, normalB));
			}

			fixed4 frag (v2f i) : SV_Target
			{
				fixed4 col = tex2D(_MainTex, i.uv);

				// TODO - need DECODE_EYEDEPTH?
				// Depth/normal sample at pixel
				float rawDepth; 
				float3 normal;
				DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, i.uv), rawDepth, normal);
				float depth = DECODE_EYEDEPTH(rawDepth);

				float dLeft, dRight, dUp, dDown;
				float3 nLeft, nRight, nUp, nDown;
				DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, i.uv - float2(_InvScreenWidth, 0)), dLeft, nLeft);
				DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, i.uv + float2(_InvScreenWidth, 0)), dRight, nRight);
				DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, i.uv - float2(0, _InvScreenHeight)), dUp, nUp);
				DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, i.uv + float2(0, _InvScreenHeight)), dDown, nDown);

				// Distinguish background (cleared maxdepth) pixels from foreground (character) pixels.
				// This is to avoid 1) drawing outlines on background pixels and 2) double-outlining edges
				// TODO - stencil to distinguish outlined objects from everything else?
				float foreground = ceil(1.0 - rawDepth);
				float backgroundRight = floor(dRight);
				float backgroundDown = floor(dDown);

				float dMax = foreground * max(
					max(depthDiff(depth, normal, dLeft, nLeft), backgroundRight * depthDiff(depth, normal, dRight, nRight)), 
					max(depthDiff(depth, normal, dUp, nUp), backgroundDown * depthDiff(depth, normal, dDown, nDown)));

				//Depth-only
				//float d = DECODE_EYEDEPTH(tex2D(_LastCameraDepthTexture, i.uv));
				//float dLeft = DECODE_EYEDEPTH(tex2D(_LastCameraDepthTexture, i.uv - float2(_InvScreenWidth, 0))) - d;
				//float dRight = DECODE_EYEDEPTH(tex2D(_LastCameraDepthTexture, i.uv + float2(_InvScreenWidth, 0))) - d;
				//float dUp = DECODE_EYEDEPTH(tex2D(_LastCameraDepthTexture, i.uv - float2(0, _InvScreenHeight))) - d;
				//float dDown = DECODE_EYEDEPTH(tex2D(_LastCameraDepthTexture, i.uv + float2(0, _InvScreenHeight))) - d;
				//float dMax = max(max(abs(dLeft), abs(dRight)), max(abs(dUp), abs(dDown)));

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
