using System;
using System.Collections.Generic;
using GameplayTags;
using UnityEngine;

namespace CharacterStats
{
    /// <summary>
    /// Everything with stats has one of these: the player, an enemy, a destructible door.
    /// It holds base values (changed by levelling), modifiers (added and removed by gear, effects, abilities,
    /// each with a source) and pools (stamina, health...). It knows nothing about abilities or movement.
    ///
    /// Reading a stat:
    ///  - Get(stat): the everyday value. Cached. Modifiers conditioned on the action or the other party do not apply.
    ///  - Get(stat, query): the value in a particular situation (this attack, against that target). Worked out each call.
    /// </summary>
    [DefaultExecutionOrder(-95)] // early, so other scripts' Awake can read stats and pools
    public class StatSheet : MonoBehaviour
    {
        [SerializeField] StatProfile profile;

        /// <summary>The owner's tags, if it has a TagHolder. Can be null.</summary>
        public TagHolder Tags { get; private set; }

        /// <summary>(stat, previous, current) for the everyday value of any stat that has been read or is in the profile.</summary>
        public event Action<StatDefinition, float, float> StatChanged;

        public event Action<ResourcePool, PoolChange> PoolChanged;

        struct Applied
        {
            public StatModifierSpec Spec;
            public object Source;
        }

        readonly Dictionary<StatDefinition, float> bases = new Dictionary<StatDefinition, float>();
        readonly Dictionary<StatDefinition, float> cache = new Dictionary<StatDefinition, float>();
        readonly Dictionary<StatDefinition, List<Applied>> modifiers = new Dictionary<StatDefinition, List<Applied>>();
        readonly Dictionary<PoolDefinition, ResourcePool> pools = new Dictionary<PoolDefinition, ResourcePool>();
        readonly List<ResourcePool> poolList = new List<ResourcePool>();
        readonly List<KeyValuePair<StatDefinition, float>> scratch = new List<KeyValuePair<StatDefinition, float>>();

        int depth;
        bool applied;
        bool refreshing;
        bool refreshQueued;

        public IReadOnlyList<ResourcePool> Pools => poolList;

        /// <summary>The profile the sheet was set up from. Null if none yet.</summary>
        public StatProfile Profile => profile;

        void Awake()
        {
            Tags = GetComponent<TagHolder>();
            if (Tags != null) Tags.Changed += Refresh;

            if (profile != null) ApplyProfile();
        }

        /// <summary>
        /// Sets the sheet up from a profile, for a sheet added from code (AddComponent runs Awake before you can set
        /// the profile). Only works once, and only if no profile was set in the inspector.
        /// </summary>
        public void Setup(StatProfile newProfile)
        {
            if (newProfile == null) return;
            if (applied)
            {
                Debug.LogWarning($"{name}: this StatSheet is already set up from '{profile.name}'.", this);
                return;
            }

            profile = newProfile;
            ApplyProfile();
            Refresh();
        }

        void ApplyProfile()
        {
            applied = true;

            foreach (StatProfile.BaseValue entry in profile.baseValues)
            {
                if (entry.stat != null) bases[entry.stat] = entry.value;
            }

            foreach (StatModifierSpec spec in profile.innateModifiers)
            {
                AddWithoutRefresh(spec, this);
            }

            // Read each once so they are cached and StatChanged covers them from the start.
            foreach (StatProfile.BaseValue entry in profile.baseValues)
            {
                if (entry.stat != null) Get(entry.stat);
            }

            foreach (PoolDefinition definition in profile.pools)
            {
                if (definition == null || pools.ContainsKey(definition)) continue;

                ResourcePool pool = new ResourcePool(definition, this);
                pools.Add(definition, pool);
                poolList.Add(pool);
            }

            // After all exist: a pool's starting amount can depend on another pool.
            foreach (ResourcePool pool in poolList) pool.Initialise();
        }

        void OnDestroy()
        {
            if (Tags != null) Tags.Changed -= Refresh;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < poolList.Count; i++) poolList[i].Tick(dt);
        }

        // ---- Stats ----

        public float GetBase(StatDefinition stat)
        {
            if (stat == null) return 0f;
            return bases.TryGetValue(stat, out float value) ? value : stat.defaultBase;
        }

        /// <summary>Sets a stat's base value (levelling up, spending attribute points).</summary>
        public void SetBase(StatDefinition stat, float value)
        {
            if (stat == null) return;

            Get(stat); // make sure it is cached, so the change is reported
            bases[stat] = value;
            Refresh();
        }

        /// <summary>The everyday value of a stat.</summary>
        public float Get(StatDefinition stat)
        {
            if (stat == null) return 0f;
            if (cache.TryGetValue(stat, out float cached)) return cached;

            float value = Compute(stat, default);
            cache[stat] = value;
            return value;
        }

        /// <summary>The value of a stat in a particular situation, with conditional modifiers taken into account.</summary>
        public float Get(StatDefinition stat, in StatQuery query)
        {
            return stat == null ? 0f : Compute(stat, query);
        }

        float Compute(StatDefinition stat, in StatQuery query)
        {
            if (depth > 16)
            {
                Debug.LogError($"StatSheet: '{stat.name}' depends on itself through Scaled By modifiers.", this);
                return 0f;
            }

            depth++;

            float flat = 0f;
            float percentAdd = 0f;
            float multiply = 1f;
            bool overridden = false;
            int overridePriority = int.MinValue;
            float overrideValue = 0f;

            // The stat's own modifiers, then those of each stat it inherits from.
            StatDefinition level = stat;
            for (int guard = 0; level != null && guard < 16; guard++, level = level.InheritsFrom)
            {
                if (!modifiers.TryGetValue(level, out List<Applied> list)) continue;

                for (int i = 0; i < list.Count; i++)
                {
                    StatModifierSpec spec = list[i].Spec;
                    if (!spec.ConditionsMet(this, query)) continue;

                    float value = spec.value;
                    if (spec.scaledBy != null) value *= Get(spec.scaledBy);

                    switch (spec.op)
                    {
                        case ModifierOp.Flat:
                            flat += value;
                            break;
                        case ModifierOp.PercentAdd:
                            percentAdd += value;
                            break;
                        case ModifierOp.PercentMultiply:
                            multiply *= 1f + value;
                            break;
                        case ModifierOp.Override:
                            if (!overridden || spec.priority >= overridePriority)
                            {
                                overridden = true;
                                overridePriority = spec.priority;
                                overrideValue = value;
                            }
                            break;
                    }
                }
            }

            depth--;

            float result = overridden ? overrideValue : (GetBase(stat) + flat) * (1f + percentAdd) * multiply;
            return stat.Clamp(result);
        }

        /// <summary>Adds every stat this sheet currently tracks (those in its profile and any read since). For debugging and UI.</summary>
        public void GetKnownStats(List<StatDefinition> results)
        {
            foreach (StatDefinition stat in cache.Keys) results.Add(stat);
        }

        /// <summary>How many modifiers are applied directly to a stat (not counting those on stats it inherits from).</summary>
        public int CountModifiers(StatDefinition stat)
        {
            return stat != null && modifiers.TryGetValue(stat, out List<Applied> list) ? list.Count : 0;
        }

        // ---- Modifiers ----

        /// <param name="source">Whatever is applying it (an item, an effect, an ability instance); pass the same object to remove it.</param>
        public void AddModifier(StatModifierSpec spec, object source)
        {
            if (AddWithoutRefresh(spec, source)) Refresh();
        }

        public void AddModifiers(IEnumerable<StatModifierSpec> specs, object source)
        {
            if (specs == null) return;

            bool any = false;
            foreach (StatModifierSpec spec in specs) any |= AddWithoutRefresh(spec, source);
            if (any) Refresh();
        }

        /// <summary>Removes one modifier a source applied.</summary>
        public void RemoveModifier(StatModifierSpec spec, object source)
        {
            if (spec == null || spec.stat == null || !modifiers.TryGetValue(spec.stat, out List<Applied> list)) return;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Spec != spec || !Equals(list[i].Source, source)) continue;

                list.RemoveAt(i);
                Refresh();
                return;
            }
        }

        /// <summary>Removes everything a source applied (an item unequipped, an effect ending).</summary>
        public void RemoveModifiers(object source)
        {
            if (source == null) return;

            int removed = 0;
            foreach (List<Applied> list in modifiers.Values)
            {
                removed += list.RemoveAll(applied => Equals(applied.Source, source));
            }

            if (removed > 0) Refresh();
        }

        bool AddWithoutRefresh(StatModifierSpec spec, object source)
        {
            if (spec == null || spec.stat == null || source == null) return false;

            if (!modifiers.TryGetValue(spec.stat, out List<Applied> list))
            {
                list = new List<Applied>();
                modifiers.Add(spec.stat, list);
            }

            list.Add(new Applied { Spec = spec, Source = source });
            return true;
        }

        /// <summary>Works every cached stat out again and reports the ones that changed.</summary>
        void Refresh()
        {
            if (refreshing)
            {
                refreshQueued = true; // something changed from inside a StatChanged handler: go round again
                return;
            }

            refreshing = true;
            do
            {
                refreshQueued = false;

                scratch.Clear();
                foreach (KeyValuePair<StatDefinition, float> pair in cache) scratch.Add(pair);
                cache.Clear();

                for (int i = 0; i < scratch.Count; i++)
                {
                    float now = Get(scratch[i].Key);
                    if (!Mathf.Approximately(now, scratch[i].Value))
                    {
                        StatChanged?.Invoke(scratch[i].Key, scratch[i].Value, now);
                    }
                }
            } while (refreshQueued);

            refreshing = false;
        }

        // ---- Pools ----

        public bool TryGetPool(PoolDefinition definition, out ResourcePool pool)
        {
            pool = null;
            return definition != null && pools.TryGetValue(definition, out pool);
        }

        internal void RaisePoolChanged(ResourcePool pool, PoolChange change)
        {
            PoolChanged?.Invoke(pool, change);
        }

        // ---- Saving ----

        public StatSheetSnapshot Capture()
        {
            StatSheetSnapshot snapshot = new StatSheetSnapshot();

            foreach (KeyValuePair<StatDefinition, float> pair in bases)
            {
                snapshot.baseValues.Add(new StatSheetSnapshot.Entry { id = pair.Key.Id, value = pair.Value });
            }

            foreach (ResourcePool pool in poolList)
            {
                snapshot.pools.Add(new StatSheetSnapshot.Entry { id = pool.Definition.Id, value = pool.Current });
            }

            return snapshot;
        }

        /// <summary>
        /// Applies saved values. Only stats and pools listed in the profile can be restored (that is how an id is
        /// matched back to its asset); anything else in the snapshot is ignored.
        /// </summary>
        public void Restore(StatSheetSnapshot snapshot)
        {
            if (snapshot == null) return;

            scratch.Clear();
            foreach (KeyValuePair<StatDefinition, float> pair in bases) scratch.Add(pair);

            foreach (StatSheetSnapshot.Entry entry in snapshot.baseValues)
            {
                for (int i = 0; i < scratch.Count; i++)
                {
                    if (scratch[i].Key.Id != entry.id) continue;
                    bases[scratch[i].Key] = entry.value;
                    break;
                }
            }

            Refresh();

            foreach (StatSheetSnapshot.Entry entry in snapshot.pools)
            {
                foreach (ResourcePool pool in poolList)
                {
                    if (pool.Definition.Id != entry.id) continue;
                    pool.SetCurrent(entry.value);
                    break;
                }
            }
        }
    }
}