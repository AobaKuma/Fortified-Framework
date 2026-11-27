using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Fortified
{

    /// <summary>世界層效果器宣告自己能打的領域；沒實作的效果器只打 <see cref="CombatDomainDefOf.Air"/>。</summary>
    public interface IDomainAwareEffector
    {
        bool Engages(CombatDomainDef domain);
    }

    public static class CombatDomainUtility
    {
        /// <summary>Def 欄位沒設定（null）時一律當成空中。</summary>
        public static CombatDomainDef Resolve(CombatDomainDef domain) => domain ?? CombatDomainDefOf.Air;

        /// <summary>有彈體看彈體，否則看來襲物的目標屬性。</summary>
        public static CombatDomainDef Of(MissileConfig config, AirTargetExtension props)
        {
            if (config?.body != null) { return Resolve(config.body.domain); }
            return Resolve(props?.domain);
        }

        public static CombatDomainDef Of(IAirDefenseTrack track) => track == null ? CombatDomainDefOf.Air : Of(track.Config, track.TargetProps);

        /// <summary>地圖層被收起的來襲物：導彈看裝配，空投艙看 def 上的 <see cref="AirTargetExtension"/>。</summary>
        public static CombatDomainDef Of(Thing payload)
        {
            if (payload == null) { return CombatDomainDefOf.Air; }
            MissileConfig config = (payload as IIncomingMissile)?.Config;
            AirTargetExtension props = payload.def?.GetModExtension<AirTargetExtension>();
            if (config?.body != null) { return Resolve(config.body.domain); }
            if (props != null) { return Resolve(props.domain); }
            // 空投艙的 skyfaller 沒有屬性時，看艙裡的東西（例如其他 mod 的突擊艇建築）。
            if (payload is Skyfaller skyfaller && skyfaller.innerContainer.Count > 0)
            {
                AirTargetExtension inner = skyfaller.innerContainer[0].def?.GetModExtension<AirTargetExtension>();
                if (inner != null) { return Resolve(inner.domain); }
            }
            return CombatDomainDefOf.Air;
        }

        /// <summary>空清單 = 只有空中（DMSE 既有內容不必設定）。</summary>
        public static bool Covers(List<CombatDomainDef> domains, CombatDomainDef domain)
            => domains.NullOrEmpty() ? Resolve(domain) == CombatDomainDefOf.Air : domains.Contains(Resolve(domain));

        public static bool Engages(Fortified.IAirEffector effector, CombatDomainDef domain)
            => effector is IDomainAwareEffector aware ? aware.Engages(Resolve(domain)) : Resolve(domain) == CombatDomainDefOf.Air;
    }
}
