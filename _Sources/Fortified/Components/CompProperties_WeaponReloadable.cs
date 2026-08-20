using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 與原版 <see cref="CompProperties_ApparelReloadable"/> 完全相同, 只是允許掛在「非 Apparel」的 ThingDef 上,
    /// 例如「打完即毀」的一次性槍械。
    ///
    /// 原版 <see cref="CompProperties_ApparelVerbOwner.ConfigErrors"/> 會對非 Apparel 的 parentDef 回報
    /// "Comp XXX can only be added to Apparel"。該檢查純粹是 log 層級的限制:
    /// <see cref="CompApparelVerbOwner_Charged.UsedOnce"/>(扣充能 + destroyOnEmpty 時 Destroy) 是由
    /// <see cref="Verb_LaunchProjectile.TryCastShot"/> 透過 EquipmentSource.GetComp&lt;CompApparelVerbOwner_Charged&gt;()
    /// 呼叫的, 掛在武器上完全能運作。comp 內唯一與服裝相關的 Wearer 只用在 CompGetWornGizmosExtra,
    /// 而武器不會走到那條路徑, 因此這裡只把那一條錯誤訊息濾掉, 其餘檢查照舊。
    ///
    /// compClass 仍維持 <see cref="CompApparelReloadable"/>, 讓其他模組 / CE 以類別比對時行為不變。
    /// </summary>
    public class CompProperties_WeaponReloadable : CompProperties_ApparelReloadable
    {
        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            // 與原版產生訊息的方式一致, 精準比對, 避免誤吞其他錯誤
            string apparelOnlyError = $"Comp {compClass} can only be added to Apparel";

            foreach (string error in base.ConfigErrors(parentDef))
            {
                if (error == apparelOnlyError)
                    continue;

                yield return error;
            }
        }
    }
}
