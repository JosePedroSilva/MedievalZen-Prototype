using UnityEngine;

namespace MedievalZen.Environment
{
    /// <summary>
    /// The one writer of <see cref="Season.Year"/> (2026-10-06): once a frame, the year for the user's choice
    /// (<see cref="Season.Choice"/>, set by the Journey menu) at the day cycle's day and hour. Under Auto the year starts at the
    /// identity (autumn) on day 1 and moves on one season every <see cref="Season.DaysPerSeason"/> days; the endless journey
    /// saves the day and hour, so a resumed journey resumes its season too. Creates itself in Play mode; runs just after
    /// <see cref="DayCycle"/> (−60), before the weather (−58) and the atmosphere (−55) read the year.
    /// </summary>
    [DefaultExecutionOrder(-59)]
    public sealed class SeasonCycle : MonoBehaviour
    {
        private static SeasonCycle instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateInPlayMode()
        {
            if (instance != null) return;
            var go = new GameObject("SeasonCycle") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SeasonCycle>();
            instance.Tick();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void Update() => Tick();

        private void Tick()
        {
            DayCycle cycle = DayCycle.Current;
            int day = cycle != null ? cycle.Day : 1;
            float hour = cycle != null ? cycle.Hour : 12f;
            Season.Set(Season.YearFor(Season.Choice, day, hour));
        }
    }
}
