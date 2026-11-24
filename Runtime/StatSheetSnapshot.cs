using System;
using System.Collections.Generic;

namespace CharacterStats
{
    /// <summary>
    /// What a StatSheet needs saved: base values and pool amounts, by id. Plain data, ready for JsonUtility
    /// or whatever the save system uses. Modifiers are not saved: whatever granted them (gear, effects, abilities)
    /// grants them again after loading.
    /// </summary>
    [Serializable]
    public class StatSheetSnapshot
    {
        [Serializable]
        public struct Entry
        {
            public string id;
            public float value;
        }

        public List<Entry> baseValues = new List<Entry>();
        public List<Entry> pools = new List<Entry>();
    }
}
