using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;

namespace Fortified
{
    public class MechModificationPreset : IExposable
    {
        public int version = 1;
        public List<MechModificationPresetEntry> entries = new List<MechModificationPresetEntry>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref version, "version", 1);
            Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && entries == null) entries = new List<MechModificationPresetEntry>();
        }
    }

    public class MechModificationPresetEntry : IExposable
    {
        public string itemDefName;
        public string partDefName;
        public int partIndex = -1;
        public string hediffDefName;
        public bool uninstall;

        public void ExposeData()
        {
            Scribe_Values.Look(ref itemDefName, "itemDefName");
            Scribe_Values.Look(ref partDefName, "partDefName");
            Scribe_Values.Look(ref partIndex, "partIndex", -1);
            Scribe_Values.Look(ref hediffDefName, "hediffDefName");
            Scribe_Values.Look(ref uninstall, "uninstall", false);
        }
    }

    public static class MechModificationPresetUtility
    {
        private const string PresetFolderName = "FFF_MechModificationPresets";
        private const string PresetExtension = ".fffmc";
        private const string LegacyFolderName = "DMS_MechCustomizationPresets";
        private const string LegacyExtension = ".dmsmc";

        public static string PresetFolderPath => EnsureFolder(PresetFolderName);

        public static IEnumerable<FileInfo> GetPresetFiles()
        {
            List<FileInfo> files = new DirectoryInfo(PresetFolderPath).GetFiles("*" + PresetExtension).ToList();
            string legacyPath = Path.Combine(GenFilePaths.SaveDataFolderPath, LegacyFolderName);
            if (Directory.Exists(legacyPath)) files.AddRange(new DirectoryInfo(legacyPath).GetFiles("*" + LegacyExtension));
            return files.OrderByDescending(file => file.LastWriteTime);
        }

        public static string BuildPresetPath(string name)
        {
            return Path.Combine(PresetFolderPath, GenText.SanitizeFilename(name) + PresetExtension);
        }

        public static void SavePreset(string name, MechModificationPreset preset)
        {
            string path = BuildPresetPath(name);
            SafeSaver.Save(path, "FFF_MechModificationPreset", delegate
            {
                ScribeMetaHeaderUtility.WriteMetaHeader();
                Scribe_Deep.Look(ref preset, "preset");
            });
        }

        public static MechModificationPreset LoadPreset(string name)
        {
            string path = ResolveExistingPath(name);
            if (path == null) return null;
            try
            {
                MechModificationPreset preset = new MechModificationPreset();
                Scribe.loader.InitLoading(path);
                try
                {
                    Scribe_Deep.Look(ref preset, "preset");
                    Scribe.loader.FinalizeLoading();
                    return preset;
                }
                catch
                {
                    Scribe.ForceStop();
                    throw;
                }
            }
            catch (Exception exception)
            {
                Log.Error("Could not load mech modification preset (" + path + "): " + exception.Message);
                return null;
            }
        }

        private static string ResolveExistingPath(string name)
        {
            string safeName = GenText.SanitizeFilename(name);
            string path = Path.Combine(PresetFolderPath, safeName + PresetExtension);
            if (File.Exists(path)) return path;
            string legacyPath = Path.Combine(GenFilePaths.SaveDataFolderPath, LegacyFolderName, safeName + LegacyExtension);
            return File.Exists(legacyPath) ? legacyPath : null;
        }

        private static string EnsureFolder(string folderName)
        {
            string path = Path.Combine(GenFilePaths.SaveDataFolderPath, folderName);
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            return path;
        }
    }
}
