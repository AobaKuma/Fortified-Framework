using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Fortified
{

    public class HediffComp_PreApplyDamage: HediffComp, IPreApplyDamageHandler
    {
        public override void CompPostMake()
        {
            base.CompPostMake();
            AddPawnComp();
        }
        public override void CompExposeData()
        {
            base.CompExposeData();
            if(Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                AddPawnComp();
            }
        }
        public void AddPawnComp()
        {
            PreApplyDamageRegistry.EnsurePawnComp(parent.pawn);
        }
        // 伤害处理优先级 高者先挡
        public virtual int PreApplyDamagePriority => 0;
        public virtual void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            absorbed = false;
        }
        // 接口转发到现有方法
        public void HandlePreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            PreApplyDamage(ref dinfo, out absorbed);
        }
    }
    public class HediffComp_ProtectiveShield : HediffComp_PreApplyDamage, IModificationMergeParticipant
    {
        public float DurablePercent => MaxHitpoints <= 0f ? 0f : Hitpoints / MaxHitpoints;
        public float MaxHitpoints => maxHitpoints == 0 ? maxHitpoints = Mathf.Max(1, Mathf.RoundToInt(Props.hitpoints * parent.pawn.BodySize)) : maxHitpoints;
        public float Hitpoints
        {
            get { return hitpoints; }
            set {
                hitpoints = Mathf.Clamp(value, 0f, MaxHitpoints);
                parent.Severity = DurablePercent;
            }
        }
        private int maxHitpoints;
        private float hitpoints;
        public HediffCompProperties_ProtectiveShield Props
        {
            get
            {
                return (HediffCompProperties_ProtectiveShield)props;
            }
        }
        public override int PreApplyDamagePriority => Props.preApplyDamagePriority;
        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            base.PreApplyDamage(ref dinfo, out absorbed);

            if (!dinfo.Def.harmsHealth)
            {
                absorbed = true;
                return;
            }

            if (Hitpoints > 0)
            {
                var dmg = dinfo.Amount;
                var dmgReduced = dmg - Hitpoints;
                if (dmgReduced <= 0)
                {
                    absorbed = true;
                    dmgReduced = 0;
                }
                dinfo.SetAmount(dmgReduced);
                Hitpoints -= dmg;
                Props.effectOnDamaged?.SpawnMaintained(parent.pawn.Position, parent.pawn.MapHeld, 0.2f);
                FilthMaker.TryMakeFilth(GenAdjFast.AdjacentCells8Way(parent.pawn.Position).RandomElement().ClampInsideMap(parent.pawn.MapHeld), parent.pawn.MapHeld, Props.filthOnDamaged);

            }
            if (Hitpoints <=0)
            {
                Hitpoints = 0;
                Messages.Message("FFF.Message.AddonBroken".Translate(), new LookTargets(parent.pawn.PositionHeld, parent.pawn.MapHeld), MessageTypeDefOf.NeutralEvent);
                parent.pawn.health.RemoveHediff(parent);
            }

        }
        public override IEnumerable<Gizmo> CompGetGizmos()
        {
            if (base.CompGetGizmos()!=null)
            {
                foreach (Gizmo item in base.CompGetGizmos())
                {
                    yield return item;
                }
            }
            
            foreach (Gizmo gizmo in GetGizmos())
            {
                yield return gizmo;
            }
        }
        private IEnumerable<Gizmo> GetGizmos()
        {
            if ((parent.pawn.Faction == Faction.OfPlayer || (parent.pawn.RaceProps.IsMechanoid)) && Find.Selector.SingleSelectedThing == parent.pawn)
            {
                Gizmo_AttachmentShieldStatus gizmo_Shield = new Gizmo_AttachmentShieldStatus
                {
                    shield = this
                };
                yield return gizmo_Shield;
            }
        }        
        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref hitpoints, "hitpoints");
        }
        public override void CompPostMake()
        {
            base.CompPostMake();
            Hitpoints = Props.hitpoints;
        }

        public override void CompPostMerged(Hediff other)
        {
            base.CompPostMerged(other);
            HediffComp_ProtectiveShield otherShield = other?.TryGetComp<HediffComp_ProtectiveShield>();
            if (otherShield != null) Hitpoints += otherShield.Hitpoints;
        }

        public bool CanMergeModificationFrom(HediffComp other)
        {
            return other is HediffComp_ProtectiveShield && Hitpoints < MaxHitpoints;
        }
    }
    public class HediffCompProperties_ProtectiveShield : HediffCompProperties, IModificationMergeCapacityProvider
    {
        public ThingDef filthOnDamaged;
        public EffecterDef effectOnDamaged;
        public int hitpoints;
        // 伤害处理优先级 高者先挡
        public int preApplyDamagePriority = 0;
        public HediffCompProperties_ProtectiveShield()
        {
            compClass = typeof(HediffComp_ProtectiveShield);
        }

        public int GetMaxModificationInstallations(Pawn pawn)
        {
            return Mathf.Max(1, Mathf.CeilToInt(pawn?.BodySize ?? 1f));
        }
    }

    public class Comp_PreApplyDamage : ThingComp
    {
        private Pawn Pawn => parent as Pawn;
        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            base.PostPreApplyDamage(ref dinfo, out absorbed);
            // 合并hediff与护甲的处理器 按优先级降序 挡住即停
            foreach (var h in CollectHandlers().OrderByDescending(c => c.PreApplyDamagePriority))
            {
                h.HandlePreApplyDamage(ref dinfo, out bool blocked);
                if (blocked)
                {
                    absorbed = true;
                    return;
                }
            }
        }
        private IEnumerable<IPreApplyDamageHandler> CollectHandlers()
        {
            Pawn pawn = Pawn;
            if (pawn == null) yield break;
            foreach (var h in pawn.health.hediffSet.GetHediffComps<HediffComp_PreApplyDamage>())
                yield return h;
            if (pawn.apparel?.WornApparel == null) yield break;
            foreach (Apparel apparel in pawn.apparel.WornApparel)
            {
                if (apparel.AllComps.NullOrEmpty()) continue;
                foreach (ThingComp comp in apparel.AllComps)
                {
                    if (comp is IPreApplyDamageHandler handler) yield return handler;
                }
            }
        }
        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            foreach (var blocker in ((Pawn)parent).health.hediffSet.GetHediffComps<HediffComp_DamageBlocker>())
            {
                foreach (var opt in blocker.GetReplenishFloatMenuOptions(selPawn))
                    yield return opt;
            }
        }
    }

    // pawn层钩子注册
    public static class PreApplyDamageRegistry
    {
        public static void EnsurePawnComp(Pawn pawn)
        {
            if (pawn == null) return;
            if (!pawn.TryGetComp<Comp_PreApplyDamage>(out var _))
                pawn.AllComps.Add(new Comp_PreApplyDamage() { parent = pawn });
        }
    }
}
