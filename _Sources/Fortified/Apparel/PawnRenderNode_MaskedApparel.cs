using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified
{
    /// <summary>
    /// <see cref="PawnRenderNode_MaskedApparel"/> 的設定（可省略：直接用原版 <see cref="PawnRenderNodeProperties"/> 時全部取預設值）。
    /// </summary>
    public class PawnRenderNodeProperties_MaskedApparel : PawnRenderNodeProperties
    {
        /// <summary>主色（遮罩紅色區域）用衣物的 <see cref="Thing.DrawColor"/>（染色、意識形態配色）；false 時用 <c>color</c>。</summary>
        public bool useApparelColor = true;

        /// <summary>第二色（遮罩黑色區域）。白色 = 維持原圖（面窗、管線、氣瓶等不染色的部分）。</summary>
        public Color colorTwo = Color.white;
    }

    /// <summary>
    /// 不分體型、可染色的衣物節點（2026-11-23 從 DepthRim 的潛水服移來）：掛在 <c>apparel.renderNodeProperties</c>，
    /// <c>texPath</c> 只要北／東／南三張貼圖（<see cref="Graphic_Multi"/>）加上同名 <c>m</c> 遮罩（<c>X_southm</c> 等），所有體型共用。
    /// 適合連頭盔的全身衣物、外骨骼、動力甲這類一套圖蓋住整個人的衣物。
    ///
    /// <para>原版的身體衣物會在 <c>wornGraphicPath</c> 後面接 <c>_體型</c>；把 <c>wornGraphicPath</c> 留空，原版就不建那個節點，
    /// 只建這裡的自訂節點。原版的基本節點只用 <c>props.color</c>、也不帶第二色，所以另寫這個類別。</para>
    ///
    /// <para>遮罩紅色 = 主色（見 <see cref="PawnRenderNodeProperties_MaskedApparel.useApparelColor"/>），黑色 = 第二色。
    /// 著色器預設 CutoutComplex，可用 <c>shaderTypeDef</c> 換。雕像沿用原版做法：單色 Cutout。
    /// 通常搭配 <see cref="PawnRenderNodeWorker_BodyApparel"/>。</para>
    /// </summary>
    public class PawnRenderNode_MaskedApparel : PawnRenderNode
    {
        public PawnRenderNode_MaskedApparel(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
            : base(pawn, props, tree)
        {
        }

        private PawnRenderNodeProperties_MaskedApparel MaskedProps => props as PawnRenderNodeProperties_MaskedApparel;

        public override Graphic GraphicFor(Pawn pawn)
        {
            string path = TexPathFor(pawn);
            if (path.NullOrEmpty())
            {
                return null;
            }
            Color? statue = pawn.Drawer.renderer.StatueColor;
            if (statue.HasValue)
            {
                return GraphicDatabase.Get<Graphic_Multi>(path, ShaderDatabase.Cutout, Vector2.one, statue.Value);
            }
            Shader shader = props.shaderTypeDef?.Shader ?? ShaderDatabase.CutoutComplex;
            Color colorTwo = MaskedProps?.colorTwo ?? Color.white;
            return GraphicDatabase.Get<Graphic_Multi>(path, shader, Vector2.one, ColorFor(pawn), colorTwo);
        }

        public override Color ColorFor(Pawn pawn)
        {
            bool useApparelColor = MaskedProps?.useApparelColor ?? true;
            Color color = (useApparelColor ? apparel?.DrawColor : null) ?? props.color ?? Color.white;
            if (props.useRottenColor && pawn.Drawer.renderer.CurRotDrawMode == RotDrawMode.Rotting)
            {
                color = PawnRenderUtility.GetRottenColor(color);
            }
            return color;
        }
    }

    /// <summary>
    /// 自訂身體衣物節點的 worker：顯示條件同原版身體衣物（<see cref="PawnRenderNodeWorker_Apparel_Body"/>）——
    /// 躺床時跟身體一起藏起來，沒有 <see cref="PawnRenderFlags.Clothes"/> 時不畫。
    /// 不直接用原版那個 worker，是因為它會把節點轉型成 <see cref="PawnRenderNode_Apparel"/>。
    /// </summary>
    public class PawnRenderNodeWorker_BodyApparel : PawnRenderNodeWorker_Body
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            return base.CanDrawNow(node, parms) && parms.flags.FlagSet(PawnRenderFlags.Clothes);
        }
    }
}
