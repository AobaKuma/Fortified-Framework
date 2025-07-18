﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CombatExtended;
using RimWorld;
using UnityEngine;
using Verse;
using Fortified;

namespace FortifiedCE
{
    [StaticConstructorOnStartup]
    public class Building_TurretCapacityCE : Building_TurretGunCE, IThingHolder, IPawnCapacity
    {
        public static readonly Texture2D ExitFacilityIcon = ContentFinder<Texture2D>.Get("Things/ExitFacility");
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn myPawn)
        {
            foreach (FloatMenuOption floatMenuOption2 in base.GetFloatMenuOptions(myPawn))
            {
                yield return floatMenuOption2;
            }
            foreach (FloatMenuOption floatMenuOption2 in BuildingTurretCapacityUtil.GetFloatMenuOptions(myPawn, this, this.innerContainer))
            {
                yield return floatMenuOption2;
            }
            yield break;
        }
        public bool CanEnter => !innerContainer.Any;
        public Building_TurretCapacityCE()
        {
            this.innerContainer = new ThingOwner<Thing>(this, false, LookMode.Deep);
        }
        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, this.GetDirectlyHeldThings());
        }
        public Pawn PawnInside => innerContainer.Any ? innerContainer.First() as Pawn : null;
        public override void Tick()
        {

            if (!innerContainer.Any)
            {
                return;
            }
            base.Tick();
            this.innerContainer.DoTick();
            if (PawnInside != null)
            {
                if (ShouldGetOut() || PawnInside.Downed || PawnInside.InMentalState || PawnInside.Dead)
                {
                    GetOut();
                }
            }

        }
        public override string GetInspectString()
        {
            StringBuilder stringBuilder = new StringBuilder();
            string inspectString = base.GetInspectString();
            if (!inspectString.NullOrEmpty())
            {
                stringBuilder.AppendLine(inspectString);
            }
            if (!CanEnter)
            {
                if (PawnInside != null)
                {
                    stringBuilder.AppendLine("FTF_CurrentOperator".Translate() + ": " + PawnInside.Name);
                }
            }
            else
            {
                stringBuilder.AppendLine("FTF_RequireOperator".Translate());
            }
            return stringBuilder.ToString().TrimEndNewlines();
        }
        private bool ShouldGetOut()
        {
            if (PawnInside.needs?.food != null && PawnInside.needs.food.CurLevel <= 0.1f) return true;
            if (PawnInside.needs?.rest != null && PawnInside.needs.rest.CurLevel <= 0.1f) return true;
            return false;
        }
        public ThingOwner GetDirectlyHeldThings()
        {
            return this.innerContainer;
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref this.innerContainer, "innerContainer", new object[]
            {
                this
            });
        }
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            if (!CanEnter)
            {
                yield return new Command_Action
                {
                    defaultLabel = "FT_BunkerFacility_ExitText".Translate(),
                    icon = ExitFacilityIcon,
                    action = delegate ()
                    {
                        this.GetOut();
                    }
                };
            }
        }
        public virtual bool Accepts(Thing thing)
        {
            return this.innerContainer.CanAcceptAnyOf(thing, true);
        }
        public virtual bool TryAcceptThing(Thing thing)
        {
            if (!this.Accepts(thing))
            {
                return false;
            }
            bool flag;
            if (thing.holdingOwner != null)
            {
                thing.holdingOwner.TryTransferToContainer(thing, this.innerContainer, thing.stackCount, true);
                flag = true;
            }
            else
            {
                flag = this.innerContainer.TryAdd(thing, true);
            }
            if (flag)
            {
                return true;
            }
            return false;
        }
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            GetOut();
            base.Destroy(mode);
        }
        public virtual void GetOut()
        {
            this.innerContainer.TryDropAll(this.InteractionCell, base.Map, ThingPlaceMode.Near, null, null, true);
        }
        public bool HasPawn(out Pawn pawn)
        {
            pawn = PawnInside;
            if (pawn != null) return true;
            return false;
        }

        protected ThingOwner innerContainer;
    }
}
