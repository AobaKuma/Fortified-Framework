using System.Collections.Generic;
using Verse;
using RimWorld;
using UnityEngine;
using Verse.AI;
using System.Linq;

namespace Fortified
{
    // 可涂装组件
    public class CompPaintable : ThingComp
    {
        // 四向涂装配置
        public Dictionary<int, FFF_PaintDef> facePaints = new();

        // 全局剪贴板
        public static bool hasClipboard = false;
        public static Color clipColor1;
        public static Color clipColor2;
        public static Color clipColor3;
        public static FFF_CamoDef clipCamoDef;
        public static float clipBrightness;
        public static FFF_OverlayDef clipOverlayDef;

        public CompProperties_Paintable Props => (CompProperties_Paintable)props;

        static ThingDef[] ownerThingDefs = null;

        public static ThingDef[] GetOwnerThingDefs()
        {
            ownerThingDefs ??= DefDatabase<ThingDef>.AllDefs.Where(thingDef => thingDef.comps is { } comps && comps.Any(comp => typeof(CompPaintable).IsAssignableFrom(comp.compClass))).ToArray();

            return ownerThingDefs;
        }


        public override void PostPostMake()
        {
            base.PostPostMake();
            // 初始化字段
            color1 = Props.defaultColor;
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            // 应用预设
            if (!respawningAfterLoad && !initialized)
            {
                ApplyFactionPreset();
                initialized = true;
                Notify_ColorChanged();
            }
        }

        private void ApplyFactionPreset()
        {
            var df = parent.Faction?.def;
            if (df == null) return;

            var allPresets = DefDatabase<FFF_FactionPaintDef>.AllDefsListForReading;
            FFF_FactionPaintDef matched = null;

            if (parent is Pawn pawn)
            {
                matched = allPresets.Find(x => x.faction == df && x.pawnKind == pawn.kindDef);
            }

            matched ??= allPresets.Find(x => x.faction == df && x.pawnKind == null);

            if (matched != null)
            {
                if (matched.options != null && matched.options.Count > 0)
                {
                    var opt = matched.options.RandomElementByWeight(x => x.weight);
                    color1 = opt.color1.ToColor;
                    color2 = opt.color2.ToColor;
                    color3 = opt.color3.ToColor;
                    camoDef = opt.camoDef;
                    brightness = opt.brightness;
                    overlayDef = opt.overlayDef;
                }
                else
                {
                    color1 = matched.color1.ToColor;
                    color2 = matched.color2.ToColor;
                    color3 = matched.color3.ToColor;
                    camoDef = matched.camoDef;
                    brightness = matched.brightness;
                    overlayDef = matched.overlayDef;
                }
            }
            else if (Props.useFactionColor && parent.Faction != Faction.OfPlayer)
            {
                color1 = parent.Faction.Color;
            }
        }

        // 初始化标记
        public bool initialized;

        // 颜色1
        public Color color1 = Color.white;

        // 颜色2
        public Color color2 = Color.white;

        // 颜色3
        public Color color3 = Color.white;

        // 迷彩定义
        public FFF_CamoDef camoDef;

        // 亮度乘数
        public float brightness;

        // 叠加层
        public FFF_OverlayDef overlayDef;

        // 属性列表缓存
        private List<ShaderParameter> cachedShaderParams;
        private Color cachedParamCol1;
        private Color cachedParamCol3;
        private FFF_CamoDef cachedParamCamo;

        // 获取或构建缓存
        public List<ShaderParameter> GetOrBuildShaderParams(Color col1, Color col3, FFF_CamoDef camo)
        {
            if (cachedShaderParams != null
                && cachedParamCol1 == col1
                && cachedParamCol3 == col3
                && cachedParamCamo == camo)
                return cachedShaderParams;

            cachedParamCol1 = col1;
            cachedParamCol3 = col3;
            cachedParamCamo = camo;
            cachedShaderParams = ShaderParamBuilder.Build(col1, col3, camo);
            return cachedShaderParams;
        }

        private Graphic cachedPaintedGraphic;

        // 获取建筑涂装后的Graphic
        public Graphic GetPaintedGraphic(Graphic baseGraphic)
        {
            if (cachedPaintedGraphic != null) return cachedPaintedGraphic;

            Shader shader = FFF_AssetLoader.PaintShader ?? baseGraphic.Shader;
            Color c2 = color2;
            if (FFF_AssetLoader.PaintShader != null) c2.a = brightness;

            var shaderParams = GetOrBuildShaderParams(color1, color3, camoDef);
            var g = GraphicDatabase.Get(baseGraphic.GetType(), baseGraphic.path, shader,
                baseGraphic.drawSize, Color.white, c2, baseGraphic.data, shaderParams);

            Harmony_Painting.ApplyPerDirectionProps(g, camoDef, overlayDef);

            cachedPaintedGraphic = g;
            return g;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref facePaints, "facePaints", LookMode.Value, LookMode.Def);
            Scribe_Values.Look(ref color1, "color1", Color.white);
            Scribe_Values.Look(ref color2, "color2", Color.white);
            Scribe_Values.Look(ref color3, "color3", Color.white);
            Scribe_Defs.Look(ref camoDef, "camoDef");
            Scribe_Values.Look(ref brightness, "brightness");
            Scribe_Values.Look(ref activePaintRequest, "activePaintRequest");
            Scribe_Values.Look(ref requestColor, "requestColor", Color.white);
            Scribe_Values.Look(ref requestColor2, "requestColor2", Color.white);
            Scribe_Values.Look(ref requestColor3, "requestColor3", Color.white);
            Scribe_Defs.Look(ref requestCamo, "requestCamo");
            Scribe_Values.Look(ref requestBrightness, "requestBrightness");
            Scribe_Values.Look(ref initialized, "initialized", false);
            Scribe_Defs.Look(ref overlayDef, "overlayDef");
            Scribe_Defs.Look(ref requestOverlay, "requestOverlay");
            if (facePaints == null) facePaints = new();
        }

        // 通知颜色变更
        public override void Notify_ColorChanged()
        {
            cachedShaderParams = null;
            cachedPaintedGraphic = null;
            if (parent is Pawn pawn)
            {
                pawn.Drawer.renderer.SetAllGraphicsDirty();
            }
            else if (parent.Spawned && parent.Map != null)
            {
                parent.Map.mapDrawer.MapMeshDirty(parent.Position, MapMeshFlagDefOf.Things);
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer) yield break;
            if (parent is Building && !Props.allowGizmoForBuilding) yield break;

            yield return new Command_Action
            {
                defaultLabel = "FFF_ChangePaint".Translate(),
                defaultDesc = "FFF_ChangePaintDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Toggle"),
                action = () => Find.WindowStack.Add(new Dialog_PaintConfig(this))
            };

            if (activePaintRequest)
            {
                yield return new Command_Action
                {
                    defaultLabel = "FFF_CancelPaint".Translate(),
                    defaultDesc = "FFF_CancelPaintDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel"),
                    action = () =>
                    {
                        activePaintRequest = false;
                        // 结束等待
                        if (parent is Pawn mech && mech.jobs?.curDriver is JobDriver_WaitForPainting)
                            mech.jobs.EndCurrentJob(JobCondition.Succeeded);
                    }
                };
            }

            if (hasClipboard)
            {
                yield return new Command_Action
                {
                    defaultLabel = "FFF_PastePaintConfig".Translate(),
                    defaultDesc = "FFF_PastePaintConfigDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/PasteSettings"),
                    action = () =>
                    {
                        activePaintRequest = true;
                        requestColor = clipColor1;
                        requestColor2 = clipColor2;
                        requestColor3 = clipColor3;
                        requestCamo = clipCamoDef;
                        requestBrightness = clipBrightness;
                        requestOverlay = clipOverlayDef;
                        Notify_ColorChanged();
                        Messages.Message("FFF_PaintRequestSent".Translate(), parent, MessageTypeDefOf.NeutralEvent, false);
                    }
                };
            }
        }

        // 涂装请求
        public bool activePaintRequest;
        public Color requestColor = Color.white;
        public Color requestColor2 = Color.white;
        public Color requestColor3 = Color.white;
        public FFF_CamoDef requestCamo;
        public float requestBrightness;
        public FFF_OverlayDef requestOverlay;
    }
}
