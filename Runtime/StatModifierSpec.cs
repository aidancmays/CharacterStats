using System;
using System.Collections.Generic;
using UnityEngine;

namespace CharacterStats
{
    public enum ModifierOp
    {
        /// <summary>Added to the base value.</summary>
        Flat,

        /// <summary>A fraction (0.2 = +20%). All of these are summed, then applied once.</summary>
        PercentAdd,

        /// <summary>A fraction (0.2 = x1.2, -0.2 = x0.8). Each one multiplies on its own.</summary>
        PercentMultiply,

        /// <summary>Replaces the result. With several, the highest Priority wins.</summary>
        Override,
    }

    /// <summary>
    /// A change to a stat: what it is, not who applied it. It is tuning data, so it lives on whatever grants it
    /// (an ability module, an item, an effect, a profile) and is handed to StatSheet.AddModifier with a source.
    ///
    /// Final value = (base + Flat) x (1 + sum of PercentAdd) x each (1 + PercentMultiply), then Override, then the stat's limits.
    /// </summary>
    [Serializable]
    public class StatModifierSpec
    {
        [Tooltip("The stat changed. Also applies to every stat that inherits from it.")]
        public StatDefinition stat;

        public ModifierOp op = ModifierOp.Flat;

        public float value;

        [Tooltip("Optional. The value is multiplied by this stat of the owner. " +
                 "E.g. Max Stamina, Flat, value 10, scaled by Endurance = +10 max stamina per point of endurance.")]
        public StatDefinition scaledBy;

        [Tooltip("Override only: with several overrides the highest priority wins.")]
        public int priority;

        [Tooltip("All must be met for the modifier to apply. Empty = always applies.")]
        public List<ModifierCondition> conditions = new List<ModifierCondition>();

        public bool ConditionsMet(StatSheet owner, in StatQuery query)
        {
            for (int i = 0; i < conditions.Count; i++)
            {
                if (!conditions[i].Met(owner, query)) return false;
            }

            return true;
        }
    }
}
