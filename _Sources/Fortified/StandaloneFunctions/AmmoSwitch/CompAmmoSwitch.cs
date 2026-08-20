using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Fortified
{
    // CompProperties
    public class CompProperties_AmmoSwitch : CompProperties
    {
        public List<AmmoOption> ammos = new List<AmmoOption>();
        public int defaultIndex = -1;
        public int switchCooldown = 90;
        public SoundDef soundSwitch;
        public bool includeDefaultAmmo = true;

        public CompProperties_AmmoSwitch()
        {
            compClass = typeof(CompAmmoSwitch);
        }

        /// <summary>Lowest index a <see cref="CompAmmoSwitch"/> built from these props may select.</summary>
        public int MinSelectableIndex => includeDefaultAmmo ? -1 : 0;

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string err in base.ConfigErrors(parentDef))
            {
                yield return err;
            }

            if (ammos == null || ammos.Count == 0)
            {
                yield return includeDefaultAmmo
                    ? "CompProperties_AmmoSwitch has no ammo options."
                    : "CompProperties_AmmoSwitch has includeDefaultAmmo=false but no ammo options; nothing would ever be selectable.";
                yield break;
            }

            for (int i = 0; i < ammos.Count; i++)
            {
                AmmoOption ammo = ammos[i];
                if (ammo == null)
                {
                    yield return $"CompProperties_AmmoSwitch has a null ammo option at index {i}.";
                    continue;
                }
                if (!ammo.useDefaultProjectile && ammo.projectileDef == null)
                {
                    yield return $"CompProperties_AmmoSwitch ammo option {i} ({ammo.label ?? "unlabelled"}) has no projectileDef while useDefaultProjectile is false.";
                }
            }

            // defaultIndex == -1 stays legal even with includeDefaultAmmo=false: it is clamped to the
            // first option at runtime so existing defs keep loading without edits.
            if (defaultIndex < -1 || defaultIndex >= ammos.Count)
            {
                yield return $"CompProperties_AmmoSwitch defaultIndex {defaultIndex} is out of range [-1, {ammos.Count - 1}].";
            }

            if (switchCooldown < 0)
            {
                yield return $"CompProperties_AmmoSwitch switchCooldown {switchCooldown} is negative.";
            }
        }
    }
	public class CompAmmoSwitch: ThingComp
	{
		private int selectedIndex;
		/// <summary>
		/// Stores the target ammo index when a pawn is switching ammo via job.
		/// Reset after job completion or can be checked by job driver.
		/// </summary>
		private int switchingToIndex;

		private int cooldownUntilTick;

        public CompProperties_AmmoSwitch Props => (CompProperties_AmmoSwitch)props;

        public bool HasAnyAmmoOption => Props?.ammos != null && Props.ammos.Count > 0;
        public int OptionCount => HasAnyAmmoOption ? Props.ammos.Count : 0;
        public int SelectedIndex => selectedIndex;

		public int SwitchingToIndex => switchingToIndex;

        /// <summary>
        /// Whether the implicit "default ammo" entry (index -1) may be shown and selected.
        /// Defaults to true when props are missing so a malformed def degrades to the old behaviour.
        /// </summary>
        public bool AllowsDefaultAmmo => Props?.includeDefaultAmmo ?? true;

        /// <summary>Lowest index this comp may hold: -1 when the default entry is allowed, otherwise 0.</summary>
        public int MinSelectableIndex => AllowsDefaultAmmo ? -1 : 0;

        /// <summary>Clamps an arbitrary index into the currently legal range.</summary>
        public int NormalizeIndex(int index)
        {
            if (!HasAnyAmmoOption) return -1;
            return Mathf.Clamp(index, MinSelectableIndex, Props.ammos.Count - 1);
        }

		public AmmoOption CurrentAmmo
        {
            get
            {
                if (!HasAnyAmmoOption) return null;
                // -1 means using the weapon's verb default projectile (no AmmoOption)
                int idx = NormalizeIndex(selectedIndex);
                if (idx < 0) return null;
                return Props.ammos[idx];
            }
        }

        public override IEnumerable<FloatMenuOption> CompMultiSelectFloatMenuOptions(IEnumerable<Pawn> selPawns)
        {
            if (!HasAnyAmmoOption) yield break;

            // collect selected comps of same def
            List<CompAmmoSwitch> selectedComps = new List<CompAmmoSwitch>();
            foreach (object obj in Find.Selector.SelectedObjects)
            {
                if (obj is Thing t && t.def == parent.def)
                {
                    var c = t.TryGetComp<CompAmmoSwitch>();
                    if (c != null) selectedComps.Add(c);
                }
            }
            if (selectedComps.Count == 0) yield break;

            // if selPawns provided, map each comp to first pawn that can reach it
            Dictionary<CompAmmoSwitch, Pawn> compPawnMap = new Dictionary<CompAmmoSwitch, Pawn>();
            List<Pawn> selPawnList = selPawns?.ToList() ?? new List<Pawn>();
            if (selPawnList.Any())
            {
                foreach (var c in selectedComps)
                {
                    Pawn found = selPawnList.FirstOrDefault(p => p.CanReach(c.parent, PathEndMode.Touch, Danger.Deadly));
                    if (found != null) compPawnMap[c] = found;
                }
                if (compPawnMap.Count == 0)
                {
                    yield return new FloatMenuOption("CannotSwitchAmmo".Translate(parent.Label) + ": " + "NoPath".Translate().CapitalizeFirst(), null);
                    yield break;
                }
            }

            string label = "FFF.AmmoSwitch.Label".Translate(CurrentLabel);
            yield return new FloatMenuOption(label, delegate
            {
                List<FloatMenuOption> list = new List<FloatMenuOption>();

                var baseVerb = parent?.GetComp<CompEquippable>()?.AllVerbs?.FirstOrDefault() as Verb_LaunchProjectile;
                ThingDef baseProjectile = baseVerb?.Projectile;

                // default projectile option — omitted when the def opts out of the implicit default entry
                if (AllowsDefaultAmmo)
                {
                    list.Add(new FloatMenuOption("FFF.AmmoSwitch.DefaultAmmo".Translate(), delegate
                    {
                        if (compPawnMap.Count > 0)
                        {
                            foreach (var kv in compPawnMap)
                            {
                                kv.Key.QueueSwitchJob(kv.Value, -1);
                            }
                        }
                        else
                        {
                            foreach (var c in selectedComps) c.SetAmmo(-1, startCooldown: true);
                        }
                    }, baseProjectile?.uiIcon ?? BaseContent.BadTex, Color.white, extraPartWidth: 29f, extraPartOnGUI: (Rect r) => Widgets.InfoCardButton(r.x + 5f, r.y + (r.height - 24f) / 2f, baseProjectile)));
                }

                for (int i = 0; i < OptionCount; i++)
                {
                    int idx = i;
                    AmmoOption ammo = GetAmmoAt(idx);
                    if (ammo == null) continue;
                    ThingDef projectileForCard = ammo.useDefaultProjectile ? baseProjectile : ammo.projectileDef;
                    list.Add(new FloatMenuOption(ammo.ResolveLabel(), delegate
                    {
                        if (compPawnMap.Count > 0)
                        {
                            foreach (var kv in compPawnMap)
                            {
                                kv.Key.QueueSwitchJob(kv.Value, idx);
                            }
                        }
                        else
                        {
                            foreach (var c in selectedComps) c.SetAmmo(idx, startCooldown: true);
                        }
                    }, ammo.ResolveIcon(), Color.white, extraPartWidth: 29f, extraPartOnGUI: (Rect r) => Widgets.InfoCardButton(r.x + 5f, r.y + (r.height - 24f) / 2f, projectileForCard)));
                }

                if (list.Count == 0) return;
                Find.WindowStack.Add(new FloatMenu(list));
            }, MenuOptionPriority.Default, null, null, 0f, null, null, true, 0);
        }

        public void QueueSwitchJob(Pawn pawn, int idx)
        {
            if (pawn?.jobs == null || parent == null || !HasAnyAmmoOption) return;
            switchingToIndex = NormalizeIndex(idx);
            pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(FFF_DefOf.FFF_SwitchAmmo, parent), JobTag.Misc);
        }

        public ThingDef CurrentProjectile
        {
            get
            {
                if (CurrentAmmo == null) return null;
                // If this ammo option uses default projectile, return null to let Verb use base projectile
                if (CurrentAmmo.useDefaultProjectile) return null;
                return CurrentAmmo.projectileDef;
            }
        }

        /// <summary>
        /// Gets whether the current ammo option uses the weapon's default projectile.
        /// </summary>
        public bool IsUsingDefaultProjectile
        {
            get
            {
                // If the effective index is -1 we are explicitly using the verb's default projectile
                if (IsOnImplicitDefault) return true;
                return CurrentAmmo?.useDefaultProjectile ?? false;
            }
        }

        /// <summary>
        /// True when the comp currently rests on the implicit default entry (index -1). Always false
        /// when <see cref="AllowsDefaultAmmo"/> is false, since that index is then not selectable.
        /// </summary>
        public bool IsOnImplicitDefault => NormalizeIndex(selectedIndex) < 0;

        public string CurrentLabel
        {
            get
            {
                if (IsOnImplicitDefault)
                {
                    return "FFF.AmmoSwitch.DefaultAmmo".Translate();
                }
                return CurrentAmmo?.ResolveLabel() ?? "N/A";
            }
        }
        public Texture2D CurrentIcon
        {
            get
            {
                if (IsOnImplicitDefault)
                {
                    // Try to use the base verb projectile icon if available
                    var verb = parent?.GetComp<CompEquippable>()?.AllVerbs?.FirstOrDefault() as Verb_LaunchProjectile;
                    return verb?.Projectile?.uiIcon ?? BaseContent.BadTex;
                }
                return CurrentAmmo?.ResolveIcon() ?? BaseContent.BadTex;
            }
        }

        public bool IsOnSwitchCooldown
        {
            get
            {
                if (Find.TickManager == null) return false;
                return Find.TickManager.TicksGame < cooldownUntilTick;
            }
        }

		public override float GetStatFactor(StatDef stat)
		{
			if (CurrentAmmo != null && !Mathf.Approximately(CurrentAmmo.accuracyFactor, 1f) && stat.defName.StartsWith("Accuracy"))
			{
				return CurrentAmmo.accuracyFactor;
			}
			return base.GetStatFactor(stat);
		}
        public void PlaySound(SoundInfo soundInfo)
        {
            if (Props?.soundSwitch != null)
            {
                Props.soundSwitch.PlayOneShot(soundInfo);
            }
            else
            {
                parent.def.soundInteract?.PlayOneShot(soundInfo);
            }
        }
		public override void GetStatsExplanation(StatDef stat, StringBuilder sb, string whitespace = "")
		{
			if (CurrentAmmo != null && !Mathf.Approximately(CurrentAmmo.accuracyFactor, 1f) && stat.defName.StartsWith("Accuracy"))
			{
				sb.AppendLine();
				sb.AppendLine(whitespace + "FFF.AmmoSwitch.StatFactor".Translate() + ": x" + CurrentAmmo.accuracyFactor.ToStringByStyle(ToStringStyle.PercentZero));
			}
		}

        public AmmoOption GetAmmoAt(int index)
        {
            if (!HasAnyAmmoOption) return null;
            if (index < 0 || index >= Props.ammos.Count) return null;
            return Props.ammos[index];
        }

        public void SetAmmo(int index, bool startCooldown = true)
        {
            if (!HasAnyAmmoOption) return;

            // -1 represents 'use verb default projectile'; it is only reachable when includeDefaultAmmo is true.
            int clamped = NormalizeIndex(index);
            bool changed = clamped != selectedIndex;
            selectedIndex = clamped;

            if (changed && startCooldown)
            {
                if (Props.switchCooldown > 0 && Find.TickManager != null)
                    cooldownUntilTick = Find.TickManager.TicksGame + Props.switchCooldown;
            }
        }

        public string GetAmmoTooltip(int index)
        {
            // Special-case for -1: represent using the weapon/verb default projectile
            if (index < 0)
            {
                var verb = parent?.GetComp<CompEquippable>()?.AllVerbs?.FirstOrDefault() as Verb_LaunchProjectile;
                string baseProjText = verb?.Projectile?.LabelCap ?? "FFF.AmmoSwitch.DefaultProjectile".Translate();
                string label = "FFF.AmmoSwitch.DefaultAmmo".Translate();
                return "FFF.AmmoSwitch.AmmoTooltip".Translate(label, baseProjText);
            }

            AmmoOption ammo = GetAmmoAt(index);
            if (ammo == null) return "N/A";

            string projText;
            if (ammo.useDefaultProjectile)
            {
                projText = "FFF.AmmoSwitch.DefaultProjectile".Translate();
            }
            else
            {
                projText = ammo.projectileDef != null
                    ? ammo.projectileDef.LabelCap
                    : "N/A";
            }

            return "FFF.AmmoSwitch.AmmoTooltip".Translate(ammo.ResolveLabel(), projText);
        }

        public string GetGizmoDesc()
        {
            var sb = new StringBuilder();
            // If explicitly using verb default (effective index == -1), show a simple description
            if (IsOnImplicitDefault)
            {
                sb.AppendLine("FFF.AmmoSwitch.Desc".Translate(CurrentLabel));
                sb.AppendLine();
                sb.AppendLine("[" + "FFF.AmmoSwitch.UsingDefault".Translate() + "]");
                return sb.ToString().TrimEnd();
            }

            if (CurrentAmmo == null) return "N/A";
            sb.AppendLine("FFF.AmmoSwitch.Desc".Translate(CurrentLabel));
            sb.AppendLine(CurrentAmmo.description ?? "");

            // Add note if using default projectile (either via AmmoOption flag)
            if (IsUsingDefaultProjectile)
            {
                sb.AppendLine();
                sb.AppendLine("[" + "FFF.AmmoSwitch.UsingDefault".Translate() + "]");
            }

            return sb.ToString().TrimEnd();
        }

        public override void PostPostMake()
        {
            base.PostPostMake();
            // NormalizeIndex pins the start index inside the legal range, so a def that leaves
            // defaultIndex at -1 while forbidding the default entry starts on the first option.
            selectedIndex = HasAnyAmmoOption ? NormalizeIndex(Props.defaultIndex) : -1;
            switchingToIndex = selectedIndex;
        }

		public virtual Gizmo GetSwitchGizmo(Thing user)
		{
			Command_Action command = new Command_Action
			{
				defaultLabel = "FFF.AmmoSwitch.Label".Translate(CurrentLabel),
				defaultDesc = GetGizmoDesc(),
				icon = CurrentIcon
			};
			command.action = delegate
			{
				List<FloatMenuOption> list = new List<FloatMenuOption>();
                // Add an option for using the weapon's base/verb default projectile,
                // unless the def opted out of the implicit default entry.
                if (AllowsDefaultAmmo)
                {
                    var baseVerb = parent?.GetComp<CompEquippable>()?.AllVerbs?.FirstOrDefault() as Verb_LaunchProjectile;
                    ThingDef baseProjectile = baseVerb?.Projectile;
                    FloatMenuOption defaultOption = new FloatMenuOption("FFF.AmmoSwitch.DefaultAmmo".Translate(), delegate
                    {
                        if (user is Pawn pawn)
                        {
                            QueueSwitchJob(pawn, -1);
                        }
                        else
                        {
                            SetAmmo(-1, startCooldown: true);
                        }
                    }, baseProjectile?.uiIcon ?? BaseContent.BadTex, Color.white, extraPartWidth: 29f, extraPartOnGUI: (Rect r) => Widgets.InfoCardButton(r.x + 5f, r.y + (r.height - 24f) / 2f, baseProjectile));
                    defaultOption.tooltip = new TipSignal(GetAmmoTooltip(-1));
                    if (SelectedIndex == -1) defaultOption.Disabled = true;
                    list.Add(defaultOption);
                }
				for (int i = 0; i < OptionCount; i++)
				{
					int idx = i;
					AmmoOption ammo = GetAmmoAt(idx);
					if (ammo == null) continue;
					string label = ammo.ResolveLabel();
					Texture2D icon = ammo.ResolveIcon();

					// Get projectile for info card: use ammo's projectile if not default, otherwise try base verb projectile
					ThingDef projectileForCard = null;
					if (!ammo.useDefaultProjectile)
					{
						projectileForCard = ammo.projectileDef;
					}
					else
					{
						// Try to get base projectile from parent weapon's verb
						var verb = parent?.GetComp<CompEquippable>()?.AllVerbs?.FirstOrDefault() as Verb_LaunchProjectile;
						if (verb != null)
						{
							projectileForCard = verb.Projectile;
						}
					}

					FloatMenuOption option = new FloatMenuOption(label, delegate
					{
						if(user is Pawn pawn)
						{
							QueueSwitchJob(pawn, idx);
						}
						else
						{
							SetAmmo(idx, startCooldown: true);
						}
					}, icon, Color.white, extraPartWidth: 29f, extraPartOnGUI: (Rect r) => Widgets.InfoCardButton(r.x + 5f, r.y + (r.height - 24f) / 2f, projectileForCard));
					option.tooltip = new TipSignal(GetAmmoTooltip(idx));
					if (idx == SelectedIndex)
					{
						option.Disabled = true;
					}
					list.Add(option);
				}
				if (list.Count == 0) return;
				Find.WindowStack.Add(new FloatMenu(list));
			};
			return command;
		}

        public override void PostExposeData()
        {
            base.PostExposeData();
            int defaultIdx = Props?.defaultIndex ?? -1;
            Scribe_Values.Look(ref selectedIndex, "selectedIndex", defaultIdx);
            Scribe_Values.Look(ref cooldownUntilTick, "cooldownUntilTick", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Saves made before includeDefaultAmmo was turned off (or before the ammo list shrank)
                // can carry an index that is no longer legal; pull it back into range on load.
                selectedIndex = HasAnyAmmoOption ? NormalizeIndex(selectedIndex) : -1;
                switchingToIndex = selectedIndex;
            }
        }
    }

	public class CompProperties_SwitchableAmmo : CompProperties
	{
		public CompProperties_SwitchableAmmo()
		{
			compClass = typeof(CompSwitchableAmmo);
		}

		public override IEnumerable<StatDrawEntry> SpecialDisplayStats(StatRequest req)
		{
            ThingDef def = req.Thing?.def ?? (req.Def as ThingDef);
			if(def == null || def.projectile == null)
            {
                yield break;
            }
			StatCategoryDef statCat = StatCategoryDefOf.Weapon_Ranged;
			if (def.projectile.damageDef != null && def.projectile.damageDef.harmsHealth)
			{
				StringBuilder stringBuilder2 = new StringBuilder();
				stringBuilder2.AppendLine("Stat_Thing_Damage_Desc".Translate());
				stringBuilder2.AppendLine();
				float num3 = def.projectile.GetDamageAmount(req.Thing, stringBuilder2);
				yield return new StatDrawEntry(statCat, "Damage".Translate(), num3.ToString(), stringBuilder2.ToString(), 5500);
				if (def.projectile.damageDef.armorCategory != null)
				{
					StringBuilder stringBuilder3 = new StringBuilder();
					float armorPenetration = def.projectile.GetArmorPenetration(req.Thing, stringBuilder3);
					TaggedString taggedString = "ArmorPenetrationExplanation".Translate();
					if (stringBuilder3.Length != 0)
					{
						taggedString += "\n\n" + stringBuilder3;
					}
					yield return new StatDrawEntry(statCat, "ArmorPenetration".Translate(), armorPenetration.ToStringPercent(), taggedString, 5400);
				}
				float buildingDamageFactor = def.projectile.damageDef.buildingDamageFactor;
				float dmgBuildingsImpassable = def.projectile.damageDef.buildingDamageFactorImpassable;
				float dmgBuildingsPassable = def.projectile.damageDef.buildingDamageFactorPassable;
				if (buildingDamageFactor != 1f)
				{
					yield return new StatDrawEntry(statCat, "BuildingDamageFactor".Translate(), buildingDamageFactor.ToStringPercent(), "BuildingDamageFactorExplanation".Translate(), 5410);
				}
				if (dmgBuildingsImpassable != 1f)
				{
					yield return new StatDrawEntry(statCat, "BuildingDamageFactorImpassable".Translate(), dmgBuildingsImpassable.ToStringPercent(), "BuildingDamageFactorImpassableExplanation".Translate(), 5420);
				}
				if (dmgBuildingsPassable != 1f)
				{
					yield return new StatDrawEntry(statCat, "BuildingDamageFactorPassable".Translate(), dmgBuildingsPassable.ToStringPercent(), "BuildingDamageFactorPassableExplanation".Translate(), 5430);
				}
			}
			float stoppingPower = def.projectile.stoppingPower;
			if (stoppingPower > 0f)
			{
				StringBuilder stoppingPowerExplanation = new StringBuilder("StoppingPowerExplanation".Translate());
				stoppingPowerExplanation.AppendLine();
				stoppingPowerExplanation.AppendLine();
				stoppingPowerExplanation.AppendLine("StatsReport_BaseValue".Translate() + ": " + stoppingPower.ToString("F1"));
				stoppingPowerExplanation.AppendLine();
				stoppingPowerExplanation.AppendLine();
				stoppingPowerExplanation.AppendLine("StatsReport_FinalValue".Translate() + ": " + stoppingPower.ToString("F1"));
				yield return new StatDrawEntry(statCat, "StoppingPower".Translate(), stoppingPower.ToString("F1"), stoppingPowerExplanation.ToString(), 5402);
			}
		}
	}
	public class CompSwitchableAmmo : ThingComp
	{
	}

	/*public class Command_AmmoSwitch : Command_Action
    {
        public CompAmmoSwitch comp;
        public LocalTargetInfo messageTarget;

        public void OpenCurrentProjectileInfoCard()
        {
            if (comp?.CurrentProjectile == null)
            {
                Messages.Message("目前彈種未設定投射物。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.WindowStack.Add(new Dialog_InfoCard(comp.CurrentProjectile));
        }

        public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions
        {
            get
            {
                if (comp == null || !comp.HasAnyAmmoOption) yield break;

                for (int i = 0; i < comp.OptionCount; i++)
                {
                    int idx = i;
                    AmmoOption ammo = comp.GetAmmoAt(idx);

                    if (ammo == null) continue;
                    if (idx == comp.SelectedIndex) continue;

                    string label = ammo.ResolveLabel();

                    FloatMenuOption opt;
                    Texture2D icon = ammo.ResolveIcon();
                    if (icon != null)
                    {
                        opt = new FloatMenuOption(label, () => SelectAmmo(idx), icon, Color.white);
                    }
                    else
                    {
                        opt = new FloatMenuOption(label, () => SelectAmmo(idx));
                    }

                    opt.tooltip = new TipSignal(comp.GetAmmoTooltip(idx));
                    yield return opt;
                }
            }
        }

        private void SelectAmmo(int index)
        {
            comp.SetAmmo(index, startCooldown: true);
        }
    }*/
}