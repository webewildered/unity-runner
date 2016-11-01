Shader "Hidden/RetroPost"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_EdgeTexture("EdgeTexture", 2D) = "white" {}
		_InvScreenWidth ("InvScreenWidth", Float) = 0
		_InvScreenHeight ("InvScreenHeight", Float) = 0
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
			sampler2D _EdgeTexture;
			sampler2D _CameraDepthNormalsTexture;

			float getDepth(float rawDepth)
			{
				// This feels silly, there's probably a better way to do it
				//return sqrt(sqrt(sqrt(rawDepth)));
				//return (abs(DECODE_EYEDEPTH(rawDepth)));
				//return abs(1.0 / (1.0 - Linear01Depth(rawDepth));
				return rawDepth;
			}

			float depthDiff(float depthA, float3 normalA, float priorityA, float rawDepthB, float3 normalB, float priorityB)
			{
				// Neighbor difference based on depth
				const float normalWeight = 10.0f;
				float normalFactor = normalWeight + 1.0f - normalWeight * dot(normalA, normalB);
				float depthDifference = (getDepth(rawDepthB) - depthA);
				float dDiff = abs(depthDifference) * normalFactor;

				// Neighbor difference based on normal
				// TODO: get cos angle instead?
				float3 crossNormal = cross(normalA, normalB);
				float sinNormalAngle = length(crossNormal);
				float nDiff = sinNormalAngle * sinNormalAngle * sinNormalAngle * 0.05f;

				// A sufficient difference in either depth or normal causes an outline
				float diff = max(dDiff, nDiff);

				// We want to draw the outline one pixel thick, but both neighboring pixels will calculate the same difference,
				// so we need to pick which pixel the outline will go on.  This is done by a priority which is set by the 
				// material properties of the object that the pixel was drawn by.  There are three priorities, background = 0,
				// level = 0.5, character = 1.  The outline is draw on the pixel with higher priority. If two neighboring pixels
				// have the same priority, then we use an algorithm for that priority: for character the pixel with lower depth
				// is chosen, for mesh the pixel with normal more aligned to the camera is chosen, for background it doesn't
				// matter because there should never be an outline between two background pixels. The outline pixel is selected
				// by multiplying diff by the final priority value.  If the priority value is less than or equal to zero, that
				// excludes this pixel from being outlined.

				float pDiff = priorityA - priorityB;

				// Calculate depth and normal based priorities.
				float pDepth = sign(depthDifference);
				float eps = 0.01;
				float pNormal = sign(crossNormal);
				pNormal -= (1.0 - abs(pNormal)) * pDepth; // pNormal could be zero, in that case use pDepth to decide

				// pEq is the priority when priorityA == priorityB
				// It's possible for pDepth or pNormal to be zero, in that case bias it arbitrarily
				float pType = priorityA * 2.0f - 1.0f;
				float pEq = pType * pDepth + (1.0 - pType) * pNormal;

				// Choose the priority based on whether priorityA == priorityB
				float pSame = 1.0 - ceil(abs(pDiff));
				float priority = pSame * pEq + (1.0 - pSame) * ceil(pDiff);

				return diff * priority;
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
				float depth = getDepth(rawDepth);
				float priority = tex2D(_EdgeTexture, i.uv).x;

				float2 coordLeft = i.uv - float2(_InvScreenWidth, 0);
				float2 coordRight = i.uv + float2(_InvScreenWidth, 0);
				float2 coordUp = i.uv - float2(0, _InvScreenHeight);
				float2 coordDown = i.uv + float2(0, _InvScreenHeight);

				float dLeft, dRight, dUp, dDown;
				float3 nLeft, nRight, nUp, nDown;
				DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, coordLeft), dLeft, nLeft);
				DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, coordRight), dRight, nRight);
				DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, coordUp), dUp, nUp);
				DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, coordDown), dDown, nDown);

				float pLeft = tex2D(_EdgeTexture, coordLeft).x;
				float pRight = tex2D(_EdgeTexture, coordRight).x;
				float pUp = tex2D(_EdgeTexture, coordUp).x;
				float pDown = tex2D(_EdgeTexture, coordDown).x;

				float dMax = max(
					max(depthDiff(depth, normal, priority, dLeft, nLeft, pLeft), depthDiff(depth, normal, priority, dRight, nRight, pRight)),
					max(depthDiff(depth, normal, priority, dUp, nUp, pUp), depthDiff(depth, normal, priority, dDown, nDown, pDown)));

				const float threshold = 0.01f;
				const float darken = 0.3f;
				float edgeFactor = saturate(floor(dMax / threshold));

				return fixed4(saturate(col.xyz * (1 - edgeFactor * darken)), 1);
			}
			ENDCG
		}
	}
}
