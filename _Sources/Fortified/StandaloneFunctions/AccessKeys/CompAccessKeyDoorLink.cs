using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Fortified
{
	/// <summary>
	/// 讓 <see cref="CompBuildingMover"/> 感應門變成「門禁鑰匙才能開」的一端。
	/// Makes a <see cref="CompBuildingMover"/> sensor door the wanter side of an access-key link.
	///
	/// 掛上這個 comp 之後，門本體就是 <see cref="IAccessKeyWanter"/>，
	/// 可直接被 <see cref="AccessKeyLinkUtility.TryLink"/> / <see cref="Task_LinkAccessKeyWanter"/> 連上，
	/// 不需要另外做 thingClass，也能跟既有的感應門設定共存。
	/// With this comp the door itself is an <see cref="IAccessKeyWanter"/>, so
	/// <see cref="AccessKeyLinkUtility"/> can link it without a dedicated thingClass.
	/// </summary>
	public class CompProperties_AccessKeyDoorLink : CompProperties
	{
		/// <summary>被連結時才上鎖；沒有 activatable 連上的門維持普通感應門。
		/// Lock only once something links to it; unlinked doors stay ordinary sensor doors.</summary>
		public bool lockOnLink = true;

		/// <summary>生成即上鎖，不管有沒有連結。Locked from spawn regardless of links.</summary>
		public bool lockedByDefault = false;

		/// <summary>解鎖後立刻開到底。Slide fully open the moment it unlocks.</summary>
		public bool openOnUnlock = true;

		/// <summary>解鎖後把門轉給用鑰匙者的陣營，否則 sensorDoorCheckFaction 會把他自己擋在外面。
		/// Hand the door to the unlocking pawn's faction, or the faction check keeps them locked out.</summary>
		public bool takeFactionFromUser = true;

		/// <summary>上鎖時疊在門上的貼圖，留空則不畫。Overlay drawn while locked; empty = none.</summary>
		public string lockedTexPath;

		/// <summary>解鎖音效。Sound played on unlock.</summary>
		public SoundDef unlockSound;

		public CompProperties_AccessKeyDoorLink()
		{
			compClass = typeof(CompAccessKeyDoorLink);
		}

		public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
		{
			foreach (string e in base.ConfigErrors(parentDef))
			{
				yield return e;
			}
			if (parentDef.comps == null || !parentDef.comps.Any(c => c is CompProperties_BuildingMover))
			{
				yield return parentDef.defName + " 掛了 CompProperties_AccessKeyDoorLink 但沒有 CompProperties_BuildingMover，無門可鎖";
			}
		}
	}

	/// <summary>
	/// 上鎖時把 <see cref="CompBuildingMover"/> 整個停用：感應門註銷、寻路退回 Impassable，
	/// 任何陣營都推不開；用掉所有連結的鑰匙之後才解鎖並開門。
	/// While locked the mover comp is disabled outright — the sensor door deregisters and the
	/// building falls back to plain Impassable, so nobody walks through. It unlocks once every
	/// linked access key has been spent.
	/// </summary>
	public class CompAccessKeyDoorLink : ThingComp, IAccessKeyWanter
	{
		/// <summary>解鎖時對自己廣播的 comp 訊號。Comp signal broadcast on unlock.</summary>
		public const string UnlockedSignal = "FFF_DoorUnlockedByAccessKey";

		// 目前是否上鎖
		private bool locked;

		// 已經被鑰匙解過鎖，之後不再重新上鎖
		private bool unlocked;

		// 還差幾把鑰匙；每建立一次連結 +1，每次使用 -1
		private int remainingKeys;

		private CompBuildingMover moverInt;

		private Graphic lockedGraphicInt;

		public CompProperties_AccessKeyDoorLink Props => (CompProperties_AccessKeyDoorLink)props;

		public bool Locked => locked;

		public bool Unlocked => unlocked;

		public int RemainingKeys => remainingKeys;

		/// <summary>優先取感應門用的 mover，沒有才退而求其次。Prefers the sensor-door mover.</summary>
		private CompBuildingMover Mover
		{
			get
			{
				if (moverInt != null)
				{
					return moverInt;
				}
				if (parent is ThingWithComps twc)
				{
					CompBuildingMover fallback = null;
					foreach (CompBuildingMover c in twc.GetComps<CompBuildingMover>())
					{
						if (c.Props.sensorDoor)
						{
							moverInt = c;
							return moverInt;
						}
						fallback ??= c;
					}
					moverInt = fallback;
				}
				return moverInt;
			}
		}

		public override void PostSpawnSetup(bool respawningAfterLoad)
		{
			base.PostSpawnSetup(respawningAfterLoad);
			if (!respawningAfterLoad && Props.lockedByDefault && !unlocked)
			{
				locked = true;
			}
			ApplyLockState();
		}

		public override void CompTick()
		{
			base.CompTick();
			// 滑動中的鎖定請求會被 mover 退回，這裡負責補上
			// SetDisabled refuses while sliding; retry until the state sticks.
			if (!locked)
			{
				return;
			}
			CompBuildingMover mover = Mover;
			if (mover != null && !mover.Disabled)
			{
				mover.SetDisabled(true);
			}
		}

		private void ApplyLockState()
		{
			Mover?.SetDisabled(locked);
		}

		// ---------- IAccessKeyWanter ----------

		/// <summary>同一扇門可被多個 activatable 連結，這裡累計需要的鑰匙數。
		/// One door can be linked by several activatables, so the requirement accumulates.</summary>
		public void Notify_LinkedTo(IAccessKeyActivatable activatable)
		{
			if (unlocked)
			{
				return;
			}
			remainingKeys++;
			if (Props.lockOnLink && !locked)
			{
				locked = true;
				ApplyLockState();
			}
		}

		public void Notify_AccessKeyUsed(IAccessKeyActivatable activatable, Pawn pawn = null)
		{
			if (unlocked)
			{
				return;
			}
			if (remainingKeys > 0)
			{
				remainingKeys--;
			}
			if (remainingKeys > 0)
			{
				return;
			}
			Unlock(pawn);
		}

		// --------------------------------------

		/// <summary>解鎖並開門。可由門禁以外的劇情流程直接呼叫。
		/// Unlocks and opens; safe to call from quest scripting too.</summary>
		public void Unlock(Pawn pawn = null)
		{
			if (unlocked)
			{
				return;
			}
			unlocked = true;
			locked = false;
			remainingKeys = 0;

			// 不換陣營的話 sensorDoorCheckFaction 會把解鎖的人自己擋在外面
			if (Props.takeFactionFromUser && pawn?.Faction != null
				&& parent is Building && parent.Faction != pawn.Faction)
			{
				parent.SetFaction(pawn.Faction);
			}

			CompBuildingMover mover = Mover;
			if (mover != null)
			{
				mover.SetDisabled(false);
				if (Props.openOnUnlock)
				{
					mover.RequestOpenTo(mover.MaxSensorDoorOpenDistance());
				}
			}

			Props.unlockSound?.PlayOneShot(SoundInfo.InMap(parent));
			parent.BroadcastCompSignal(UnlockedSignal);

			if (parent.Spawned)
			{
				parent.DirtyMapMesh(parent.Map);
				Messages.Message("FFF.AccessKeyDoor.Unlocked".Translate(parent.Label),
					parent, MessageTypeDefOf.PositiveEvent, historical: false);
			}
		}

		public override string CompInspectStringExtra()
		{
			if (!locked)
			{
				return null;
			}
			return remainingKeys > 0
				? "FFF.AccessKeyDoor.LockedKeysRemaining".Translate(remainingKeys)
				: "FFF.AccessKeyDoor.Locked".Translate();
		}

		public override IEnumerable<Gizmo> CompGetGizmosExtra()
		{
			foreach (Gizmo g in base.CompGetGizmosExtra())
			{
				yield return g;
			}
			if (locked && DebugSettings.ShowDevGizmos)
			{
				yield return new Command_Action
				{
					defaultLabel = "DEV: Unlock door",
					action = delegate { Unlock(); }
				};
			}
		}

		private Graphic LockedGraphic
		{
			get
			{
				if (lockedGraphicInt == null)
				{
					GraphicData data = new GraphicData();
					data.CopyFrom(parent.def.graphicData);
					data.texPath = Props.lockedTexPath;
					lockedGraphicInt = data.GraphicColoredFor(parent);
				}
				return lockedGraphicInt;
			}
		}

		public override void PostDraw()
		{
			base.PostDraw();
			if (!locked || Props.lockedTexPath.NullOrEmpty() || parent.def.graphicData == null)
			{
				return;
			}
			// 上鎖時 mover 停用不會滑動，直接畫在本體位置上方
			Vector3 loc = parent.DrawPos;
			loc.y += Altitudes.AltInc;
			LockedGraphic.Draw(loc, parent.Rotation, parent);
		}

		public override void PostExposeData()
		{
			base.PostExposeData();
			Scribe_Values.Look(ref locked, "accessKeyDoorLocked", defaultValue: false);
			Scribe_Values.Look(ref unlocked, "accessKeyDoorUnlocked", defaultValue: false);
			Scribe_Values.Look(ref remainingKeys, "accessKeyDoorRemainingKeys", defaultValue: 0);
		}
	}
}
