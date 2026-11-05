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
    /// Builds <see cref="MusicLibrary"/> (<c>Assets/_Project/Resources/MusicLibrary.asset</c>) from <see cref="MusicCatalog"/>: finds each
    /// track's five WAVs (third-party, never moved or edited; only their import settings are set), sets them to Vorbis 0.65, Compressed
    /// In Memory, no preload, background loading (not Streaming: the tail overlap plays one clip on two sources at once), and bakes
    /// lengths, levels, trims and mood weights. The menu item re-measures every file from disk with <see cref="WavFile"/> (a minute or
    /// so); the automatic build on editor load uses the catalogue's offline measurements (same code) and checks the clip lengths.
    /// </summary>
    [InitializeOnLoad]
    public static class MusicMenu
    {
        public const string LibraryPath = "Assets/_Project/Resources/MusicLibrary.asset";
        private const string SessionKey = "MedievalZen.MusicMenu.AutoBuild";
        private const float VorbisQuality = 0.65f;

        private static bool building;
        private static int waitFrames;

        static MusicMenu()
        {
            EditorApplication.delayCall += () =>
            {
                waitFrames = 0;
                EditorApplication.update -= AutoBuildWhenIdle;
                EditorApplication.update += AutoBuildWhenIdle;
            };
        }

        [MenuItem("MedievalZen/Sound/Build Music Library", priority = 60)]
        private static void BuildMenu()
        {
            MusicLibrary library = Build(true);
            if (library == null) return;
            Selection.activeObject = library;
            EditorGUIUtility.PingObject(library);
        }

        [MenuItem("MedievalZen/Sound/Build Music Library", true)]
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
                if (!AssetDatabase.IsValidFolder(MusicCatalog.Root + "/Ethereal Music Pack")) return;   // the packs are not imported
                var library = AssetDatabase.LoadAssetAtPath<MusicLibrary>(LibraryPath);
                if (library != null && library.IsCurrent) return;
                string mark = MusicCatalog.Version.ToString();
                if (SessionState.GetString(SessionKey, string.Empty) == mark) return;
                SessionState.SetString(SessionKey, mark);
                Debug.Log($"Music: {(library == null ? "no library yet" : "the library is out of date")}; building {LibraryPath} (MedievalZen/Sound/Build Music Library).");
                Build(false);
            }
            catch (Exception e)
            {
                EditorApplication.update -= AutoBuildWhenIdle;
                Debug.LogError($"Music: building the library automatically failed: {e}");
            }
        }

        /// <summary>Builds or refreshes the library; <paramref name="measure"/> re-reads every WAV from disk.</summary>
        public static MusicLibrary Build(bool measure)
        {
            if (building) return null;
            building = true;
            try
            {
                var library = AssetDatabase.LoadAssetAtPath<MusicLibrary>(LibraryPath);
                bool created = library == null;
                var kept = new Dictionary<string, MusicLibrary.Track>();
                if (!created && library.tracks != null)
                    foreach (MusicLibrary.Track t in library.tracks)
                        if (t != null && !string.IsNullOrEmpty(t.folder)) kept[t.folder] = t;

                if (created)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath) ?? "Assets/_Project/Resources");
                    library = ScriptableObject.CreateInstance<MusicLibrary>();
                    AssetDatabase.CreateAsset(library, LibraryPath);
                }

                var tracks = new List<MusicLibrary.Track>();
                var log = new StringBuilder();
                log.AppendLine($"Music library: {MusicCatalog.Tracks.Length} tracks, target {SoundMix.MusicTargetDb:0} dB RMS for Main, {(measure ? "measured from the WAVs now" : "levels from the catalogue (offline measurement)")}.");
                log.AppendLine("track | on | loop s | tail s | tail drop dB | RMS I1/I2/Main dB | trim dB | bright Hz | pulse | onsets/s | weight day/forest/night/rest");
                int reimported = 0, problems = 0;
                AssetDatabase.StartAssetEditing();
                try
                {
                    for (int k = 0; k < MusicCatalog.Tracks.Length; k++)
                    {
                        MusicTrackInfo info = MusicCatalog.Tracks[k];
                        if (measure) EditorUtility.DisplayProgressBar("Build Music Library", info.Title, k / (float)MusicCatalog.Tracks.Length);
                        kept.TryGetValue(info.Folder, out MusicLibrary.Track old);
                        var t = new MusicLibrary.Track
                        {
                            title = info.Title, folder = info.Folder, enabled = old?.enabled ?? true, userWeight = old?.userWeight ?? 1f,
                            tail = info.Tail, duration = info.Duration, loop = info.Loop, cut30Seconds = info.Cut30Seconds, cut60Seconds = info.Cut60Seconds,
                            mainRmsDb = info.MainRmsDb, intensity1RmsDb = info.Intensity1RmsDb, intensity2RmsDb = info.Intensity2RmsDb,
                            brightnessHz = info.BrightnessHz, pulse = info.Pulse, onsets = info.Onsets, tailDropDb = float.NaN,
                        };
                        string dir = MusicCatalog.Root + "/" + info.Folder;
                        if (!Directory.Exists(dir))
                        {
                            log.AppendLine($"{info.Title} | MISSING folder {dir}");
                            problems++;
                            tracks.Add(t);
                            continue;
                        }

                        foreach (string file in Directory.GetFiles(dir, "*.wav"))
                        {
                            string path = file.Replace('\\', '/');
                            if (!MusicCatalog.TryVariantOf(Path.GetFileNameWithoutExtension(path), out MusicVariant v)) continue;
                            if (SetImport(path)) reimported++;
                            if (measure) Measure(path, v, t);
                        }

                        tracks.Add(t);
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                    EditorUtility.ClearProgressBar();
                }

                // Clips are loaded after the reimport so the references are the imported assets.
                foreach (MusicLibrary.Track t in tracks)
                {
                    string dir = MusicCatalog.Root + "/" + t.folder;
                    if (!Directory.Exists(dir)) continue;
                    foreach (string file in Directory.GetFiles(dir, "*.wav"))
                    {
                        string path = file.Replace('\\', '/');
                        if (!MusicCatalog.TryVariantOf(Path.GetFileNameWithoutExtension(path), out MusicVariant v)) continue;
                        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                        switch (v)
                        {
                            case MusicVariant.Intensity1: t.intensity1 = clip; break;
                            case MusicVariant.Intensity2: t.intensity2 = clip; break;
                            case MusicVariant.Main: t.main = clip; break;
                            case MusicVariant.Cut30: t.cut30 = clip; break;
                            default: t.cut60 = clip; break;
                        }
                    }

                    t.trimDb = Mathf.Min(0f, SoundMix.MusicTargetDb - t.mainRmsDb);
                    t.mood = new float[4];
                    for (int m = 0; m < 4; m++) t.mood[m] = MusicCatalog.MoodWeight((MusicMood)m, t.brightnessHz, t.pulse, t.onsets);
                    string note = string.Empty;
                    if (!t.Complete) { note = "  MISSING CLIPS"; problems++; }
                    else if (Mathf.Abs(t.main.length - t.duration) > 0.05f) { note = $"  LENGTH {t.main.length:0.000} s, catalogue {t.duration:0.000} s"; problems++; }
                    log.AppendLine($"{t.title} | {(t.enabled ? "on" : "OFF")} | {t.loop:0.000} | {t.tail:0.###} | {(float.IsNaN(t.tailDropDb) ? "-" : t.tailDropDb.ToString("0.0"))} | " +
                                   $"{t.intensity1RmsDb:0.0}/{t.intensity2RmsDb:0.0}/{t.mainRmsDb:0.0} | {t.trimDb:0.0} | {t.brightnessHz:0} | {t.pulse:0.00} | {t.onsets:0.00} | " +
                                   $"{t.mood[0]:0.00}/{t.mood[1]:0.00}/{t.mood[2]:0.00}/{t.mood[3]:0.00}{note}");
                }

                library.tracks = tracks;
                library.version = MusicCatalog.Version;
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssets();
                log.Insert(0, $"{(problems > 0 ? problems + " problem(s); " : string.Empty)}{reimported} clip import(s) changed. ");
                if (problems > 0) Debug.LogWarning(log.ToString(), library);
                else Debug.Log(log.ToString(), library);
                return library;
            }
            finally
            {
                building = false;
            }
        }

        /// <summary>Vorbis 0.65, Compressed In Memory, no preload, background loading, stereo. True when the settings changed.</summary>
        private static bool SetImport(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is AudioImporter importer)) return false;
            bool changed = Configure(importer);
            if (changed) importer.SaveAndReimport();
            return changed;
        }

        /// <summary>
        /// Gives an importer the music settings without reimporting; true when anything changed. Also used on a file's first
        /// import (<see cref="SoundImportRules"/>), so the 150 music files are encoded once.
        /// </summary>
        public static bool Configure(AudioImporter importer)
        {
            AudioImporterSampleSettings s = importer.defaultSampleSettings;
            bool same = s.loadType == AudioClipLoadType.CompressedInMemory && s.compressionFormat == AudioCompressionFormat.Vorbis &&
                        Mathf.Abs(s.quality - VorbisQuality) < 0.001f && !s.preloadAudioData && importer.loadInBackground && !importer.forceToMono;
            if (same) return false;
            s.loadType = AudioClipLoadType.CompressedInMemory;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = VorbisQuality;
            s.preloadAudioData = false;
            importer.defaultSampleSettings = s;
            importer.loadInBackground = true;
            importer.forceToMono = false;
            return true;
        }

        /// <summary>Re-measures one file: lengths, levels over the musical part, and the tail's fall.</summary>
        private static void Measure(string path, MusicVariant v, MusicLibrary.Track t)
        {
            if (!WavFile.TryRead(File.ReadAllBytes(path), out WavFile.Info info, out float[] samples))
            {
                Debug.LogWarning($"Music: could not read {path}.");
                return;
            }

            float seconds = (float)info.Seconds;
            long musical = (long)((seconds - t.tail) * info.SampleRate);
            switch (v)
            {
                case MusicVariant.Cut30: t.cut30Seconds = seconds; return;
                case MusicVariant.Cut60: t.cut60Seconds = seconds; return;
            }

            WavFile.Level(samples, info.Channels, 0, musical, out float rms, out _);
            if (v == MusicVariant.Intensity1) t.intensity1RmsDb = rms;
            else if (v == MusicVariant.Intensity2) t.intensity2RmsDb = rms;
            else
            {
                t.mainRmsDb = rms;
                t.duration = seconds;
                t.loop = seconds - t.tail;
                WavFile.Level(samples, info.Channels, Math.Max(0, musical - 4 * info.SampleRate), musical, out float body, out _);
                WavFile.Level(samples, info.Channels, musical, Math.Min(info.Frames, musical + info.SampleRate), out float ring, out _);
                t.tailDropDb = ring - body;
            }
        }
    }
}
