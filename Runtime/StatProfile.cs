using System;
using System.Collections.Generic;
using UnityEngine;

namespace CharacterStats
{
    /// <summary>
    /// What a kind of character starts with: base values, pools, and the modifiers that are simply part of it
    /// (the rules deriving one stat from another, such as max stamina from endurance).
    /// Shared asset: one per archetype (the player, a wolf, a bandit).
    /// </summary>
    [CreateAssetMenu(menuName = "Character Stats/Profile")]
    public class StatProfile : ScriptableObject
    {
        [Serializable]
        public struct BaseValue
        {
            public StatDefinition stat;
            public float value;
        }

        [Tooltip("Stats not listed use their own default base.")]
        public List<BaseValue> baseValues = new List<BaseValue>();

        public List<PoolDefinition> pools = new List<PoolDefinition>();

        [Tooltip("Always applied. Use Scaled By to derive one stat from another.")]
        public List<StatModifierSpec> innateModifiers = new List<StatModifierSpec>();
    }
}
