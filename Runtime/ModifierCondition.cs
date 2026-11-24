using System;
using GameplayTags;
using UnityEngine;

namespace CharacterStats
{
    /// <summary>
    /// One thing that must be true for a modifier to apply: a tag that someone or something has (or lacks).
    /// A modifier with several conditions needs all of them.
    /// </summary>
    [Serializable]
    public class ModifierCondition
    {
        public enum Subject
        {
            /// <summary>The action being asked about (a Fire attack, a Movement cost).</summary>
            Action,

            /// <summary>Whoever owns the stat.</summary>
            Owner,

            /// <summary>The other party (the target when attacking, the attacker when defending).</summary>
            Other,
        }

        public Subject subject = Subject.Action;
        public GameplayTag tag;

        [Tooltip("On: the condition is met when the tag is absent.")]
        public bool not;

        public bool Met(StatSheet owner, in StatQuery query)
        {
            bool has;
            switch (subject)
            {
                case Subject.Owner:
                    has = owner.Tags != null && owner.Tags.Has(tag);
                    break;
                case Subject.Other:
                    has = query.Other != null && query.Other.Tags != null && query.Other.Tags.Has(tag);
                    break;
                default:
                    has = query.HasTag(tag);
                    break;
            }

            return has != not;
        }
    }
}
