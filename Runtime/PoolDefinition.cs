using GameplayTags;
using UnityEngine;

namespace CharacterStats
{
    /// <summary>
    /// A quantity that is spent and comes back: stamina, health, mana, exhaustion.
    /// Everything about how it behaves is a stat, so all of it can be modified like any other stat.
    ///
    /// Stamina with exhaustion, where pushing past the exhaustion cap hurts and then stops you:
    ///  - Health: Rests At = Max;
    ///  - Exhaustion: Rests At = Zero, Max Stat = its cap, Regen Stat = how fast it recovers,
    ///    Overflow Into = Health (Overflow Drains on), Tag While Full = Exhausted;
    ///  - Stamina: Rests At = Max, Can Go Negative on, Overdraw Into = Exhaustion, Reserved By = Exhaustion,
    ///    When Overdraw Target Full = Block All Costs.
    /// </summary>
    [CreateAssetMenu(menuName = "Character Stats/Pool")]
    public class PoolDefinition : ScriptableObject
    {
        public enum RestPoint
        {
            /// <summary>Starts full and regenerates upwards (stamina, health).</summary>
            Max,

            /// <summary>Starts empty and "regenerates" downwards (exhaustion, a poison build-up).</summary>
            Zero,
        }

        public enum OverdrawLimit
        {
            /// <summary>Overdraw carries on. Whatever it would add to the full pool overflows from it instead (see its Overflow Into).</summary>
            None,

            /// <summary>Costs need the full amount again, and drains stop at zero.</summary>
            NoFurtherOverdraw,

            /// <summary>Nothing that costs from this pool can start, and running drains end.</summary>
            BlockAllCosts,
        }

        [Tooltip("Used in save data. Set it once and never change it. Empty = the asset's name.")]
        [SerializeField] string id;

        public string displayName;

        [Tooltip("The stat giving this pool's capacity.")]
        public StatDefinition maxStat;

        public RestPoint restsAt = RestPoint.Max;

        [Header("Regeneration (towards the rest point)")]
        [Tooltip("Amount per second. No stat = no regeneration. A negative value moves away from the rest point.")]
        public StatDefinition regenStat;

        [Tooltip("Seconds after the pool is pushed away from its rest point before regeneration resumes. No stat = no delay.")]
        public StatDefinition regenDelayStat;

        [Header("Spending")]
        [Tooltip("On: a cost can be paid with any amount above zero, taking the pool below zero. Off: the full cost is needed.")]
        public bool canGoNegative;

        [Tooltip("Optional. Whatever part of a spend lands below zero is added to this pool, times the conversion.")]
        public PoolDefinition overdrawInto;

        [Tooltip("Fraction of the overdrawn amount added to Overdraw Into. No stat = 1.")]
        public StatDefinition overdrawConversionStat;

        [Tooltip("What happens while the Overdraw Into pool is full (exhaustion at its cap). It applies again as soon as " +
                 "that pool drops below full.")]
        public OverdrawLimit whenOverdrawTargetFull = OverdrawLimit.None;

        [Tooltip("Optional. Multiplies costs taken from this pool (give it a base of 1); see ResourcePool.PriceOf. It is read " +
                 "with the cost's tags, so a modifier on it conditioned on the Movement tag makes movement cheaper.")]
        public StatDefinition costMultiplierStat;

        [Header("Overflow")]
        [Tooltip("Optional. Whatever is added to this pool beyond its max goes to this pool instead, " +
                 "credited to the same source (the ability that caused it).")]
        public PoolDefinition overflowInto;

        [Tooltip("Fraction of the overflow passed on. No stat = 1.")]
        public StatDefinition overflowConversionStat;

        [Tooltip("On: the overflow is taken out of that pool (damage to health). Off: it is added to it (a build-up).")]
        public bool overflowDrains = true;

        [Header("Being full")]
        [Tooltip("Once full, the pool counts as full until it drops to this fraction of its max. " +
                 "0.9 = until it recovers 10%; 0 = until it is completely empty; 1 = only while exactly at max. " +
                 "Keeps anything that waits on it (blocked costs, a knockout) from flickering as it recovers. " +
                 "Used when Full Release Stat is not set.")]
        [Range(0f, 1f)]
        public float fullReleaseFraction = 0.9f;

        [Tooltip("Optional. The release fraction as a stat (0-1), so it can be modified like anything else. Overrides the value above.")]
        public StatDefinition fullReleaseStat;

        [Header("Capacity")]
        [Tooltip("Optional. The amount currently in that pool is taken off this pool's capacity (exhaustion eating the stamina bar).")]
        public PoolDefinition reservedBy;

        [Header("Tags (need a TagHolder on the owner)")]
        [Tooltip("Optional. The owner has this tag while the pool is full (Exhausted). Other systems and modifier conditions can use it.")]
        public GameplayTag tagWhileFull;

        [Tooltip("Optional. The owner has this tag while the pool is below zero (Overdrawn).")]
        public GameplayTag tagWhileOverdrawn;

        public string Id => string.IsNullOrEmpty(id) ? name : id;
    }
}