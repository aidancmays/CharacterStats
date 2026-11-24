using System;
using System.Collections.Generic;
using UnityEngine;

namespace CharacterStats
{
    public enum PoolChangeReason
    {
        Spend,
        Restore,
        Regen,
        /// <summary>The capacity shrank below the current amount.</summary>
        Clamp,
        /// <summary>Set directly (loading a save, debugging).</summary>
        Set,
    }

    public readonly struct PoolChange
    {
        public readonly float Previous;
        public readonly float Current;
        public readonly PoolChangeReason Reason;

        /// <summary>Whatever caused it (an ability instance, an effect, an item...), if the caller said. Can be null.</summary>
        public readonly object Source;

        public PoolChange(float previous, float current, PoolChangeReason reason, object source)
        {
            Previous = previous;
            Current = current;
            Reason = reason;
            Source = source;
        }
    }

    /// <summary>One character's amount of a pool. Created and ticked by its StatSheet.</summary>
    public class ResourcePool
    {
        const float FullTolerance = 0.001f;
        const int MaxChain = 8; // overdraw and overflow passing amounts between pools, in case they are set up in a loop

        static int chainDepth;

        public PoolDefinition Definition { get; }
        public float Current { get; private set; }

        public event Action<ResourcePool, PoolChange> Changed;

        /// <summary>
        /// Raised with true when the pool becomes full, and false when it stops being full (also when its max grows).
        /// For exhaustion, true means it has hit its cap.
        /// </summary>
        public event Action<ResourcePool, bool> FullChanged;

        /// <summary>Raised with true when the pool goes below zero, and false when it is back at or above zero.</summary>
        public event Action<ResourcePool, bool> OverdrawnChanged;

        readonly StatSheet sheet;
        float regenDelayLeft;
        bool wasFull;
        bool wasOverdrawn;

        internal ResourcePool(PoolDefinition definition, StatSheet sheet)
        {
            Definition = definition;
            this.sheet = sheet;
        }

        /// <summary>Capacity from the max stat, before anything is reserved.</summary>
        public float Capacity => Definition.maxStat != null ? sheet.Get(Definition.maxStat) : 0f;

        /// <summary>How much of the capacity is taken up by the Reserved By pool.</summary>
        public float Reserved =>
            Definition.reservedBy != null && sheet.TryGetPool(Definition.reservedBy, out ResourcePool other)
                ? Mathf.Max(0f, other.Current)
                : 0f;

        /// <summary>The most this pool can hold right now: capacity less what is reserved.</summary>
        public float Max => Mathf.Max(0f, Capacity - Reserved);

        /// <summary>Seconds until regeneration resumes. 0 = regenerating.</summary>
        public float RegenDelayLeft => Mathf.Max(0f, regenDelayLeft);

        /// <summary>
        /// Reached its max, and has not yet dropped back to its release point (see Full Release Fraction).
        /// A pool whose max is zero never counts as full.
        /// </summary>
        public bool IsFull => wasFull;

        /// <summary>Below zero.</summary>
        public bool IsOverdrawn => Current < 0f;

        /// <summary>True while the Overdraw Into pool is full (exhaustion at its cap).</summary>
        public bool OverdrawTargetFull =>
            Definition.overdrawInto != null
            && sheet.TryGetPool(Definition.overdrawInto, out ResourcePool target)
            && target.IsFull;

        /// <summary>Whether a spend may take this pool below zero right now.</summary>
        public bool CanOverdraw =>
            Definition.canGoNegative
            && !(Definition.whenOverdrawTargetFull != PoolDefinition.OverdrawLimit.None && OverdrawTargetFull);

        /// <summary>True while nothing that costs from this pool may start or keep draining it.</summary>
        public bool CostsBlocked =>
            Definition.whenOverdrawTargetFull == PoolDefinition.OverdrawLimit.BlockAllCosts && OverdrawTargetFull;

        internal void Initialise()
        {
            Current = Definition.restsAt == PoolDefinition.RestPoint.Max ? Max : 0f;
            regenDelayLeft = 0f;
            UpdateFlags();
        }

        /// <summary>
        /// Could a cost be paid now? <paramref name="requirement"/> is the least the pool must hold, whatever the cost.
        /// While the pool may overdraw it only needs to be above zero; otherwise it needs the full cost.
        /// </summary>
        /// <summary>
        /// A cost after the pool's Cost Multiplier Stat (if it has one), read with the cost's tags so conditional
        /// modifiers apply (cheaper Movement costs, dearer Fire spells). Pass the result to CanPay and Spend.
        /// </summary>
        public float PriceOf(float cost, IReadOnlyList<GameplayTags.GameplayTag> costTags = null)
        {
            StatDefinition multiplier = Definition.costMultiplierStat;
            if (multiplier == null) return cost;
            return cost * Mathf.Max(0f, sheet.Get(multiplier, new StatQuery(costTags)));
        }

        public bool CanPay(float cost, float requirement = 0f)
        {
            if (CostsBlocked) return false;
            if (Current < requirement) return false;
            if (cost <= 0f) return true;
            return CanOverdraw ? Current > 0f : Current >= cost;
        }

        /// <summary>
        /// Takes an amount out. If the pool may overdraw and <paramref name="allowOverdraw"/> is on, it may end up
        /// below zero, and the part below zero is passed to the Overdraw Into pool. Otherwise it stops at zero.
        /// </summary>
        /// <returns>The amount actually taken.</returns>
        public float Spend(float amount, bool allowOverdraw = true, object source = null)
        {
            if (amount <= 0f) return 0f;

            // Stopping at zero must never raise a pool that is already below it.
            float floor = CanOverdraw && allowOverdraw ? float.NegativeInfinity : Mathf.Min(0f, Current);
            float next = Mathf.Max(Current - amount, floor);
            float spent = Current - next;
            if (spent <= 0f) return 0f;

            float overdrawn = Mathf.Min(spent, Mathf.Max(0f, -next));

            if (Definition.restsAt == PoolDefinition.RestPoint.Max) RestartRegenDelay();
            Set(next, PoolChangeReason.Spend, source);

            if (overdrawn > 0f && Definition.overdrawInto != null && sheet.TryGetPool(Definition.overdrawInto, out ResourcePool into))
            {
                float conversion = Definition.overdrawConversionStat != null ? sheet.Get(Definition.overdrawConversionStat) : 1f;
                PassOn(into, overdrawn * conversion, false, source);
            }

            return spent;
        }

        /// <summary>
        /// Puts an amount in, up to Max. Whatever does not fit goes to the Overflow Into pool, if there is one.
        /// </summary>
        /// <returns>The amount actually added to this pool.</returns>
        public float Restore(float amount, object source = null)
        {
            if (amount <= 0f) return 0f;

            float next = Mathf.Min(Current + amount, Mathf.Max(Max, Current));
            float added = Mathf.Max(0f, next - Current);

            if (added > 0f)
            {
                if (Definition.restsAt == PoolDefinition.RestPoint.Zero) RestartRegenDelay();
                Set(next, PoolChangeReason.Restore, source);
            }

            float overflow = amount - added;
            if (overflow > 0f && Definition.overflowInto != null && sheet.TryGetPool(Definition.overflowInto, out ResourcePool into))
            {
                float conversion = Definition.overflowConversionStat != null ? sheet.Get(Definition.overflowConversionStat) : 1f;
                PassOn(into, overflow * conversion, Definition.overflowDrains, source);
            }

            return added;
        }

        /// <summary>Sets the amount directly, with no limits applied and no side effects (loading a save, debugging).</summary>
        public void SetCurrent(float value, object source = null)
        {
            Set(value, PoolChangeReason.Set, source);
        }

        internal void Tick(float dt)
        {
            float max = Max;

            // Capacity shrank (an item came off, exhaustion grew): the amount is kept as it is, up to the new limit.
            if (Current > max) Set(max, PoolChangeReason.Clamp, null);

            // The max can change without the amount changing (an item, the stat it scales with).
            UpdateFlags();

            if (regenDelayLeft > 0f)
            {
                regenDelayLeft -= dt;
                return;
            }

            if (Definition.regenStat == null) return;

            float rate = sheet.Get(Definition.regenStat);
            if (Definition.restsAt == PoolDefinition.RestPoint.Zero) rate = -rate;
            if (rate == 0f) return;

            float lowest = Definition.canGoNegative ? float.NegativeInfinity : 0f;
            if (Definition.restsAt == PoolDefinition.RestPoint.Zero) lowest = 0f;

            float next = Mathf.Clamp(Current + rate * dt, Mathf.Min(lowest, Current), max);
            if (next != Current) Set(next, PoolChangeReason.Regen, null);
        }

        static void PassOn(ResourcePool target, float amount, bool drain, object source)
        {
            if (amount <= 0f) return;

            if (chainDepth >= MaxChain)
            {
                Debug.LogError("ResourcePool: overdraw/overflow pools pass amounts round in a loop. Check Overdraw Into and Overflow Into.");
                return;
            }

            chainDepth++;
            try
            {
                if (drain) target.Spend(amount, true, source);
                else target.Restore(amount, source);
            }
            finally
            {
                chainDepth--;
            }
        }

        void RestartRegenDelay()
        {
            regenDelayLeft = Definition.regenDelayStat != null ? Mathf.Max(0f, sheet.Get(Definition.regenDelayStat)) : 0f;
        }

        void Set(float value, PoolChangeReason reason, object source)
        {
            if (value == Current) return;

            PoolChange change = new PoolChange(Current, value, reason, source);
            Current = value;
            Changed?.Invoke(this, change);
            sheet.RaisePoolChanged(this, change);
            UpdateFlags();
        }

        bool ComputeFull()
        {
            float max = Max;
            if (max <= 0f) return false;

            if (!wasFull) return Current >= max - FullTolerance;

            // Already full: stays so until it drops to the release point.
            float fraction = Definition.fullReleaseStat != null
                ? Mathf.Clamp01(sheet.Get(Definition.fullReleaseStat))
                : Definition.fullReleaseFraction;
            float releaseAt = Mathf.Min(max * fraction, max - FullTolerance);
            return Current > releaseAt;
        }

        void UpdateFlags()
        {
            bool full = ComputeFull();
            if (full != wasFull)
            {
                wasFull = full;
                SetTag(Definition.tagWhileFull, full);
                FullChanged?.Invoke(this, full);
            }

            bool overdrawn = IsOverdrawn;
            if (overdrawn != wasOverdrawn)
            {
                wasOverdrawn = overdrawn;
                SetTag(Definition.tagWhileOverdrawn, overdrawn);
                OverdrawnChanged?.Invoke(this, overdrawn);
            }
        }

        void SetTag(GameplayTags.GameplayTag tag, bool on)
        {
            if (tag == null || sheet.Tags == null) return;

            if (on) sheet.Tags.Add(tag);
            else sheet.Tags.Remove(tag);
        }
    }
}