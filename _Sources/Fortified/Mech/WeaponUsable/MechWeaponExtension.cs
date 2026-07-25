using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Fortified
{
    public class MechWeaponExtension : DefModExtension
    {
        public bool EnableWeaponFilter = true;  //根據WeaponTag
        public List<string> UsableWeaponTags = new List<string>();

        public bool EnableTechLevelFilter = false; //根據科技等級
        public List<TechLevel> UsableTechLevels = new List<TechLevel>();

        public bool EnableClassFilter = false; //根據文化分類
        public List<WeaponClassDef> UsableWeaponClasses = new List<WeaponClassDef>();

        public List<string> BypassUsableWeapons = new List<string>();

        /// <summary>
        /// 處理以下狀況：
        /// 1.可直接使用的武器白名單
        /// 2.啟用武器標籤篩選，且武器標籤符合
        /// 3.啟用科技等級篩選，且武器科技等級符合
        /// 4.啟用武器分類篩選，且武器分類符合
        /// 5.武器沒有被HeavyEquippableExtension限制體型
        /// 6.以上皆符合則回傳true，否則false
        /// </summary>
        public bool CanUse(ThingWithComps weapon)
        {
            if (BypassUsableWeapons.Contains(weapon.def.defName)) return true;
            if (EnableWeaponFilter)
            {
                if (weapon.def.weaponTags.NullOrEmpty())
                {
                    return false;
                }
                if (UsableWeaponTags.NullOrEmpty()) Log.Warning("MechWeaponExtension has EnableWeaponFilter enabled but UsableWeaponTags is empty!");
                bool tagMatch = false;
                foreach (string tag in UsableWeaponTags)
                {
                    if (weapon.def.weaponTags.Contains(tag))
                    {
                        tagMatch = true;
                        break;
                    }
                }
                if (!tagMatch) return false;
            }
            if (EnableTechLevelFilter && !UsableTechLevels.Contains(weapon.def.techLevel))
            {
                return false;
            }
            if (EnableClassFilter)
            {
                if (weapon.def.weaponClasses.NullOrEmpty() || !weapon.def.weaponClasses.ContainsAny(p => UsableWeaponClasses.Contains(p)))
                {
                    return false;
                }
            }
            return true;
        }

        public bool CanUseAsHeavyWeapon(ThingWithComps weapon, float PawnBodysize = 1)//這裡是因為沒法再ModExt獲取到對象BosySize，所以只能透過這個方式在UseableInRuntime檢查。
        {
            if (weapon.def.TryGetModExtension<HeavyEquippableExtension>(out var ext))
            {
                if (ext.EquippableDef.EquippableBaseBodySize == -1) return false;//無體型限制的武器不適用於此處理。
                return ext.EquippableDef.EquippableBaseBodySize < PawnBodysize;
            }
            return true;
        }
    }
}