﻿using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace Fortified
{
    [StaticConstructorOnStartup]
    public class Building_TurretCapacity : Building_TurretGun, IThingHolder, IPawnCapacity
    {
        public bool CanEnter => !innerContainer.Any;
        public static readonly Texture2D ExitFacilityIcon = ContentFinder<Texture2D>.Get("Things/ExitFacility");
        public Pawn PawnInside => innerContainer.Any ? innerContainer.First() as Pawn : null;
        public float CurrentAccuracy => PawnInside == null ? PawnInside.GetStatValue(StatDefOf.ShootingAccuracyPawn) : 0;

        public Building_TurretCapacity()
        {
            this.innerContainer = new ThingOwner<Thing>(this, false, LookMode.Deep);
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn myPawn)
        {
            foreach (FloatMenuOption floatMenuOption1 in base.GetFloatMenuOptions(myPawn))
            {
                yield return floatMenuOption1;
            }
            foreach (FloatMenuOption floatMenuOption2 in BuildingTurretCapacityUtil.GetFloatMenuOptions(myPawn, this, this.innerContainer))
            {
                yield return floatMenuOption2;
            }
            yield break;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, this.GetDirectlyHeldThings());
        }
        protected override void Tick()
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
                    stringBuilder.AppendLine("FFF.CurrentOperator".Translate() + ": " + PawnInside.Name);
                }
            }
            else
            {
                stringBuilder.AppendLine("FFF.RequireOperator".Translate());
            }
            return stringBuilder.ToString().TrimEndNewlines();
        }

        private bool ShouldGetOut()
        {
            if (PawnInside?.needs.food != null)
            {
                //只有在這兩個同時都小於10%時會自動出來
                if (PawnInside.needs.food.CurLevel <= 0.1f && PawnInside.needs.rest.CurLevel <= 0.1f)
                {
                    return true;
                }
            }
            return false;
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
                    defaultLabel = "FFF.BunkerFacility_ExitText".Translate(),
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