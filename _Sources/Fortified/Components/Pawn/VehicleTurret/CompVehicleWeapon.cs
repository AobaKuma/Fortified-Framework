﻿using System;
using System.Collections.Generic;
using Verse;
using UnityEngine;
using RimWorld;

namespace Fortified
{
    public class CompVehicleWeapon : ThingComp
    {
        public Pawn pawn;

        public float CurrentAngle => _currentAngle;
        public float TargetAngle
        {
            get
            {
                if (pawn.stances.curStance is Stance_Busy busy && busy.focusTarg.IsValid)
                {
                    Vector3 targetPos;
                    if (busy.focusTarg.HasThing)
                    {
                        targetPos = busy.focusTarg.Thing.DrawPos;
                    }
                    else
                    {
                        targetPos = busy.focusTarg.Cell.ToVector3Shifted();
                    }
					return (targetPos - pawn.DrawPos).AngleFlat();
                }
                return _turretFollowingAngle;
            }
        }

        private float _turretFollowingAngle = 0f;

        private float _turretAnglePerFrame = 0.1f;

        private float _currentAngle = 0f;
        private float _rotationSpeed = 0f;

        private Rot4 _lastRotation;

        public CompProperties_VehicleWeapon Props => (CompProperties_VehicleWeapon)props;
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            pawn = parent as Pawn;

            if (pawn == null)
            {
                Log.Error("The CompVehicleWeapon is set on a non-pawn object.");
                return;
            }
            if (pawn.equipment.Primary == null && Props.defaultWeapon != null)
            {
                Thing weapon = ThingMaker.MakeThing(Props.defaultWeapon);
                pawn.equipment.AddEquipment((ThingWithComps)weapon);
            }
        }
        public override void CompTickInterval(int delta)
        {
            base.CompTickInterval(delta);
            if (pawn == null) return;

            if (Props.turretRotationFollowPawn)
            {
                _turretFollowingAngle = pawn.Rotation.AsAngle + Props.drawData.RotationOffsetForRot(pawn.Rotation);
            }
            else
            {
                _turretFollowingAngle += _turretAnglePerFrame;
            }
            if (_lastRotation != pawn.Rotation)
            {
                _lastRotation = pawn.Rotation;
                _currentAngle = _turretFollowingAngle;
            }
            _currentAngle = Mathf.SmoothDampAngle(_currentAngle, TargetAngle, ref _rotationSpeed, Props.rotationSmoothTime * delta);
        }
        public override void CompTickRare()
        {
            base.CompTickRare();
            _turretAnglePerFrame = Rand.Range(-0.5f, 0.5f);
        }

        public Vector3 GetOffsetByRot()
        {
            if (Props.drawData != null)
            {
                return Props.drawData.OffsetForRot(pawn.Rotation);
            }
            return Vector3.zero;
        }
    }
    public class CompProperties_VehicleWeapon : CompProperties
    {
        public CompProperties_VehicleWeapon()
        {
            this.compClass = typeof(CompVehicleWeapon);
        }
        public DrawData drawData;
        public bool turretRotationFollowPawn = false;
        public bool horizontalFlip = false;
        public float rotationSmoothTime = 0.12f;
        public ThingDef defaultWeapon;
        public float drawSize = 0f;
    }
}
