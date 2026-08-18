using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Fortified
{
    public static class MechModificationWindowUtility
    {
        private static readonly List<Func<Pawn, Window>> WindowFactories = new List<Func<Pawn, Window>>();

        public static void RegisterWindowFactory(Func<Pawn, Window> factory)
        {
            if (factory != null && !WindowFactories.Contains(factory)) WindowFactories.Add(factory);
        }

        public static bool CanOpenFor(Pawn pawn)
        {
            return pawn != null
                && !pawn.Dead
                && !pawn.Downed
                && pawn.Faction == Faction.OfPlayer
                && pawn.RaceProps?.IsMechanoid == true
                && ResearchProjectDefOf.MicroelectronicsBasics?.IsFinished == true;
        }

        public static void OpenFor(Pawn pawn)
        {
            if (!CanOpenFor(pawn)) return;
            for (int i = 0; i < WindowFactories.Count; i++)
            {
                Window window = WindowFactories[i](pawn);
                if (window != null)
                {
                    Find.WindowStack.Add(window);
                    return;
                }
            }
            Find.WindowStack.Add(new Window_MechModification(pawn));
        }
    }

    public enum MechModificationOperationKind
    {
        Install,
        Uninstall,
        Custom
    }

    public class MechModificationQueueEntry
    {
        public MechModificationOperationKind kind;
        public Thing item;
        public ThingDef itemDef;
        public BodyPartRecord part;
        public Hediff uninstallHediff;
        public bool custom;
        public bool allowEquivalentPart;
        public bool submitted;
        public string customId;
        public string customData;
    }

    public class Window_MechModification : Window
    {
        private const float FooterHeight = 40f;
        private const float LeftColumnWidth = 360f;
        private const float PortraitHeight = 240f;
        private const float PanelPadding = 8f;
        private const float ActionButtonHeight = 26f;
        private const float ActionButtonWidth = 180f;
        private const float DoubleClickInterval = 0.42f;
        private const int AvailableRefreshTicks = 300;
        private static readonly float OpenAnimationDuration = 0.22f;
        private static readonly float CloseAnimationDuration = 0.18f;

        private static List<ThingDef> standardModificationDefs;
        private static Dictionary<HediffDef, ThingDef> standardSourceByHediff;

        protected readonly Pawn Mech;
        protected readonly List<MechModificationQueueEntry> QueuedOperations = new List<MechModificationQueueEntry>();

        private Vector2 partScroll;
        private Vector2 availableScroll;
        private Vector2 queueScroll;
        private List<SlotInfo> slotCache = new List<SlotInfo>();
        private List<Thing> availableItems = new List<Thing>();
        private SlotInfo selectedSlot;
        private InstalledModification selectedInstalled;
        private BodyPartRecord availableCachePart;
        private int availableCacheTick = -999999;
        private BodyPartRecord corePart;
        private object lastClickedEntry;
        private float lastClickedTime = -999f;
        private float openStartTime;
        private float closeStartTime;
        private float closeStartProgress = 1f;
        private bool isClosing;
        private bool allowClose;
        private bool closeSound = true;

        public override Vector2 InitialSize => new Vector2(940f, 720f);

        protected BodyPartRecord SelectedPart => selectedSlot?.part;

        protected virtual Texture2D FooterIcon => null;
        protected virtual string ApplyButtonLabel => "FFF.MechModification.Apply".Translate();
        protected virtual string ResetButtonLabel => "FFF.MechModification.Reset".Translate();
        protected virtual string ReturnButtonLabel => "FFF.MechModification.Return".Translate();

        public Window_MechModification(Pawn mech) : base(new AnimatedWindowDrawing())
        {
            Mech = mech;
            forcePause = true;
            doCloseX = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            drawShadow = false;
        }

        public override void PreOpen()
        {
            base.PreOpen();
            openStartTime = Time.realtimeSinceStartup;
            closeStartTime = 0f;
            closeStartProgress = 1f;
            isClosing = false;
            allowClose = false;
            closeSound = true;
            RebuildSlotCache();
            selectedSlot = slotCache.FirstOrDefault();
            InvalidateAvailableItems();
            OnCorePreOpen();
        }

        protected virtual void OnCorePreOpen()
        {
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            if (isClosing && Time.realtimeSinceStartup - closeStartTime >= CloseAnimationDuration)
            {
                allowClose = true;
                base.Close(closeSound);
                allowClose = false;
            }
        }

        public override void ExtraOnGUI()
        {
            base.ExtraOnGUI();
            if (!drawInScreenshotMode && Find.UIRoot.screenshotMode.Active) return;
            float animation = GetAnimationProgress();
            if (animation <= 0.01f) return;

            Rect rect = windowRect;
            float height = rect.height * animation;
            Rect animatedRect = new Rect(rect.x, rect.y + (rect.height - height) * 0.5f, rect.width, height);
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(shadowAlpha * animation));
            Widgets.DrawShadowAround(animatedRect);
            GUI.color = previous;
        }

        public override bool OnCloseRequest()
        {
            if (allowClose) return true;
            BeginCloseAnimation(true);
            return false;
        }

        public override void Close(bool doCloseSound = true)
        {
            BeginCloseAnimation(doCloseSound);
        }

        public override void PostClose()
        {
            ResetOperationWorkspace();
            base.PostClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (Mech == null)
            {
                base.Close(false);
                return;
            }

            bool disableInput = GetAnimationProgress() < 1f || isClosing;
            bool previousEnabled = GUI.enabled;
            if (disableInput) GUI.enabled = false;
            DrawWindowContents(inRect);
            if (disableInput) GUI.enabled = previousEnabled;
        }

        protected virtual void DrawWindowContents(Rect inRect)
        {
            Rect contentRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - FooterHeight - 6f);
            Rect footerRect = new Rect(inRect.x, contentRect.yMax + 6f, inRect.width, FooterHeight);
            Rect leftRect = new Rect(contentRect.x, contentRect.y, LeftColumnWidth, contentRect.height);
            Rect rightRect = new Rect(leftRect.xMax + 12f, contentRect.y, contentRect.width - LeftColumnWidth - 12f, contentRect.height);
            DrawLeft(leftRect);
            DrawRight(rightRect);
            DrawFooter(footerRect);
        }

        protected virtual void DrawRight(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.2f));
            DrawModificationPanel(rect.ContractedBy(PanelPadding));
        }

        protected void DrawModificationPanel(Rect rect)
        {
            float headerHeight = ActionButtonHeight;
            float buttonGap = 4f;
            Rect buttonArea = new Rect(rect.xMax - ActionButtonWidth, rect.y, ActionButtonWidth, headerHeight);
            float halfWidth = (ActionButtonWidth - buttonGap) * 0.5f;
            Rect saveRect = new Rect(buttonArea.x, buttonArea.y, halfWidth, headerHeight);
            Rect loadRect = new Rect(saveRect.xMax + buttonGap, buttonArea.y, halfWidth, headerHeight);
            if (Widgets.ButtonText(saveRect, "FFF.MechModification.SavePreset".Translate())) OpenSavePresetDialog();
            if (Widgets.ButtonText(loadRect, "FFF.MechModification.LoadPreset".Translate())) OpenLoadPresetDialog();

            Rect titleRect = new Rect(rect.x, rect.y, rect.width - ActionButtonWidth - 6f, headerHeight);
            Widgets.Label(titleRect, selectedSlot != null
                ? "FFF.MechModification.SelectModsFor".Translate(selectedSlot.label)
                : "FFF.MechModification.SelectPartPrompt".Translate());

            Rect listsRect = new Rect(rect.x, titleRect.yMax + 4f, rect.width, rect.height - titleRect.height - 4f);
            float gap = 8f;
            float availableHeight = Mathf.Round((listsRect.height - gap) * 0.58f);
            Rect availableRect = new Rect(listsRect.x, listsRect.y, listsRect.width, availableHeight);
            Rect queueRect = new Rect(listsRect.x, availableRect.yMax + gap, listsRect.width, listsRect.height - availableHeight - gap);
            Widgets.DrawBoxSolid(availableRect, new Color(0f, 0f, 0f, 0.2f));
            Widgets.DrawBoxSolid(queueRect, new Color(0f, 0f, 0f, 0.2f));
            DrawAvailableItems(availableRect.ContractedBy(4f));
            DrawQueuedOperations(queueRect.ContractedBy(4f));
        }

        protected void QueueCustomOperation(string id, string data = null)
        {
            QueuedOperations.Add(new MechModificationQueueEntry
            {
                kind = MechModificationOperationKind.Custom,
                custom = true,
                customId = id,
                customData = data
            });
            InvalidateAvailableItems();
        }

        protected bool HasCustomOperation(string id)
        {
            return QueuedOperations.Any(entry => entry.kind == MechModificationOperationKind.Custom && entry.customId == id);
        }

        protected virtual void DrawFooter(Rect rect)
        {
            float buttonWidth = 160f;
            Rect resetRect = new Rect(rect.xMax - buttonWidth * 3f - 16f, rect.y, buttonWidth, rect.height);
            Rect applyRect = new Rect(rect.xMax - buttonWidth * 2f - 8f, rect.y, buttonWidth, rect.height);
            Rect returnRect = new Rect(rect.xMax - buttonWidth, rect.y, buttonWidth, rect.height);

            if (FooterIcon != null)
            {
                float iconSize = rect.height - 8f;
                Widgets.DrawTextureFitted(new Rect(rect.x + 4f, rect.y + 4f, iconSize, iconSize), FooterIcon, 1f);
            }
            if (Widgets.ButtonText(resetRect, ResetButtonLabel))
            {
                ResetOperationWorkspace();
                OnResetClicked();
            }
            if (Widgets.ButtonText(applyRect, ApplyButtonLabel)) ApplyAndStartJobs();
            if (Widgets.ButtonText(returnRect, ReturnButtonLabel)) Close();
        }

        protected virtual void OnResetClicked()
        {
        }

        private void ResetOperationWorkspace()
        {
            QueuedOperations.Clear();
            InvalidateAvailableItems();
        }

        protected virtual bool ValidateBeforeCommit(out string rejectionReason)
        {
            rejectionReason = null;
            return true;
        }

        protected virtual void CommitCustomSettings()
        {
        }

        protected virtual void NotifyJobsStarted(int count)
        {
            Messages.Message("FFF.MechModification.AppliedQueued".Translate(count), Mech, MessageTypeDefOf.PositiveEvent, false);
        }

        private void ApplyAndStartJobs()
        {
            if (!ValidateStandardQueue(out string rejectionReason) || !ValidateBeforeCommit(out rejectionReason))
            {
                if (!rejectionReason.NullOrEmpty()) Messages.Message(rejectionReason, Mech, MessageTypeDefOf.RejectInput, false);
                return;
            }
            CommitCustomSettings();
            NotifyJobsStarted(StartQueuedJobs());
        }

        private bool ValidateStandardQueue(out string rejectionReason)
        {
            rejectionReason = null;
            List<MechModificationQueueEntry> preceding = QueuedOperations
                .Where(entry => entry?.submitted == true)
                .ToList();
            for (int i = 0; i < QueuedOperations.Count; i++)
            {
                MechModificationQueueEntry entry = QueuedOperations[i];
                if (entry == null || entry.submitted) continue;
                if (entry.kind == MechModificationOperationKind.Install && !entry.custom
                    && ModificationProfileDatabase.IsModificationDef(entry.itemDef)
                    && !ModificationInstallValidator.CanInstall(
                        Mech,
                        entry.itemDef,
                        entry.part,
                        preceding,
                        out rejectionReason,
                        true,
                        entry.allowEquivalentPart))
                {
                    return false;
                }
                preceding.Add(entry);
            }
            return true;
        }

        protected virtual bool TryBuildCustomInstallEntry(Thing item, BodyPartRecord part, out MechModificationQueueEntry entry)
        {
            entry = null;
            return false;
        }

        protected virtual bool TryRestoreCustomInstallEntry(ThingDef itemDef, BodyPartRecord part, out MechModificationQueueEntry entry)
        {
            entry = null;
            return false;
        }

        protected virtual IEnumerable<ThingDef> GetAdditionalInstallItemDefs()
        {
            yield break;
        }

        protected virtual void AddCustomTargetPartDefs(HashSet<BodyPartDef> targetDefs)
        {
        }

        protected virtual bool CustomPartMatches(BodyPartRecord part, BodyPartDef targetDef)
        {
            return false;
        }

        protected virtual bool IsCustomInstalledHediff(Hediff hediff)
        {
            return false;
        }

        protected virtual bool TryGetCustomSourceThing(Hediff hediff, out ThingDef source)
        {
            source = null;
            return false;
        }

        protected virtual bool TryCreateCustomJob(MechModificationQueueEntry entry, out Job job)
        {
            job = null;
            return false;
        }

        protected virtual string GetCustomQueueLabel(MechModificationQueueEntry entry)
        {
            return entry.customId ?? "FFF.MechModification.Missing".Translate();
        }

        private void DrawLeft(Rect rect)
        {
            Rect portraitRect = new Rect(rect.x, rect.y, rect.width, PortraitHeight);
            Widgets.DrawBoxSolid(portraitRect, new Color(0f, 0f, 0f, 0.2f));
            DrawPortrait(portraitRect.ContractedBy(6f));

            Rect partsRect = new Rect(rect.x, portraitRect.yMax + 8f, rect.width, rect.height - portraitRect.height - 8f);
            Widgets.DrawBoxSolid(partsRect, new Color(0f, 0f, 0f, 0.2f));
            DrawPartsGrid(partsRect.ContractedBy(6f));
        }

        private void DrawPortrait(Rect rect)
        {
            Texture portrait = PortraitsCache.Get(Mech, rect.size, Rot4.South, Vector3.zero);
            GUI.DrawTexture(rect, portrait, ScaleMode.ScaleToFit);
            DrawPortraitInfo(rect);

            Rect infoButton = new Rect(rect.xMax - 30f, rect.y + 6f, 24f, 24f);
            if (Widgets.ButtonImage(infoButton, TexButton.Info)) Find.WindowStack.Add(new Dialog_InfoCard(Mech));
            TooltipHandler.TipRegion(infoButton, "FFF.MechModification.PawnInfoTip".Translate().ToString());
        }

        private void DrawPortraitInfo(Rect rect)
        {
            float lineHeight = 18f;
            Rect infoRect = new Rect(rect.x + 6f, rect.y + 6f, rect.width * 0.6f, lineHeight * 3f + 8f);
            Widgets.DrawBoxSolid(infoRect, new Color(0f, 0f, 0f, 0.35f));
            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.UpperLeft;
            Rect line = new Rect(infoRect.x + 4f, infoRect.y + 4f, infoRect.width - 8f, lineHeight);
            Widgets.Label(line, "FFF.MechModification.InfoName".Translate(Mech.Name?.ToStringShort ?? Mech.LabelCap.ToString()));
            line.y += lineHeight;
            Widgets.Label(line, "FFF.MechModification.InfoRace".Translate(Mech.def?.LabelCap ?? "-"));
            line.y += lineHeight;
            Widgets.Label(line, "FFF.MechModification.InfoAge".Translate(Mech.ageTracker?.AgeBiologicalYears.ToString() ?? "0"));
            Text.Anchor = previousAnchor;
        }

        private void DrawAvailableItems(Rect rect)
        {
            List<Thing> items = GetAvailableItems();
            const float rowHeight = 60f;
            const float iconSize = 48f;
            const float columnGap = 8f;
            float viewWidth = rect.width - 16f;
            int columns = viewWidth >= 420f ? 2 : 1;
            float columnWidth = (viewWidth - columnGap * (columns - 1)) / columns;
            int rows = Mathf.CeilToInt(items.Count / (float)columns);
            Rect view = new Rect(0f, 0f, viewWidth, Mathf.Max(rect.height, rows * rowHeight + 4f));

            Widgets.BeginScrollView(rect, ref availableScroll, view);
            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            for (int i = 0; i < items.Count; i++)
            {
                Thing item = items[i];
                int row = i / columns;
                int column = i % columns;
                Rect cell = new Rect(column * (columnWidth + columnGap), row * rowHeight, columnWidth, rowHeight);
                if (cell.yMax < availableScroll.y || cell.y > availableScroll.y + rect.height) continue;

                Rect iconRect = new Rect(cell.x + 6f, cell.y + 6f, iconSize, iconSize);
                Texture icon = GetThingIcon(item.def);
                if (icon != null) Widgets.DrawTextureFitted(iconRect, icon, 1f);
                Rect labelRect = new Rect(iconRect.xMax + 6f, cell.y, Mathf.Max(0f, cell.width - iconSize - 12f), rowHeight);
                Widgets.Label(labelRect, item.LabelCapNoCount);
                Widgets.DrawHighlightIfMouseover(cell);
                if (Widgets.ButtonInvisible(cell) && TryCreateInstallEntry(item, SelectedPart, out MechModificationQueueEntry entry))
                {
                    QueuedOperations.Add(entry);
                    InvalidateAvailableItems();
                }
            }
            if (items.Count == 0) Widgets.Label(new Rect(0f, 0f, view.width, rowHeight), "FFF.MechModification.NoModsOnMap".Translate());
            Text.Anchor = previousAnchor;
            Widgets.EndScrollView();
        }

        private void DrawQueuedOperations(Rect rect)
        {
            QueuedOperations.RemoveAll(entry => entry == null || entry.kind == MechModificationOperationKind.Install && entry.itemDef == null && (entry.item == null || entry.item.Destroyed));
            const float rowHeight = 40f;
            Rect view = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(rect.height, QueuedOperations.Count * rowHeight + 4f));
            Widgets.BeginScrollView(rect, ref queueScroll, view);
            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            for (int i = 0; i < QueuedOperations.Count; i++)
            {
                MechModificationQueueEntry entry = QueuedOperations[i];
                Rect row = new Rect(0f, i * rowHeight, view.width, rowHeight);
                Widgets.DrawBoxSolid(row, i % 2 == 0 ? new Color(0.12f, 0.12f, 0.12f, 0.45f) : new Color(0.18f, 0.18f, 0.18f, 0.45f));
                Widgets.DrawHighlightIfMouseover(row);
                Rect labelRect = new Rect(row.x, row.y, row.width - 28f, row.height);
                Widgets.Label(labelRect, GetQueueLabel(entry));
                bool previousEnabled = GUI.enabled;
                GUI.enabled = previousEnabled && !entry.submitted;
                if (Widgets.ButtonText(new Rect(labelRect.xMax + 2f, row.y, 24f, row.height), "X"))
                {
                    QueuedOperations.RemoveAt(i--);
                    InvalidateAvailableItems();
                }
                GUI.enabled = previousEnabled;
            }
            if (QueuedOperations.Count == 0) Widgets.Label(new Rect(0f, 0f, view.width, rowHeight), "FFF.MechModification.QueueEmpty".Translate());
            Text.Anchor = previousAnchor;
            Widgets.EndScrollView();
        }

        private string GetQueueLabel(MechModificationQueueEntry entry)
        {
            string missing = "FFF.MechModification.Missing".Translate();
            string partLabel = entry.part?.LabelCap.ToString() ?? corePart?.LabelCap.ToString() ?? "FFF.MechModification.PartFallback".Translate();
            switch (entry.kind)
            {
                case MechModificationOperationKind.Uninstall:
                    return "FFF.MechModification.QueueUninstall".Translate(entry.uninstallHediff?.def?.label ?? missing, partLabel);
                case MechModificationOperationKind.Custom:
                    return GetCustomQueueLabel(entry);
                default:
                    string itemLabel = entry.item?.LabelCapNoCount ?? entry.itemDef?.LabelCap.ToString() ?? missing;
                    return "FFF.MechModification.QueueInstall".Translate(itemLabel, partLabel);
            }
        }

        private void DrawPartsGrid(Rect rect)
        {
            const float labelHeight = 24f;
            const float spacing = 6f;
            const float boxSize = 72f;
            const int columns = 4;
            float viewHeight = spacing;
            for (int i = 0; i < slotCache.Count; i++)
            {
                int rows = Mathf.Max(1, Mathf.CeilToInt(slotCache[i].installed.Count / (float)columns));
                viewHeight += labelHeight + rows * boxSize + (rows - 1) * spacing + spacing;
            }
            Rect view = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(rect.height, viewHeight));
            Widgets.BeginScrollView(rect, ref partScroll, view);
            float y = 0f;
            for (int i = 0; i < slotCache.Count; i++)
            {
                SlotInfo slot = slotCache[i];
                int rows = Mathf.Max(1, Mathf.CeilToInt(slot.installed.Count / (float)columns));
                Rect rowRect = new Rect(0f, y, view.width, labelHeight + rows * boxSize + (rows - 1) * spacing);
                Rect headerRect = new Rect(rowRect.x, rowRect.y, rowRect.width, labelHeight);
                if (selectedSlot == slot) Widgets.DrawHighlight(rowRect);
                if (Widgets.ButtonInvisible(headerRect))
                {
                    selectedSlot = slot;
                    InvalidateAvailableItems();
                }
                Widgets.Label(headerRect, slot.label);

                for (int j = 0; j < slot.installed.Count; j++)
                {
                    InstalledModification installed = slot.installed[j];
                    Rect box = new Rect((j % columns) * (boxSize + spacing), headerRect.yMax + (j / columns) * (boxSize + spacing), boxSize, boxSize);
                    if (selectedInstalled == installed) Widgets.DrawBoxSolid(box, new Color(0.55f, 0.55f, 0.65f, 0.22f));
                    Rect iconRect = box.ContractedBy(4f);
                    if (installed.icon != null) Widgets.DrawTextureFitted(iconRect, installed.icon, 1f);
                    Widgets.DrawHighlightIfMouseover(iconRect);
                    if (Widgets.ButtonText(new Rect(box.xMax - 20f, box.yMax - 20f, 20f, 20f), "X"))
                    {
                        QueueUninstall(installed);
                        continue;
                    }
                    if (Widgets.ButtonInvisible(iconRect))
                    {
                        selectedInstalled = installed;
                        if (IsDoubleClick(installed)) OpenInstalledInfo(installed);
                    }
                }
                y = rowRect.yMax + spacing;
            }
            Widgets.EndScrollView();
        }

        private bool TryCreateInstallEntry(Thing item, BodyPartRecord part, out MechModificationQueueEntry entry)
        {
            if (TryBuildCustomInstallEntry(item, part, out entry))
            {
                if (ValidateBuiltInstallEntry(entry, out string customReason)) return true;
                if (!customReason.NullOrEmpty()) Messages.Message(customReason, Mech, MessageTypeDefOf.RejectInput, false);
                entry = null;
                return false;
            }
            CompTargetable_AddHediffOnTarget comp = item?.TryGetComp<CompTargetable_AddHediffOnTarget>();
            string reason = null;
            if (comp == null || !ModificationInstallValidator.CanInstall(Mech, item.def, part, QueuedOperations, out reason))
            {
                if (!reason.NullOrEmpty()) Messages.Message(reason, Mech, MessageTypeDefOf.RejectInput, false);
                entry = null;
                return false;
            }
            entry = new MechModificationQueueEntry
            {
                kind = MechModificationOperationKind.Install,
                item = item,
                itemDef = item.def,
                part = part
            };
            return true;
        }

        private bool ValidateBuiltInstallEntry(MechModificationQueueEntry entry, out string reason)
        {
            reason = null;
            ThingDef itemDef = entry?.itemDef ?? entry?.item?.def;
            if (entry == null || entry.kind != MechModificationOperationKind.Install || !ModificationProfileDatabase.IsModificationDef(itemDef)) return entry != null;
            return ModificationInstallValidator.CanInstall(
                Mech,
                itemDef,
                entry.part,
                QueuedOperations,
                out reason,
                true,
                entry.allowEquivalentPart);
        }

        private void QueueUninstall(InstalledModification installed)
        {
            if (installed?.hediff == null) return;
            bool custom = IsCustomInstalledHediff(installed.hediff);
            if (!custom && installed.hediff.TryGetComp<HediffComp_Modification>() == null) return;
            QueuedOperations.Insert(0, new MechModificationQueueEntry
            {
                kind = MechModificationOperationKind.Uninstall,
                uninstallHediff = installed.hediff,
                part = installed.displayPart,
                custom = custom
            });
            InvalidateAvailableItems();
        }

        private int StartQueuedJobs()
        {
            if (Mech?.jobs == null || Mech.Map == null) return 0;
            int started = 0;
            for (int i = 0; i < QueuedOperations.Count; i++)
            {
                MechModificationQueueEntry entry = QueuedOperations[i];
                if (entry == null || entry.submitted) continue;
                Job job = CreateJob(entry);
                if (job == null) continue;
                job.playerForced = true;
                bool accepted;
                if (started == 0 && Mech.jobs.curJob == null) accepted = Mech.jobs.TryTakeOrderedJob(job, JobTag.MiscWork);
                else
                {
                    Mech.jobs.jobQueue.EnqueueLast(job);
                    accepted = true;
                }
                if (!accepted) continue;
                entry.submitted = true;
                started++;
            }
            return started;
        }

        private Job CreateJob(MechModificationQueueEntry entry)
        {
            if (entry == null) return null;
            if (entry.custom && TryCreateCustomJob(entry, out Job customJob)) return customJob;

            switch (entry.kind)
            {
                case MechModificationOperationKind.Uninstall:
                    HediffComp_Modification mod = entry.uninstallHediff?.TryGetComp<HediffComp_Modification>();
                    JobDef removeDef = mod?.Props?.applyJob ?? FFF_DefOf.FFF_ModificationRemove;
                    return removeDef == null ? null : ModificationJobUtility.MakeRemoveJob(removeDef, Mech, entry.uninstallHediff);
                case MechModificationOperationKind.Install:
                    Thing item = entry.item;
                    if (item == null || item.Destroyed) item = FindClosestInstallItem(entry.itemDef, entry.part);
                    if (entry.itemDef == null || FFF_DefOf.FFF_Modification == null) return null;
                    Job_Modification job = ModificationJobUtility.MakeApplyJob(FFF_DefOf.FFF_Modification, Mech, item, entry.part);
                    job.itemDefName = entry.itemDef.defName;
                    job.allowEquivalentPart = entry.allowEquivalentPart;
                    return job;
                default:
                    return null;
            }
        }

        private void RebuildSlotCache()
        {
            slotCache.Clear();
            if (Mech?.RaceProps?.body == null) return;
            EnsureStandardDefCache();
            List<BodyPartRecord> allParts = Mech.RaceProps.body.AllParts;
            corePart = Mech.RaceProps.body.corePart;
            HashSet<BodyPartDef> targetDefs = new HashSet<BodyPartDef>();
            for (int i = 0; i < standardModificationDefs.Count; i++)
            {
                CompProperties_AddHediffOnTarget props = standardModificationDefs[i].GetCompProperties<CompProperties_AddHediffOnTarget>();
                if (props?.targetBodyPartDefs.NullOrEmpty() != false)
                {
                    if (corePart?.def != null) targetDefs.Add(corePart.def);
                    continue;
                }
                for (int j = 0; j < props.targetBodyPartDefs.Count; j++) targetDefs.Add(props.targetBodyPartDefs[j]);
            }
            AddCustomTargetPartDefs(targetDefs);

            for (int i = 0; i < allParts.Count; i++)
            {
                BodyPartRecord part = allParts[i];
                if (targetDefs.Any(target => part.def == target || CustomPartMatches(part, target)))
                {
                    slotCache.Add(new SlotInfo { part = part, label = part.Label });
                }
            }

            List<Hediff> hediffs = Mech.health?.hediffSet?.hediffs ?? new List<Hediff>();
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (!IsInstalledModification(hediff)) continue;
                BodyPartRecord displayPart = hediff.Part ?? corePart;
                if (displayPart == null) continue;
                SlotInfo slot = slotCache.FirstOrDefault(candidate => candidate.part == displayPart);
                if (slot == null)
                {
                    slot = new SlotInfo { part = displayPart, label = displayPart.Label };
                    slotCache.Add(slot);
                }

                ThingDef source = hediff.TryGetComp<HediffComp_Modification>()?.SourceThingDef;
                if (source == null) standardSourceByHediff.TryGetValue(hediff.def, out source);
                if (source == null) TryGetCustomSourceThing(hediff, out source);
                slot.installed.Add(new InstalledModification
                {
                    hediff = hediff,
                    displayPart = displayPart,
                    source = source,
                    icon = GetThingIcon(source)
                });
            }

            slotCache = slotCache.OrderBy(slot => slot.label).ToList();
            InvalidateAvailableItems();
        }

        private bool IsInstalledModification(Hediff hediff)
        {
            return hediff?.TryGetComp<HediffComp_Modification>() != null || IsCustomInstalledHediff(hediff);
        }

        private List<Thing> GetAvailableItems()
        {
            BodyPartRecord part = SelectedPart;
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (availableCachePart != part || tick - availableCacheTick >= AvailableRefreshTicks) RebuildAvailableItems(part, tick);
            else availableItems.RemoveAll(item => item == null || item.Destroyed || !item.Spawned || item.Map != Mech?.Map || item.stackCount <= 0);
            return availableItems;
        }

        private void RebuildAvailableItems(BodyPartRecord part, int tick)
        {
            availableCachePart = part;
            availableCacheTick = tick;
            availableItems.Clear();
            Map map = Mech?.Map ?? Find.CurrentMap;
            if (map == null || part == null) return;

            EnsureStandardDefCache();
            IEnumerable<ThingDef> defs = standardModificationDefs.Concat(GetAdditionalInstallItemDefs() ?? Enumerable.Empty<ThingDef>()).Where(def => def != null).Distinct();
            MapComponent_ModificationIndex index = map.GetComponent<MapComponent_ModificationIndex>();
            foreach (ThingDef def in defs)
            {
                List<Thing> things = index?.GetCandidates(def, Mech, Mech.Position, true) ?? new List<Thing>();
                Thing best = null;
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (!CanUseThingOnPart(thing, part)) continue;
                    best = thing;
                    break;
                }
                if (best != null) availableItems.Add(best);
            }
            availableItems = availableItems.OrderBy(item => item.LabelCapNoCount.ToString()).ThenBy(item => item.def.defName).ToList();
        }

        private bool CanUseThingOnPart(Thing item, BodyPartRecord part)
        {
            if (TryBuildCustomInstallEntry(item, part, out MechModificationQueueEntry customEntry))
            {
                return ValidateBuiltInstallEntry(customEntry, out _);
            }
            return item != null && ModificationInstallValidator.CanInstall(Mech, item.def, part, QueuedOperations, out _);
        }

        private Thing FindClosestInstallItem(ThingDef def, BodyPartRecord part)
        {
            if (def == null || Mech?.Map == null) return null;
            List<Thing> things = Mech.Map.GetComponent<MapComponent_ModificationIndex>()?.GetCandidates(def, Mech, Mech.Position, true);
            return things.NullOrEmpty() ? null : things[0];
        }

        private void InvalidateAvailableItems()
        {
            availableCachePart = null;
            availableCacheTick = -999999;
            availableItems.Clear();
        }

        private void OpenSavePresetDialog()
        {
            Find.WindowStack.Add(new Dialog_MechModificationPresetSave(BuildPreset));
        }

        private void OpenLoadPresetDialog()
        {
            Find.WindowStack.Add(new Dialog_MechModificationPresetLoad(ApplyPreset));
        }

        private MechModificationPreset BuildPreset()
        {
            MechModificationPreset preset = new MechModificationPreset();
            for (int i = 0; i < QueuedOperations.Count; i++)
            {
                MechModificationQueueEntry operation = QueuedOperations[i];
                if (operation.kind == MechModificationOperationKind.Custom) continue;
                MechModificationPresetEntry entry = new MechModificationPresetEntry
                {
                    uninstall = operation.kind == MechModificationOperationKind.Uninstall,
                    partDefName = operation.part?.def?.defName,
                    partIndex = operation.part?.Index ?? -1
                };
                if (entry.uninstall)
                {
                    entry.hediffDefName = operation.uninstallHediff?.def?.defName;
                    if (!entry.hediffDefName.NullOrEmpty()) preset.entries.Add(entry);
                }
                else
                {
                    entry.itemDefName = operation.itemDef?.defName ?? operation.item?.def?.defName;
                    if (!entry.itemDefName.NullOrEmpty()) preset.entries.Add(entry);
                }
            }
            return preset;
        }

        private void ApplyPreset(MechModificationPreset preset)
        {
            QueuedOperations.RemoveAll(entry => entry?.submitted != true);
            if (preset?.entries == null)
            {
                InvalidateAvailableItems();
                return;
            }
            for (int i = 0; i < preset.entries.Count; i++)
            {
                MechModificationPresetEntry presetEntry = preset.entries[i];
                if (presetEntry == null) continue;
                BodyPartRecord part = FindPart(presetEntry.partIndex, presetEntry.partDefName);
                if (part == null) continue;
                if (presetEntry.uninstall)
                {
                    Hediff hediff = FindInstalledHediff(presetEntry.hediffDefName, part);
                    if (hediff != null)
                    {
                        QueuedOperations.Add(new MechModificationQueueEntry
                        {
                            kind = MechModificationOperationKind.Uninstall,
                            uninstallHediff = hediff,
                            part = part,
                            custom = IsCustomInstalledHediff(hediff)
                        });
                    }
                    continue;
                }

                ThingDef itemDef = DefDatabase<ThingDef>.GetNamedSilentFail(presetEntry.itemDefName);
                if (itemDef == null) continue;
                if (TryRestoreCustomInstallEntry(itemDef, part, out MechModificationQueueEntry customEntry))
                {
                    customEntry.itemDef = itemDef;
                    customEntry.item = FindClosestInstallItem(itemDef, customEntry.part ?? part);
                    if (ValidateBuiltInstallEntry(customEntry, out _)) QueuedOperations.Add(customEntry);
                    continue;
                }
                CompProperties_AddHediffOnTarget props = itemDef.GetCompProperties<CompProperties_AddHediffOnTarget>();
                if (props == null || !ModificationInstallValidator.CanInstall(Mech, itemDef, part, QueuedOperations, out _)) continue;
                QueuedOperations.Add(new MechModificationQueueEntry
                {
                    kind = MechModificationOperationKind.Install,
                    itemDef = itemDef,
                    item = FindClosestInstallItem(itemDef, part),
                    part = part
                });
            }
            InvalidateAvailableItems();
        }

        private BodyPartRecord FindPart(int index, string defName)
        {
            List<BodyPartRecord> parts = Mech?.RaceProps?.body?.AllParts;
            if (parts == null) return null;
            if (index >= 0 && index < parts.Count)
            {
                BodyPartRecord indexed = parts[index];
                if (defName.NullOrEmpty() || indexed.def?.defName == defName) return indexed;
            }
            return defName.NullOrEmpty() ? null : parts.FirstOrDefault(part => part.def?.defName == defName);
        }

        private Hediff FindInstalledHediff(string defName, BodyPartRecord part)
        {
            List<Hediff> hediffs = Mech?.health?.hediffSet?.hediffs;
            if (hediffs == null || defName.NullOrEmpty()) return null;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                bool samePart = hediff?.Part == part || hediff?.Part == null && part == corePart;
                if (hediff?.def?.defName == defName && samePart && IsInstalledModification(hediff)) return hediff;
            }
            return null;
        }

        private bool IsDoubleClick(object entry)
        {
            float now = Time.realtimeSinceStartup;
            bool result = ReferenceEquals(lastClickedEntry, entry) && now - lastClickedTime <= DoubleClickInterval;
            lastClickedEntry = entry;
            lastClickedTime = now;
            return result;
        }

        private static void OpenInstalledInfo(InstalledModification installed)
        {
            if (installed.source != null) Find.WindowStack.Add(new Dialog_InfoCard(installed.source));
            else if (installed.hediff?.def != null) Find.WindowStack.Add(new Dialog_InfoCard(installed.hediff.def));
        }

        private static Texture GetThingIcon(ThingDef def)
        {
            if (def?.uiIcon != null) return def.uiIcon;
            return def?.graphicData?.texPath.NullOrEmpty() == false ? ContentFinder<Texture2D>.Get(def.graphicData.texPath, false) : null;
        }

        private static void EnsureStandardDefCache()
        {
            if (standardModificationDefs != null) return;
            standardModificationDefs = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def.GetCompProperties<CompProperties_AddHediffOnTarget>() != null)
                .ToList();
            standardSourceByHediff = new Dictionary<HediffDef, ThingDef>();
            for (int i = 0; i < standardModificationDefs.Count; i++)
            {
                ThingDef source = standardModificationDefs[i];
                HediffDef hediff = source.GetCompProperties<CompProperties_AddHediffOnTarget>()?.hediffDef;
                if (hediff != null && !standardSourceByHediff.ContainsKey(hediff)) standardSourceByHediff.Add(hediff, source);
            }
        }

        private void BeginCloseAnimation(bool sound)
        {
            if (isClosing) return;
            isClosing = true;
            closeSound = sound;
            closeStartTime = Time.realtimeSinceStartup;
            closeStartProgress = GetOpenProgress();
        }

        private float GetOpenProgress()
        {
            return OpenAnimationDuration <= 0f ? 1f : Mathf.Clamp01((Time.realtimeSinceStartup - openStartTime) / OpenAnimationDuration);
        }

        private float GetAnimationProgress()
        {
            float open = GetOpenProgress();
            if (!isClosing) return open;
            if (CloseAnimationDuration <= 0f) return 0f;
            float close = Mathf.Clamp01((Time.realtimeSinceStartup - closeStartTime) / CloseAnimationDuration);
            return Mathf.Lerp(closeStartProgress, 0f, close);
        }

        private class SlotInfo
        {
            public BodyPartRecord part;
            public string label;
            public readonly List<InstalledModification> installed = new List<InstalledModification>();
        }

        private class InstalledModification
        {
            public Hediff hediff;
            public BodyPartRecord displayPart;
            public ThingDef source;
            public Texture icon;
        }

        private class AnimatedWindowDrawing : IWindowDrawing
        {
            private readonly DefaultWindowDrawing inner = new DefaultWindowDrawing();
            private bool clipped;

            public GUIStyle EmptyStyle => inner.EmptyStyle;

            public void DoWindowBackground(Rect rect)
            {
                if (!TryGetClipRects(rect, out Rect clip, out Rect offset))
                {
                    inner.DoWindowBackground(rect);
                    return;
                }
                GUI.BeginGroup(clip);
                inner.DoWindowBackground(offset);
                GUI.EndGroup();
            }

            public bool DoCloseButton(Rect rect, string text) => inner.DoCloseButton(rect, text);
            public bool DoClostButtonSmall(Rect rect) => inner.DoClostButtonSmall(rect);

            public void BeginGroup(Rect rect)
            {
                if (!TryGetClipRects(rect, out Rect clip, out Rect offset))
                {
                    inner.BeginGroup(rect);
                    clipped = false;
                    return;
                }
                inner.BeginGroup(clip);
                inner.BeginGroup(offset);
                clipped = true;
            }

            public void EndGroup()
            {
                if (clipped)
                {
                    inner.EndGroup();
                    inner.EndGroup();
                    clipped = false;
                }
                else inner.EndGroup();
            }

            public void DoGrayOut(Rect rect)
            {
                if (!TryGetClipRects(rect, out Rect clip, out Rect offset))
                {
                    inner.DoGrayOut(rect);
                    return;
                }
                GUI.BeginGroup(clip);
                inner.DoGrayOut(offset);
                GUI.EndGroup();
            }

            private static bool TryGetClipRects(Rect rect, out Rect clip, out Rect offset)
            {
                clip = rect;
                offset = rect;
                Window_MechModification window = Find.WindowStack?.currentlyDrawnWindow as Window_MechModification;
                if (window == null) return false;
                float animation = window.GetAnimationProgress();
                if (animation >= 0.999f) return false;
                float height = Mathf.Max(0.01f, rect.height * animation);
                clip = new Rect(rect.x, rect.y + (rect.height - height) * 0.5f, rect.width, height);
                offset = new Rect(rect.x - clip.x, rect.y - clip.y, rect.width, rect.height);
                return true;
            }
        }
    }
}
