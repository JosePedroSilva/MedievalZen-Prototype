using System;
using System.IO;
using MedievalZen.Sound;
using UnityEditor;
using UnityEngine;

namespace MedievalZen.EditorTools
{
    /// <summary>
    /// How a file under <c>Assets/Sounds</c> is imported the FIRST time (it has no .meta yet), so that 6 GB of WAV is encoded once
    /// and not twice: a clip the ambience catalog or the music catalog uses takes the settings its palette menu would give it
    /// (<see cref="AmbienceMenu.Configure"/>, <see cref="MusicMenu.Configure"/>), so the menus find nothing to change; every other
    /// clip of the packs (hundreds the game does not use) takes the cheapest import there is (ADPCM, no preload) instead of the
    /// default Vorbis encode. Unity compiles and loads editor code before it imports other assets in the same refresh, so this
    /// applies to the packs' very first import. Later imports are left alone: the settings are then the menus' or the user's.
    /// </summary>
    public sealed class SoundImportRules : AssetPostprocessor
    {
        private const string Root = "Assets/Sounds/";

        private void OnPreprocessAudio()
        {
            if (assetPath == null || !assetPath.StartsWith(Root, StringComparison.Ordinal)) return;
            if (!(assetImporter is AudioImporter importer) || !importer.importSettingsMissing) return;

            foreach (AmbienceClipSpec spec in AmbienceCatalog.All)
            {
                if (!string.Equals(spec.AssetPath, assetPath, StringComparison.Ordinal)) continue;
                AmbienceMenu.Configure(importer, spec);
                return;
            }

            if (IsMusic(assetPath))
            {
                MusicMenu.Configure(importer);
                return;
            }

            AudioImporterSampleSettings s = importer.defaultSampleSettings;
            s.loadType = AudioClipLoadType.CompressedInMemory;
            s.compressionFormat = AudioCompressionFormat.ADPCM;
            s.preloadAudioData = false;
            importer.defaultSampleSettings = s;
            importer.loadInBackground = true;
        }

        /// <summary>A variant file of one of the catalog's tracks.</summary>
        private static bool IsMusic(string path)
        {
            if (!MusicCatalog.TryVariantOf(Path.GetFileNameWithoutExtension(path), out _)) return false;
            foreach (MusicTrackInfo track in MusicCatalog.Tracks)
            {
                if (track != null && path.StartsWith(MusicCatalog.Root + "/" + track.Folder + "/", StringComparison.Ordinal)) return true;
            }

            return false;
        }

        /// <summary>
        /// The importer's "Normalize" flag, which has no public property: with Force To Mono on, Unity by default also raises the
        /// down-mix to full scale, which would make a clip trimmed from its measured level many decibels too loud (a quiet lake
        /// loop by 16 dB). True when the flag changed. Call it after every other change to the importer.
        /// </summary>
        public static bool SetNormalize(AudioImporter importer, bool normalize)
        {
            if (importer == null) return false;
            var serialized = new SerializedObject(importer);
            SerializedProperty property = serialized.FindProperty("m_Normalize");
            if (property == null)
            {
                Debug.LogWarning($"Sound import: {importer.assetPath} has no m_Normalize property; a mono clip may be imported louder than measured.");
                return false;
            }

            if (property.boolValue == normalize) return false;
            property.boolValue = normalize;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }
    }
}
