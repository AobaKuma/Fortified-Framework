using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using System;

namespace Fortified
{
	[HarmonyPatch(typeof(CompChangeableProjectile), nameof(CompChangeableProjectile.Projectile), MethodType.Getter)]
	public static class Patch_CompChangeableProjectile_Projectile
	{
		[HarmonyPrefix]
		public static bool Prefix(CompChangeableProjectile __instance, ref ThingDef __result)
		{
			if(__instance is CompTurretProjectile comp)
			{
				__result = comp.ProjectileOverride;
				return false;
			}
			return true;
		}
	}

	[HarmonyPatch(typeof(CompChangeableProjectile), nameof(CompChangeableProjectile.LoadedShell), MethodType.Getter)]
	public static class Patch_CompChangeableProjectile_LoadedShell
	{
		[HarmonyPrefix]
		public static bool Prefix(CompChangeableProjectile __instance, ref ThingDef __result)
		{
			if (__instance is CompTurretProjectile comp)
			{
				__result = comp.LoadedShellOverride;
				return false;
			}
			return true;
		}
	}
}
