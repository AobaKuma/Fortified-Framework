using System;
using RimWorld;
using Verse;

namespace Fortified
{
    public class Dialog_MechModificationPresetSave : Dialog_FileList
    {
        private readonly Func<MechModificationPreset> buildPreset;

        protected override bool ShouldDoTypeInField => true;

        public Dialog_MechModificationPresetSave(Func<MechModificationPreset> buildPreset)
        {
            this.buildPreset = buildPreset;
            interactButLabel = "OverwriteButton".Translate().ToString();
            typingName = "FFF.MechModification.PresetDefaultName".Translate().ToString();
        }

        protected override void DoFileInteraction(string fileName)
        {
            fileName = GenFile.SanitizedFileName(fileName);
            MechModificationPreset preset = buildPreset?.Invoke();
            if (preset == null) return;
            MechModificationPresetUtility.SavePreset(fileName, preset);
            Messages.Message("FFF.MechModification.PresetSaved".Translate(fileName), MessageTypeDefOf.PositiveEvent, false);
            Close();
        }

        protected override void ReloadFiles()
        {
            files.Clear();
            foreach (System.IO.FileInfo file in MechModificationPresetUtility.GetPresetFiles())
            {
                try
                {
                    SaveFileInfo info = new SaveFileInfo(file);
                    info.LoadData();
                    files.Add(info);
                }
                catch (Exception exception)
                {
                    Log.Error("Exception loading mech modification preset: " + file.Name + ": " + exception);
                }
            }
        }
    }

    public class Dialog_MechModificationPresetLoad : Dialog_FileList
    {
        private readonly Action<MechModificationPreset> onLoad;

        protected override bool FocusSearchField => true;

        public Dialog_MechModificationPresetLoad(Action<MechModificationPreset> onLoad)
        {
            this.onLoad = onLoad;
            interactButLabel = "LoadGameButton".Translate().ToString();
        }

        protected override void DoFileInteraction(string fileName)
        {
            MechModificationPreset preset = MechModificationPresetUtility.LoadPreset(fileName);
            if (preset == null)
            {
                Messages.Message("FFF.MechModification.PresetLoadFailed".Translate(fileName), MessageTypeDefOf.RejectInput, false);
                return;
            }
            onLoad?.Invoke(preset);
            Messages.Message("FFF.MechModification.PresetLoaded".Translate(fileName), MessageTypeDefOf.PositiveEvent, false);
            Close();
        }

        protected override void ReloadFiles()
        {
            files.Clear();
            foreach (System.IO.FileInfo file in MechModificationPresetUtility.GetPresetFiles())
            {
                try
                {
                    SaveFileInfo info = new SaveFileInfo(file);
                    info.LoadData();
                    files.Add(info);
                }
                catch (Exception exception)
                {
                    Log.Error("Exception loading mech modification preset: " + file.Name + ": " + exception);
                }
            }
        }
    }
}
