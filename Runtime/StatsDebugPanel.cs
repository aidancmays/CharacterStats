using System.Collections.Generic;
using GameplayTags;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && CHARACTER_STATS_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CharacterStats
{
    /// <summary>
    /// A debug window for a StatSheet (F5 by default; editor and development builds only): pools as bars (with
    /// reserved capacity, overdraw and regen delay), every tracked stat with its base and final value, the tags held,
    /// and a log of recent spends and restores. Add it to the character with the StatSheet.
    /// A project with its own debug overlay can call <see cref="DrawSheet"/> from it instead (no log).
    /// </summary>
    [AddComponentMenu("Character Stats/Stats Debug Panel")]
    public class StatsDebugPanel : MonoBehaviour
    {
        [Tooltip("The sheet shown. Empty = the one on this GameObject or a parent.")]
        [SerializeField] StatSheet sheet;

#if ENABLE_INPUT_SYSTEM && CHARACTER_STATS_INPUT_SYSTEM
        [SerializeField] Key toggleKey = Key.F5;
#else
        [SerializeField] KeyCode toggleKey = KeyCode.F5;
#endif
        [SerializeField] bool visible = true;
        [SerializeField] float width = 360f;

        [Tooltip("How many log lines to keep.")]
        [SerializeField] int logLines = 8;

        [Tooltip("Changes to the same pool, for the same reason, from the same source this close together share one log line (so drains read as one total).")]
        [SerializeField] float mergeWindow = 0.3f;

        class LogEntry
        {
            public float Time;
            public string Pool;
            public PoolChangeReason Reason;
            public object Source;
            public float Delta;
        }

        static readonly Color BarBackground = new Color(0f, 0f, 0f, 0.6f);
        static readonly Color FillRestsAtMax = new Color(0.35f, 0.8f, 0.35f);
        static readonly Color FillRestsAtZero = new Color(0.95f, 0.6f, 0.15f);
        static readonly Color ReservedColor = new Color(0.45f, 0.12f, 0.12f);
        static readonly Color NegativeColor = new Color(0.9f, 0.15f, 0.15f);

        static GUIStyle label, dim;
        static readonly List<StatDefinition> statScratch = new List<StatDefinition>();

        readonly List<LogEntry> log = new List<LogEntry>();
        Rect window;

        public bool Visible { get => visible; set => visible = value; }

        void Awake()
        {
            if (!Debug.isDebugBuild) enabled = false;
            if (sheet == null) sheet = GetComponentInParent<StatSheet>();
        }

        void OnEnable()
        {
            if (sheet != null) sheet.PoolChanged += HandlePoolChanged;
        }

        void OnDisable()
        {
            if (sheet != null) sheet.PoolChanged -= HandlePoolChanged;
        }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM && CHARACTER_STATS_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame) visible = !visible;
#else
            if (Input.GetKeyDown(toggleKey)) visible = !visible;
#endif
        }

        void OnGUI()
        {
            if (!visible) return;
            if (window.width <= 0f) window = new Rect(Screen.width - width - 10f, 10f, width, 10f);
            window.x = Mathf.Clamp(window.x, 0f, Mathf.Max(0f, Screen.width - window.width));
            window = GUILayout.Window(GetInstanceID(), window, id =>
            {
                DrawSheet(sheet, width);
                GUILayout.Space(4f);
                DrawLog();
                GUI.DragWindow();
            }, sheet != null ? "Stats: " + sheet.name : "Stats", GUILayout.Width(width));
        }

        void HandlePoolChanged(ResourcePool pool, PoolChange change)
        {
            if (change.Reason == PoolChangeReason.Regen) return; // continuous; the bar shows it

            float delta = change.Current - change.Previous;
            string poolName = NameOf(pool.Definition.displayName, pool.Definition);

            if (log.Count > 0)
            {
                LogEntry last = log[log.Count - 1];
                if (last.Pool == poolName && last.Reason == change.Reason && Equals(last.Source, change.Source)
                    && Time.time - last.Time <= mergeWindow)
                {
                    last.Delta += delta;
                    last.Time = Time.time;
                    return;
                }
            }

            log.Add(new LogEntry { Time = Time.time, Pool = poolName, Reason = change.Reason, Source = change.Source, Delta = delta });
            while (log.Count > Mathf.Max(1, logLines)) log.RemoveAt(0);
        }

        /// <summary>Pools, stats and tags of a sheet (IMGUI layout), for drawing inside another debug overlay.</summary>
        public static void DrawSheet(StatSheet sheet, float width = 360f)
        {
            EnsureStyles();
            if (sheet == null)
            {
                GUILayout.Label("No StatSheet found.", dim);
                return;
            }

            IReadOnlyList<ResourcePool> pools = sheet.Pools;
            for (int i = 0; i < pools.Count; i++) DrawPool(pools[i]);

            GUILayout.Space(4f);
            DrawStats(sheet, width);

            if (sheet.Tags != null)
            {
                var names = new List<string>();
                foreach (GameplayTag tag in sheet.Tags.Held) if (tag != null) names.Add(tag.name);
                names.Sort(string.CompareOrdinal);
                GUILayout.Space(4f);
                GUILayout.Label(names.Count > 0 ? "Tags: " + string.Join(", ", names) : "Tags: none", dim);
            }
        }

        static void DrawPool(ResourcePool pool)
        {
            float current = pool.Current;
            float max = pool.Max;
            float capacity = pool.Capacity;
            float reserved = pool.Reserved;

            string text = "<b>" + NameOf(pool.Definition.displayName, pool.Definition) + "</b>  ";
            text += current < 0f ? $"<color=#ff5555>{current:0.0}</color>" : $"{current:0.0}";
            text += $" / {max:0.0}";
            if (reserved > 0.05f) text += $"  <color=#cc7777>(cap {capacity:0.#}, {reserved:0.0} reserved)</color>";
            if (pool.RegenDelayLeft > 0f) text += $"  <color=#999999>regen in {pool.RegenDelayLeft:0.0}s</color>";
            if (pool.CostsBlocked) text += "  <color=#ff5555><b>BLOCKED</b></color>";
            else if (pool.Definition.canGoNegative && !pool.CanOverdraw) text += "  <color=#ffaa55>no overdraw</color>";
            if (pool.IsFull && pool.Definition.restsAt == PoolDefinition.RestPoint.Zero) text += "  <color=#ff5555>CAPPED</color>";
            GUILayout.Label(text, label);

            Rect bar = GUILayoutUtility.GetRect(10f, 10f, GUILayout.ExpandWidth(true));
            float scale = Mathf.Max(capacity, max, 1f);

            Fill(bar, 0f, 1f, BarBackground);
            if (reserved > 0f) Fill(bar, max / scale, Mathf.Max(capacity, max) / scale, ReservedColor);

            if (current > 0f)
            {
                Color fill = pool.Definition.restsAt == PoolDefinition.RestPoint.Max ? FillRestsAtMax : FillRestsAtZero;
                Fill(bar, 0f, current / scale, fill);
            }
            else if (current < 0f)
            {
                Fill(bar, 0f, -current / scale, NegativeColor); // how far below zero, on the same scale
            }

            GUILayout.Space(3f);
        }

        static void DrawStats(StatSheet sheet, float width)
        {
            statScratch.Clear();
            sheet.GetKnownStats(statScratch);
            statScratch.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            for (int i = 0; i < statScratch.Count; i++)
            {
                StatDefinition stat = statScratch[i];
                float baseValue = sheet.GetBase(stat);
                float final = sheet.Get(stat);
                int mods = sheet.CountModifiers(stat);

                GUILayout.BeginHorizontal();
                GUILayout.Label(NameOf(stat.displayName, stat), label, GUILayout.Width(width * 0.5f));

                string value = Mathf.Approximately(baseValue, final)
                    ? $"{final:0.##}"
                    : $"<color=#ffd54f>{final:0.##}</color>  <color=#999999>base {baseValue:0.##}</color>";
                if (mods > 0) value += $"  <color=#999999>{mods} mod{(mods == 1 ? "" : "s")}</color>";

                GUILayout.Label(value, label);
                GUILayout.EndHorizontal();
            }
        }

        void DrawLog()
        {
            if (log.Count == 0)
            {
                GUILayout.Label("No spends yet.", dim);
                return;
            }

            for (int i = log.Count - 1; i >= 0; i--)
            {
                LogEntry entry = log[i];
                string colour = entry.Delta < 0f ? "#ff8a80" : "#a5d6a7";
                GUILayout.Label(
                    $"<color=#999999>{Time.time - entry.Time,4:0.0}s</color>  {entry.Pool} <color={colour}>{entry.Delta:+0.0;-0.0}</color>  " +
                    $"<color=#999999>{entry.Reason}, {SourceName(entry.Source)}</color>",
                    label);
            }
        }

        static void Fill(Rect rect, float from, float to, Color colour)
        {
            from = Mathf.Clamp01(from);
            to = Mathf.Clamp01(to);
            if (to <= from) return;

            Color previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(new Rect(rect.x + rect.width * from, rect.y, rect.width * (to - from), rect.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        static string SourceName(object source)
        {
            if (source == null) return "-";
            var unityObject = source as Object;
            if (!ReferenceEquals(unityObject, null)) return unityObject != null ? unityObject.name : "destroyed";
            return source.ToString();
        }

        static string NameOf(string displayName, Object asset)
        {
            if (!string.IsNullOrEmpty(displayName)) return displayName;
            return asset != null ? asset.name : "?";
        }

        static void EnsureStyles()
        {
            if (label != null) return;

            label = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = false };
            label.margin = new RectOffset(2, 2, 0, 0);
            label.padding = new RectOffset(0, 0, 1, 1);

            dim = new GUIStyle(label);
            dim.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
        }
    }
}
