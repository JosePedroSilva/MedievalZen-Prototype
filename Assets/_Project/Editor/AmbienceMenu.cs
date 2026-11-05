using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MedievalZen.Sound;
using UnityEditor;
using UnityEngine;

namespace MedievalZen.EditorTools
{
    /// <summary>
    /// Builds <c>Resources/AmbiencePalette</c> from <see cref="AmbienceCatalog"/> (menu MedievalZen/Sound/Build Ambience Palette, and by itself
    /// when the editor loads with the palette missing or older than the catalog). Each clip is measured from the file on disk with
    /// <see cref="WavFile"/> (whole-file RMS, the quietest and loudest second, the loop seam) and trimmed to its target; its import settings
    /// are set (Vorbis about quality 0.5, Compressed In Memory, never Streaming: a bed crossfades into itself and plays one clip on two sources
    /// at once; beds stay stereo, positioned loops and spot sounds are forced to mono). The user's <c>userTrimDb</c> and <c>enabled</c> are
    /// kept. The sound packs themselves are never moved or edited.
    /// </summary>
    [InitializeOnLoad]
    internal static class AmbienceMenu
    {
        public const string ResourcesFolder = "Assets/_Project/Resources";
        public const string PalettePath = ResourcesFolder + "/" + AmbiencePalette.ResourcePath + ".asset";
        private const string SessionKey = "MedievalZen.Ambience.AutoBuildTried";
        private const float VorbisQuality = 0.5f;

        private static bool building;
        private static int waitFrames;

        static AmbienceMenu()
        {
            EditorApplication.delayCall += () =>
            {
                waitFrames = 0;
                EditorApplication.update -= AutoBuildWhenIdle;
                EditorApplication.update += AutoBuildWhenIdle;
            };
        }

        [MenuItem("MedievalZen/Sound/Build Ambience Palette", priority = 60)]
        private static void BuildMenu()
        {
            AmbiencePalette palette = Build();
            if (palette == null) return;
            Selection.activeObject = palette;
            EditorGUIUtility.PingObject(palette);
        }

        [MenuItem("MedievalZen/Sound/Build Ambience Palette", true)]
        private static bool BuildMenuValidate() => !EditorApplication.isPlayingOrWillChangePlaymode;

        private static void AutoBuildWhenIdle()
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    EditorApplication.update -= AutoBuildWhenIdle;
                    return;
                }

                if (EditorApplication.isCompiling || EditorApplication.isUpdating || building)
                {
                    if (++waitFrames > 20000) EditorApplication.update -= AutoBuildWhenIdle;
                    return;
                }

                EditorApplication.update -= AutoBuildWhenIdle;
                if (!AssetDatabase.IsValidFolder(AmbienceCatalog.SoundsFolder)) return;   // the sound packs are not imported
                var palette = AssetDatabase.LoadAssetAtPath<AmbiencePalette>(PalettePath);
                if (palette != null && palette.IsCurrent) return;
                string mark = AmbienceCatalog.Version.ToString();
                if (SessionState.GetString(SessionKey, string.Empty) == mark) return;
                SessionState.SetString(SessionKey, mark);
                Debug.Log($"Ambience: {(palette == null ? "no palette yet" : "the palette is out of date")}; building {PalettePath} (MedievalZen/Sound/Build Ambience Palette).");
                Build();
            }
            catch (Exception e)
            {
                EditorApplication.update -= AutoBuildWhenIdle;
                Debug.LogError($"Ambience: building the palette automatically failed: {e}");
            }
        }

        /// <summary>Measures, imports and records every catalog clip. Returns the palette (null when nothing could be built).</summary>
        public static AmbiencePalette Build()
        {
            if (building) return null;
            building = true;
            try
            {
                return BuildNow();
            }
            finally
            {
                building = false;
            }
        }

        private static AmbiencePalette BuildNow()
        {
            if (!AssetDatabase.IsValidFolder(ResourcesFolder)) AssetDatabase.CreateFolder("Assets/_Project", "Resources");
            var palette = AssetDatabase.LoadAssetAtPath<AmbiencePalette>(PalettePath);
            bool created = palette == null;
            if (created) palette = ScriptableObject.CreateInstance<AmbiencePalette>();

            // The user's fields survive, keyed by path.
            var kept = new Dictionary<string, AmbiencePalette.Entry>(StringComparer.OrdinalIgnoreCase);
            foreach (AmbiencePalette.Entry old in palette.Entries)
            {
                if (old != null && !string.IsNullOrEmpty(old.path)) kept[old.path] = old;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            var entries = new List<AmbiencePalette.Entry>();
            var log = new StringBuilder();
            int missing = 0, warnings = 0;
            log.Append("Ambience palette (catalog v").Append(AmbienceCatalog.Version).Append("): clip | category | RMS | peak | 1 s min..max | seam | trim\n");
            var reimport = new List<string>();
            {
                foreach (AmbienceClipSpec spec in AmbienceCatalog.All)
                {
                    string file = Path.Combine(projectRoot, spec.AssetPath);
                    if (!File.Exists(file))
                    {
                        missing++;
                        Debug.LogWarning($"Ambience: {spec.AssetPath} is missing; its role {spec.Role} plays without it.");
                        continue;
                    }

                    if (!AmbienceLevels.Measure(File.ReadAllBytes(file), out AmbienceLevel level, spec.Category != AmbienceCategory.Bed))
                    {
                        missing++;
                        Debug.LogWarning($"Ambience: {spec.AssetPath} is not a WAV file the reader supports; skipped.");
                        continue;
                    }

                    if (SetImport(spec)) reimport.Add(spec.AssetPath);
                    kept.TryGetValue(spec.Path, out AmbiencePalette.Entry before);
                    var e = new AmbiencePalette.Entry
                    {
                        role = spec.Role,
                        category = spec.Category,
                        path = spec.Path,
                        rmsDb = level.ReferenceDb(spec.Category),
                        peakDb = level.PeakDb,
                        windowMinDb = level.WindowMinDb,
                        windowMaxDb = level.WindowMaxDb,
                        seamLevelDb = level.SeamLevelDb,
                        seamJump = level.SeamJump,
                        trimDb = AmbienceCatalog.TargetDb(spec) - level.ReferenceDb(spec.Category),
                        userTrimDb = before != null ? before.userTrimDb : 0f,
                        enabled = before == null || before.enabled,
                    };
                    entries.Add(e);
                    string flags = AmbienceLevels.Flags(level, spec.Category);
                    if (e.trimDb > SoundMix.GainToDb(SoundBoost.MaxGain)) flags += $" {e.trimDb:0.0} dB quieter than its target, more than the boost's {SoundMix.GainToDb(SoundBoost.MaxGain):0} dB: it stays under the target;";
                    else if (e.trimDb > 6f) flags += $" lifted {e.trimDb:0.0} dB by the source's boost (a quiet recording: listen for hiss);";
                    log.Append($"{spec.Name} | {spec.Category} | {e.rmsDb:0.0} | {level.PeakDb:0.0} | {level.WindowMinDb:0.0}..{level.WindowMaxDb:0.0} | {level.SeamLevelDb:+0.0;-0.0} dB, jump {level.SeamJump:0.000} | {e.trimDb:+0.0;-0.0} dB");
                    if (flags.Length > 0)
                    {
                        warnings++;
                        log.Append(" | ").Append(flags.Trim());
                    }

                    log.Append('\n');
                }
            }

            // Clips are loaded after the imports so the references are to the reimported assets.
            foreach (AmbiencePalette.Entry e in entries)
            {
                e.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AmbienceCatalog.SoundsFolder + "/" + e.path);
                if (e.clip == null)
                {
                    missing++;
                    Debug.LogWarning($"Ambience: {e.path} did not load as an AudioClip.");
                }
            }

            palette.Entries.Clear();
            palette.Entries.AddRange(entries);
            palette.Version = AmbienceCatalog.Version;
            palette.Invalidate();
            if (created) AssetDatabase.CreateAsset(palette, PalettePath);
            EditorUtility.SetDirty(palette);
            AssetDatabase.SaveAssets();
            palette = AssetDatabase.LoadAssetAtPath<AmbiencePalette>(PalettePath);
            Debug.Log(log.ToString());
            string summary = $"Ambience: built {PalettePath}: {entries.Count} of {AmbienceCatalog.All.Length} clips, {missing} missing, {warnings} with notes, {reimport.Count} reimported.";
            if (missing > 0) Debug.LogWarning(summary);
            else Debug.Log(summary);
            return palette;
        }

        /// <summary>Sets a clip's import settings; true when they changed (the clip needs a reimport).</summary>
        private static bool SetImport(in AmbienceClipSpec spec)
        {
            if (!(AssetImporter.GetAtPath(spec.AssetPath) is AudioImporter importer)) return false;
            bool changed = Configure(importer, spec);
            if (changed) importer.SaveAndReimport();
            return changed;
        }

        /// <summary>
        /// Gives an importer the settings of a catalog clip without reimporting; true when anything changed. Also used on a
        /// clip's first import (<see cref="SoundImportRules"/>), so the packs are encoded once.
        /// </summary>
        public static bool Configure(AudioImporter importer, in AmbienceClipSpec spec)
        {
            bool mono = spec.Category != AmbienceCategory.Bed;
            AudioImporterSampleSettings s = importer.defaultSampleSettings;
            bool changed = false;
            if (s.loadType != AudioClipLoadType.CompressedInMemory)
            {
                s.loadType = AudioClipLoadType.CompressedInMemory;
                changed = true;
            }

            if (s.compressionFormat != AudioCompressionFormat.Vorbis)
            {
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                changed = true;
            }

            if (Math.Abs(s.quality - VorbisQuality) > 0.01f)
            {
                s.quality = VorbisQuality;
                changed = true;
            }

            if (s.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate)
            {
                s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                changed = true;
            }

            if (!s.preloadAudioData)
            {
                s.preloadAudioData = true;
                changed = true;
            }

            if (changed) importer.defaultSampleSettings = s;
            if (importer.forceToMono != mono)
            {
                importer.forceToMono = mono;
                changed = true;
            }

            bool background = spec.Category != AmbienceCategory.Spot;   // a one-shot must be ready the moment it is asked for
            if (importer.loadInBackground != background)
            {
                importer.loadInBackground = background;
                changed = true;
            }

            // Last, on a fresh snapshot of the importer: a mono down-mix must keep its measured level (Unity normalises it by default).
            if (SoundImportRules.SetNormalize(importer, false)) changed = true;
            return changed;
        }
    }
}
