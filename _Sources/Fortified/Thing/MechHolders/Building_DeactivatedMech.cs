using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Noise;

namespace Fortified
{
    //在遺跡裡面生成的機體，玩家能夠駭入來獲取，並有機率失敗。
    public class Building_DeactivatedMech : Building, IThingHolder
    {
        public Building_DeactivatedMech()
        {
            this.innerContainer = new ThingOwner<Pawn>(this, false, LookMode.Deep);
        }
        private Faction PawnFaction => this.Faction ?? Faction.OfAncients ?? Faction.OfPirates;
        private ModExtension_DeactivatedMech Extension => this.def.GetModExtension<ModExtension_DeactivatedMech>();
        public bool HasPawn
        {
            get
            {
                if (!innerContainer.NullOrEmpty())
                {
                    if (innerContainer.First() is Pawn || innerContainer.First() is Corpse)
                        return true;
                }
                return false;
            }
        }

        public Pawn Pawn
        {
            get
            {
                if (this != null && !this.innerContainer.NullOrEmpty())
                {
                    if (innerContainer.First() is Pawn) return innerContainer?.First() as Pawn;
                    if (innerContainer.First() is Corpse) return (innerContainer?.First() as Corpse).InnerPawn;
                }
                return null;
            }
        }
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);

            if (!respawningAfterLoad && (Rand.Chance(Extension.spawnChance) || DebugSettings.godMode))
            {
                float weightSelector(PawnGenOption PawnGenOption) => PawnGenOption.selectionWeight;
                do
                {
                    innerContainer.TryAdd(PawnGenerator.GeneratePawn(this.Extension.possibleGeneratePawn.RandomElementByWeight(weightSelector).kind, PawnFaction));
                }
                while (Pawn == null);

                if (Pawn.kindDef?.nameMaker != null)
                {
                    Pawn.Name = PawnBioAndNameGenerator.GenerateFullPawnName(Pawn.def, Pawn.kindDef.nameMaker);
                }

                if (!Rand.Chance(Extension.weaponChance) && Pawn.equipment != null && Pawn.equipment.Primary != null && !Pawn.equipment.Primary.def.destroyOnDrop)
                {
                    Pawn.equipment?.DestroyAllEquipment();
                    Pawn.inventory?.DestroyAll();
                }

                if (Extension.damageHediffs.NullOrEmpty()) return;

                var num = Extension.damageCount.RandomInRange;
                if (num > 0)
                {
                    if (Pawn?.RaceProps != null)
                    {
                        for (int i = 0; i < num; i++)
                        {
                            var part = Pawn.RaceProps.body.AllParts.Where(p => !p.IsCorePart).RandomElement();
                            if (!Pawn.health.hediffSet.HasMissingPartFor(part)) Pawn.health.AddHediff(Extension.damageHediffs.RandomElement(), part);
                            else i--;
                        }
                    }
                }
            }

            if (!HasPawn) Destroy(DestroyMode.Vanish);
        }
        public override void DynamicDrawPhaseAt(DrawPhase phase, Vector3 drawLoc, bool flip = false)
        {
            base.DynamicDrawPhaseAt(phase, drawLoc, flip);
            if (HasPawn && phase == DrawPhase.Draw)
            {
                Vector3 drawLoc2 = drawLoc + (Extension != null ? Extension.innerPawnDrawOffset + Rand.InsideUnitCircleVec3 * Extension.innerPawnScatterRange : Vector3.zero);
                Pawn.Drawer.renderer.DynamicDrawPhaseAt(phase, drawLoc2, Rotation, false);
            }
        }
        private int Seed => this.ThingID.GetHashCodeSafe();
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip); 
            if (Pawn?.equipment?.Primary != null && Extension?.weaponDraw != null)
            {
                Vector3 drawLoc3 = drawLoc + Extension.weaponDraw.OffsetForRot(Rotation);
                drawLoc3.y += Altitudes.AltInc * Extension.weaponDraw.LayerForRot(Rotation, 1);
                float aimAngle = (Extension.weaponDraw.RotationOffsetForRot(Rotation) + Extension.weaponRandomRotRange.RandomInRangeSeeded(Seed)) % 360f;
                PawnRenderUtility.DrawEquipmentAiming(Pawn.equipment.Primary, drawLoc3, aimAngle);
            }
        }
        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, this.GetDirectlyHeldThings());
        }
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption option in base.GetFloatMenuOptions(selPawn))
            {
                yield return option;
            }
            if (this.HasPawn)
            {
                if (selPawn.CanReach(this, PathEndMode.InteractionCell, Danger.Deadly, false, false, TraverseMode.ByPawn))
                {
                    yield return new FloatMenuOption("AncientCorps.DeactivatedMech_Disable".Translate(), delegate ()
                    {
                        selPawn.jobs.TryTakeOrderedJob(new Job(FFF_DefOf.FFF_EjectDeactivatedMech, this));//直接幹掉。
                    });

                    if (selPawn.WorkTypeIsDisabled(WorkTypeDefOf.Research))
                    {
                        yield return DisableOption("FFF.Reason.WorkTypeDisabled".Translate().CapitalizeFirst());
                    }
                    else if (!MechanitorUtility.IsMechanitor(selPawn))
                    {
                        yield return DisableOption("FFF.Reason.NotMechanitor".Translate().CapitalizeFirst());
                    }
                    else if (selPawn.mechanitor.TotalBandwidth - selPawn.mechanitor.UsedBandwidth < Pawn.GetStatValue(StatDefOf.BandwidthCost))
                    {
                        yield return DisableOption("FFF.Reason.NeedBandwidth".Translate(Pawn.GetStatValue(StatDefOf.BandwidthCost)).CapitalizeFirst());
                    }
                    else
                    {
                        yield return new FloatMenuOption("FFF.DeactivatedMech_TryHack".Translate(Pawn.LabelCap), delegate ()
                        {
                            selPawn.jobs.TryTakeOrderedJob(new Job(FFF_DefOf.FFF_HackDeactivatedMech, this));
                        });
                    }
                }
                else
                {
                    yield return DisableOption("NoPath".Translate().CapitalizeFirst());
                }
            }
        }
        private FloatMenuOption DisableOption(string reason)
        {
            return new FloatMenuOption("FFF.DeactivatedMech_CannotHack".Translate() + ": " + reason, null);
        }
        public ThingOwner GetDirectlyHeldThings()
        {
            return this.innerContainer;
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look<ThingOwner>(ref this.innerContainer, "innerContainer", new object[]
            {
                this
            });
        }
        public void EjectContents(Pawn usedBy = null, bool killPawn = false)
        {
            if (killPawn)//非駭入的情況下，機體會被摧毀。
            {
                if (Pawn != null)
                {
                    if (innerContainer.First() is Corpse c || Pawn.health.ShouldBeDead())
                    {
                        innerContainer.TryDropAll(this.Position, this.Map, ThingPlaceMode.Near);
                    }
                    else
                    {
                        innerContainer.TryDrop(Pawn, ThingPlaceMode.Near, 1, out var p);
                        p.SetFactionDirect(Faction.OfAncients);
                        if (p is Pawn pa && !pa.Dead) pa.Kill(new DamageInfo(DamageDefOf.ExecutionCut, 200)); 
                    }
                }
            }
            else
            {
                //if (usedBy.skills.GetSkill(SkillDefOf.Intellectual).Level < 5 && Rand.Chance(0.25f) ||
                //    usedBy.skills.GetSkill(SkillDefOf.Intellectual).Level < 10 && Rand.Chance(0.05f) ||
                //    usedBy.skills.GetSkill(SkillDefOf.Intellectual).Level < 15 && Rand.Chance(0.01f)
                //   )//駭入行蹤洩漏。
                //{
                //    Find.LetterStack.ReceiveLetter("DMSAC_HackFailed".Translate(), "DMSAC_HackFailedDesc".Translate(usedBy.NameShortColored), LetterDefOf.NegativeEvent);
                //    Map.Parent.GetComponent<worldobjectComp>()?.SpawnPatrol();
                //    SetLordJob();
                //}
                //else
                if (usedBy.skills.GetSkill(SkillDefOf.Crafting).Level < 5 && Rand.Chance(0.25f) ||
                    usedBy.skills.GetSkill(SkillDefOf.Crafting).Level < 10 && Rand.Chance(0.05f) ||
                    usedBy.skills.GetSkill(SkillDefOf.Crafting).Level < 15 && Rand.Chance(0.01f)
                   )
                {
                    Find.LetterStack.ReceiveLetter("FFF.HackFailed".Translate(), "FFF.HackFailedDesc".Translate(usedBy.NameShortColored), LetterDefOf.NegativeEvent);
                    Pawn.SetFactionDirect(PawnFaction);
                    SetLordJob();
                }
                else if (usedBy != null && MechanitorUtility.IsMechanitor(usedBy))
                {
                    Pawn.SetFaction(Faction.OfPlayer);
                    if (usedBy.mechanitor.TotalBandwidth - usedBy.mechanitor.UsedBandwidth < Pawn.GetStatValue(StatDefOf.BandwidthCost))
                        if (MechanitorUtility.EverControllable(Pawn))
                        {
                            Pawn.GetOverseer()?.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, Pawn);
                        }
                    usedBy.relations.AddDirectRelation(PawnRelationDefOf.Overseer, Pawn);
                    // Non-player generation can cache unrestricted work types. Rebuild after recruitment.
                    Pawn.Notify_DisabledWorkTypesChanged();
                }
                innerContainer.TryDropAll(base.Position, base.Map, ThingPlaceMode.Near);
            }
            DeSpawn(DestroyMode.Vanish);
        }
        private void SetLordJob()
        {
            Lord lord = Map.lordManager.lords.Find((Lord l2) => l2.LordJob.GetType() == typeof(LordJob_StageThenAttack));
            if (lord == null)
            {
                lord = LordMaker.MakeNewLord(PawnFaction, new LordJob_StageThenAttack(PawnFaction, this.Position, Rand.Range(1, 10)), Map, new List<Pawn> { Pawn });
            }
            else
            {
                lord.AddPawn(Pawn);
            }
        }
        public ThingOwner innerContainer;
    }
}
