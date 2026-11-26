using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Fortified
{
    public class FortifiedMod : Mod
    {
        public static FortifiedSettings Settings { get; private set; }

        private Vector2 featureScroll;

        public FortifiedMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<FortifiedSettings>();
        }

        public override string SettingsCategory() => Content.Name;

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.CheckboxLabeled("FFF.Settings.ShowDevCategory".Translate(), ref Settings.showDevCategory,
                "FFF.Settings.ShowDevCategory.Desc".Translate());
            float used = listing.CurHeight;
            listing.End();

            List<FFF_FeatureDef> features = DefDatabase<FFF_FeatureDef>.AllDefsListForReading;
            if (features.Count > 0)
            {
                DoFeatureSection(new Rect(inRect.x, inRect.y + used + 12f, inRect.width, inRect.height - used - 12f), features);
            }
        }

        // 功能清單（唯讀）：每項功能一列，顯示這次啟動是否啟用、由哪些模組啟用；滑鼠移上去看各模組怎麼啟用的
        private void DoFeatureSection(Rect rect, List<FFF_FeatureDef> features)
        {
            const float RowHeight = 30f;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 32f), "FFF.Settings.Features".Translate());
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(rect.x, rect.y + 32f, rect.width, 20f), "FFF.Settings.Features.Desc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Rect outRect = new Rect(rect.x, rect.y + 56f, rect.width, rect.height - 56f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, features.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref featureScroll, viewRect);
            for (int i = 0; i < features.Count; i++)
            {
                FFF_FeatureDef feature = features[i];
                Rect row = new Rect(0f, i * RowHeight, viewRect.width, RowHeight);
                if (i % 2 == 1)
                {
                    Widgets.DrawLightHighlight(row);
                }
                bool on = FFF_FeatureRegistry.IsEnabled(feature);
                IReadOnlyList<FFF_FeatureEnabler> enablers = FFF_FeatureRegistry.EnablersOf(feature.defName);
                string blocked = FFF_FeatureRegistry.BlockedReason(feature.defName);

                Rect nameRect = new Rect(row.x + 4f, row.y, row.width * 0.35f, row.height);
                Rect usersRect = new Rect(nameRect.xMax + 8f, row.y, row.xMax - nameRect.xMax - 12f, row.height);
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = on ? Color.white : Color.gray;
                Widgets.Label(nameRect, feature.LabelCap);
                string users;
                if (on)
                {
                    users = string.Join(", ", enablers.Select(e => e.modName).Distinct());
                }
                else if (blocked != null)
                {
                    users = "FFF.Settings.Features.Blocked".Translate(blocked);
                }
                else
                {
                    users = "FFF.Settings.Features.Unused".Translate();
                }
                Widgets.Label(usersRect, users.Truncate(usersRect.width));
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;

                string tip = feature.defName + (feature.description.NullOrEmpty() ? "" : "\n\n" + feature.description);
                if (enablers.Count > 0)
                {
                    tip += "\n\n" + "FFF.Settings.Features.EnabledBy".Translate() + "\n"
                        + string.Join("\n", enablers.Select(e => "- " + e.modName + ": " + e.detail));
                }
                TooltipHandler.TipRegion(row, tip);
            }
            Widgets.EndScrollView();
        }
    }
}
