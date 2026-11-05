using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MedievalZen.Wildlife
{
    /// <summary>
    /// The state graph of one Animator controller, read from its YAML (<c>.controller</c>): bool parameters, states and
    /// transitions with their conditions and exit times. Pure (no Unity calls), so the catalog can be checked against the
    /// pack's controllers in tests and by the palette builder, and the bool driver can be run against a model of each one.
    /// </summary>
    public sealed class ControllerGraph
    {
        /// <summary>A transition between states.</summary>
        public sealed class Transition
        {
            /// <summary>Destination state, or null for an exit (back to the default state).</summary>
            public string To;

            /// <summary>(parameter, required value) pairs; all must hold.</summary>
            public readonly List<KeyValuePair<string, bool>> Conditions = new List<KeyValuePair<string, bool>>();

            public bool HasExitTime;
            public float ExitTime;
            public bool FromAnyState;
            public bool CanTransitionToSelf;
        }

        /// <summary>A state.</summary>
        public sealed class State
        {
            public string Name;
            public float Speed = 1f;
            public readonly List<Transition> Out = new List<Transition>();
        }

        /// <summary>Parameter names by type ("bool", "float", "int", "trigger").</summary>
        public readonly Dictionary<string, string> Parameters = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>States by name.</summary>
        public readonly Dictionary<string, State> States = new Dictionary<string, State>(StringComparer.Ordinal);

        /// <summary>Any-state transitions of the root state machine.</summary>
        public readonly List<Transition> AnyState = new List<Transition>();

        /// <summary>Default state of the first layer.</summary>
        public string DefaultState;

        /// <summary>True when the controller has a bool parameter of this name.</summary>
        public bool HasBool(string name) => Parameters.TryGetValue(name, out string type) && type == "bool";

        /// <summary>The state Idle goes to when <paramref name="boolName"/> is set (null when there is none).</summary>
        public string TargetOf(string boolName)
        {
            if (DefaultState == null || !States.TryGetValue(DefaultState, out State idle)) return null;
            foreach (Transition t in idle.Out)
            {
                if (t.Conditions.Count == 1 && t.Conditions[0].Key == boolName && t.Conditions[0].Value) return t.To ?? DefaultState;
            }

            foreach (Transition t in AnyState)
            {
                if (t.Conditions.Count == 1 && t.Conditions[0].Key == boolName && t.Conditions[0].Value) return t.To;
            }

            return null;
        }

        /// <summary>Every state Idle (or any state) can go to when <paramref name="boolName"/> is set (some controllers have two).</summary>
        public List<string> TargetsOf(string boolName)
        {
            var list = new List<string>();
            if (DefaultState != null && States.TryGetValue(DefaultState, out State idle))
            {
                foreach (Transition t in idle.Out)
                {
                    if (t.Conditions.Count == 1 && t.Conditions[0].Key == boolName && t.Conditions[0].Value) list.Add(t.To ?? DefaultState);
                }
            }

            foreach (Transition t in AnyState)
            {
                if (t.Conditions.Count == 1 && t.Conditions[0].Key == boolName && t.Conditions[0].Value && t.To != null) list.Add(t.To);
            }

            return list;
        }

        /// <summary>True when a state has no way out at all (a death state): the game must never enter it.</summary>
        public bool IsSticky(string state) => States.TryGetValue(state, out State s) && s.Out.Count == 0 && state != DefaultState;

        // ------------------------------------------------------------------ parsing

        private static readonly Regex DocSplit = new Regex(@"^--- !u!(\d+) &(-?\d+)[^\n]*\n", RegexOptions.Multiline);
        private static readonly Regex ParamRx = new Regex(@"- m_Name: (\S+)\s+m_Type: (\d+)");
        private static readonly Regex FileIdRx = new Regex(@"fileID: (-?\d+)");
        private static readonly Regex ConditionRx = new Regex(@"m_ConditionMode: (\d+)\s+m_ConditionEvent: (\S+)");

        /// <summary>Parses a controller's YAML text.</summary>
        public static ControllerGraph Parse(string yaml)
        {
            var graph = new ControllerGraph();
            if (string.IsNullOrEmpty(yaml)) return graph;
            var docs = new Dictionary<string, KeyValuePair<int, string>>(StringComparer.Ordinal);
            MatchCollection heads = DocSplit.Matches(yaml);
            for (int i = 0; i < heads.Count; i++)
            {
                int start = heads[i].Index + heads[i].Length;
                int end = i + 1 < heads.Count ? heads[i + 1].Index : yaml.Length;
                docs[heads[i].Groups[2].Value] = new KeyValuePair<int, string>(int.Parse(heads[i].Groups[1].Value, CultureInfo.InvariantCulture), yaml.Substring(start, end - start));
            }

            string rootMachine = null;
            foreach (KeyValuePair<string, KeyValuePair<int, string>> d in docs)
            {
                if (d.Value.Key != 91) continue;   // AnimatorController
                string body = d.Value.Value;
                foreach (Match m in ParamRx.Matches(body))
                {
                    string type = m.Groups[2].Value == "4" ? "bool" : m.Groups[2].Value == "1" ? "float" : m.Groups[2].Value == "3" ? "int" : "trigger";
                    graph.Parameters[m.Groups[1].Value] = type;
                }

                Match sm = Regex.Match(body, @"m_StateMachine: \{fileID: (-?\d+)\}");
                if (sm.Success) rootMachine = sm.Groups[1].Value;
            }

            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, KeyValuePair<int, string>> d in docs)
            {
                if (d.Value.Key != 1102) continue;   // AnimatorState
                string body = d.Value.Value;
                var state = new State { Name = Field(body, "m_Name") };
                if (float.TryParse(Field(body, "m_Speed"), NumberStyles.Float, CultureInfo.InvariantCulture, out float speed)) state.Speed = speed;
                names[d.Key] = state.Name;
                graph.States[state.Name] = state;
            }

            foreach (KeyValuePair<string, KeyValuePair<int, string>> d in docs)
            {
                if (d.Value.Key != 1102) continue;
                State state = graph.States[names[d.Key]];
                foreach (string id in ListOf(d.Value.Value, "m_Transitions"))
                {
                    Transition t = ReadTransition(docs, names, id);
                    if (t != null) state.Out.Add(t);
                }
            }

            foreach (KeyValuePair<string, KeyValuePair<int, string>> d in docs)
            {
                if (d.Value.Key != 1107) continue;   // AnimatorStateMachine
                bool isRoot = rootMachine == null || d.Key == rootMachine;
                if (!isRoot) continue;
                Match def = Regex.Match(d.Value.Value, @"m_DefaultState: \{fileID: (-?\d+)\}");
                if (def.Success && names.TryGetValue(def.Groups[1].Value, out string n)) graph.DefaultState = n;
                foreach (string id in ListOf(d.Value.Value, "m_AnyStateTransitions"))
                {
                    Transition t = ReadTransition(docs, names, id);
                    if (t == null) continue;
                    t.FromAnyState = true;
                    graph.AnyState.Add(t);
                }
            }

            return graph;
        }

        private static Transition ReadTransition(Dictionary<string, KeyValuePair<int, string>> docs, Dictionary<string, string> names, string id)
        {
            if (!docs.TryGetValue(id, out KeyValuePair<int, string> doc)) return null;
            string body = doc.Value;
            var t = new Transition();
            foreach (Match m in ConditionRx.Matches(body))
            {
                string mode = m.Groups[1].Value;
                if (mode == "1") t.Conditions.Add(new KeyValuePair<string, bool>(m.Groups[2].Value, true));
                else if (mode == "2") t.Conditions.Add(new KeyValuePair<string, bool>(m.Groups[2].Value, false));
            }

            Match dst = Regex.Match(body, @"m_DstState: \{fileID: (-?\d+)\}");
            bool exit = Field(body, "m_IsExit") == "1";
            t.To = exit ? null : dst.Success && names.TryGetValue(dst.Groups[1].Value, out string to) ? to : null;
            t.HasExitTime = Field(body, "m_HasExitTime") == "1";
            float.TryParse(Field(body, "m_ExitTime"), NumberStyles.Float, CultureInfo.InvariantCulture, out t.ExitTime);
            t.CanTransitionToSelf = Field(body, "m_CanTransitionToSelf") == "1";
            return t;
        }

        private static string Field(string body, string key)
        {
            Match m = Regex.Match(body, "^  " + key + @": ?(.*)$", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value.Trim() : string.Empty;
        }

        private static List<string> ListOf(string body, string key)
        {
            var ids = new List<string>();
            string[] lines = body.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (!line.StartsWith("  " + key + ":", StringComparison.Ordinal)) continue;
                // Flow style on the same line ("[]" or "[{fileID: …}]"), then block items ("  - {fileID: …}").
                Collect(line.Substring(key.Length + 3), ids);
                for (int j = i + 1; j < lines.Length && lines[j].StartsWith("  - ", StringComparison.Ordinal); j++) Collect(lines[j], ids);
                break;
            }

            return ids;
        }

        private static void Collect(string text, List<string> ids)
        {
            foreach (Match f in FileIdRx.Matches(text))
            {
                if (f.Groups[1].Value != "0") ids.Add(f.Groups[1].Value);
            }
        }

        /// <summary>
        /// Checks a catalog row against this graph: every bound bool exists and leads somewhere from Idle, nothing leads into a
        /// sticky state, no death bool is used, and the idle state matches. Returns the problems (empty when it fits).
        /// </summary>
        public List<string> Check(SpeciesInfo info)
        {
            var problems = new List<string>();
            if (DefaultState != info.IdleState) problems.Add($"{info.Name}: idle state is '{DefaultState}', catalog says '{info.IdleState}'");
            foreach (PoseBinding b in info.Poses)
            {
                if (b.Bool.IndexOf("Dead", StringComparison.OrdinalIgnoreCase) >= 0) problems.Add($"{info.Name}: {b.Pose} uses a death bool '{b.Bool}'");
                if (!HasBool(b.Bool))
                {
                    problems.Add($"{info.Name}: {b.Pose} uses '{b.Bool}', which {info.Controller} lacks");
                    continue;
                }

                string target = TargetOf(b.Bool);
                if (target == null)
                {
                    problems.Add($"{info.Name}: '{b.Bool}' leads nowhere from '{DefaultState}'");
                    continue;
                }

                // Follow exit-time chains (lie down → sleep); none may end in a state without a way out.
                string s = target;
                for (int guard = 0; guard < 6 && s != null; guard++)
                {
                    if (IsSticky(s))
                    {
                        problems.Add($"{info.Name}: '{b.Bool}' leads to '{s}', which has no way out");
                        break;
                    }

                    State st = States[s];
                    Transition next = null;
                    foreach (Transition t in st.Out)
                    {
                        if (t.Conditions.Count == 0) next = t;
                    }

                    s = next != null ? next.To : null;
                }
            }

            return problems;
        }
    }

    /// <summary>
    /// A small model of the Animator running a <see cref="ControllerGraph"/>: one layer, transitions taken instantly when
    /// their conditions hold and (if they have one) their exit time has passed, clips one second long scaled by the state's
    /// speed. Good enough to check that the bool driver reaches every pose and never gets stuck (tests only need this).
    /// </summary>
    public sealed class ControllerModel : IAnimatorBoolSink
    {
        private readonly ControllerGraph graph;
        private readonly SpeciesInfo info;
        private readonly Dictionary<string, bool> bools = new Dictionary<string, bool>(StringComparer.Ordinal);
        private float normalized;
        private float transitionLeft;

        /// <summary>Creates the model in the default state.</summary>
        public ControllerModel(ControllerGraph graph, SpeciesInfo info)
        {
            this.graph = graph;
            this.info = info;
            Current = graph.DefaultState;
        }

        /// <summary>The state playing.</summary>
        public string Current { get; private set; }

        /// <summary>Every bool ever set true (to check that no death bool was touched).</summary>
        public readonly HashSet<string> EverSet = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Largest number of bools true at the same time.</summary>
        public int MaxTrueAtOnce { get; private set; }

        /// <summary>True while in the default state and not leaving it.</summary>
        public bool InIdle => Current == graph.DefaultState && transitionLeft <= 0f;

        /// <summary>Value of a bool.</summary>
        public bool Get(string name) => bools.TryGetValue(name, out bool v) && v;

        /// <inheritdoc />
        public void SetBool(int boolIndex, bool value)
        {
            string name = info.Bools[boolIndex];
            bools[name] = value;
            if (value) EverSet.Add(name);
            int n = 0;
            foreach (bool b in bools.Values)
            {
                if (b) n++;
            }

            MaxTrueAtOnce = Math.Max(MaxTrueAtOnce, n);
        }

        /// <summary>Advances the model by <paramref name="dt"/> seconds.</summary>
        public void Step(float dt)
        {
            if (transitionLeft > 0f)
            {
                transitionLeft -= dt;
                return;
            }

            ControllerGraph.State state = graph.States[Current];
            float previous = normalized;
            normalized += dt * Math.Abs(state.Speed);
            foreach (ControllerGraph.Transition t in graph.AnyState)
            {
                if (!t.CanTransitionToSelf && t.To == Current) continue;
                if (!Holds(t, previous)) continue;
                Go(t.To);
                return;
            }

            foreach (ControllerGraph.Transition t in state.Out)
            {
                if (!Holds(t, previous)) continue;
                Go(t.To ?? graph.DefaultState);
                return;
            }
        }

        private bool Holds(ControllerGraph.Transition t, float previous)
        {
            foreach (KeyValuePair<string, bool> c in t.Conditions)
            {
                if (Get(c.Key) != c.Value) return false;
            }

            if (!t.HasExitTime) return true;
            // Exit time below 1 repeats each loop; at or above 1 it is reached once.
            float exit = t.ExitTime;
            if (exit >= 1f) return normalized >= exit;
            float a = previous - (float)Math.Floor(previous), b = normalized - (float)Math.Floor(previous);
            return (a < exit && b >= exit) || (b >= 1f + exit) || (exit <= 0f && Math.Floor(normalized) > Math.Floor(previous));
        }

        private void Go(string to)
        {
            Current = to;
            normalized = 0f;
            transitionLeft = 0.25f;
        }
    }
}
