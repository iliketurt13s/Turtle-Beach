Shader "Custom/SpriteSilhouette"
{
    // Renders a sprite as one flat solid color following its exact silhouette
    // — only the texture's alpha channel is sampled, its RGB is discarded
    // entirely, so unlike a normal tinted sprite (color * texture), differently
    // colored/shaded pixels in the source art all read as the same flat color.
    // Structured to match Unity's built-in Sprites-Default shader (which every
    // sprite in this project already renders with) so it blends/sorts
    // identically — same tags, same premultiplied-alpha blend mode.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                // The SpriteRenderer's own Color field arrives here as the
                // per-vertex color — this is the sole source of the solid
                // outline color, so callers just set SpriteRenderer.color.
                OUT.color = IN.color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed alpha = tex2D(_MainTex, IN.texcoord).a;
                fixed4 col = IN.color;
                col.a *= alpha;
                col.rgb *= col.a; // premultiplied, required by the "Blend One OneMinusSrcAlpha" above
                return col;
            }
            ENDCG
        }
    }
}
