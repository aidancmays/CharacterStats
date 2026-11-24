using UnityEngine;

namespace CharacterStats
{
    /// <summary>
    /// One kind of number a character can have: Endurance, Max Stamina, Stamina Regen, Fire Resistance...
    /// New stats are new assets.
    ///
    /// Inherits From groups stats: a modifier on the parent applies to every stat under it. So with
    /// Fire Resistance -> Magic Resistance -> Resistance, "+10% magic resistance" is one modifier on Magic Resistance.
    /// Only modifiers are inherited; each stat has its own base value.
    /// </summary>
    [CreateAssetMenu(menuName = "Character Stats/Stat")]
    public class StatDefinition : ScriptableObject
    {
        [Tooltip("Used in save data. Set it once and never change it. Empty = the asset's name (so then never rename the asset).")]
        [SerializeField] string id;

        public string displayName;

        [Tooltip("Optional. Modifiers on that stat (and on its own parents) also apply to this one.")]
        [SerializeField] StatDefinition inheritsFrom;

        [Tooltip("Base value for anything whose profile does not list this stat.")]
        public float defaultBase;

        [Header("Limits on the final value")]
        public bool hasMin;
        public float min;
        public bool hasMax;
        public float max;

        public string Id => string.IsNullOrEmpty(id) ? name : id;
        public StatDefinition InheritsFrom => inheritsFrom;

        public float Clamp(float value)
        {
            if (hasMin && value < min) value = min;
            if (hasMax && value > max) value = max;
            return value;
        }
    }
}
