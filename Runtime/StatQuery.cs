using System.Collections.Generic;
using GameplayTags;

namespace CharacterStats
{
    /// <summary>
    /// The situation a stat is being asked about, so that conditional modifiers can decide whether they apply:
    /// what the action is (its tags: Fire, Movement...) and who else is involved (the target of an attack, the attacker).
    /// A plain stat read uses an empty query.
    /// </summary>
    public readonly struct StatQuery
    {
        /// <summary>Tags describing the action. Can be null.</summary>
        public readonly IReadOnlyList<GameplayTag> Tags;

        /// <summary>The other party, if there is one. Can be null.</summary>
        public readonly StatSheet Other;

        public StatQuery(IReadOnlyList<GameplayTag> tags, StatSheet other = null)
        {
            Tags = tags;
            Other = other;
        }

        /// <summary>True if any of the query's tags is <paramref name="tag"/> or a descendant of it.</summary>
        public bool HasTag(GameplayTag tag)
        {
            if (Tags == null || tag == null) return false;

            for (int i = 0; i < Tags.Count; i++)
            {
                if (Tags[i] != null && Tags[i].Is(tag)) return true;
            }

            return false;
        }
    }
}
