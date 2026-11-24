using CharacterStats;
using GameplayTags;
using UnityEngine;

namespace CharacterStats.Samples
{
    /// <summary>
    /// Stamina and Exhaustion sample. Add it to an empty GameObject and press Play: it adds a StatSheet set up from
    /// the Sample Stats profile (and a TagHolder and the debug panel), and draws buttons to spend stamina, take damage
    /// and put a ring on. Spending stamina below zero turns into exhaustion, which eats the stamina bar; exhaustion
    /// past its cap blocks all stamina costs and hurts.
    /// </summary>
    [AddComponentMenu("Character Stats/Samples/Stats Sample Controller")]
    public class StatsSampleController : MonoBehaviour
    {
        [SerializeField] StatProfile profile;
        [SerializeField] PoolDefinition stamina;
        [SerializeField] PoolDefinition health;

        [Tooltip("The stat the ring raises.")]
        [SerializeField] StatDefinition staminaRegen;

        [SerializeField] float spend = 30f;
        [SerializeField] float damage = 15f;

        StatSheet sheet;
        bool ringOn;

        // The ring's modifier. Its source is this component, so RemoveModifier(ring, this) takes it off again.
        readonly StatModifierSpec ring = new StatModifierSpec { op = ModifierOp.PercentAdd, value = 1f };

        void Awake()
        {
            TagHolder.On(this);
            if (!TryGetComponent(out sheet)) sheet = gameObject.AddComponent<StatSheet>();
            if (sheet.Profile == null) sheet.Setup(profile);
            if (GetComponent<StatsDebugPanel>() == null) gameObject.AddComponent<StatsDebugPanel>();
            ring.stat = staminaRegen;
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10f, Screen.height - 140f, 260f, 130f), GUI.skin.box);

            if (sheet.TryGetPool(stamina, out ResourcePool staminaPool))
            {
                float price = staminaPool.PriceOf(spend);
                GUI.enabled = staminaPool.CanPay(price);
                if (GUILayout.Button($"Spend {price:0} stamina")) staminaPool.Spend(price, true, "Button");
                GUI.enabled = true;
            }

            if (sheet.TryGetPool(health, out ResourcePool healthPool))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button($"Take {damage:0} damage")) healthPool.Spend(damage, false, "Button");
                if (GUILayout.Button("Heal")) healthPool.Restore(healthPool.Max, "Button");
                GUILayout.EndHorizontal();
            }

            bool wear = GUILayout.Toggle(ringOn, " Wear ring (+100% stamina regen)");
            if (wear != ringOn)
            {
                ringOn = wear;
                if (ringOn) sheet.AddModifier(ring, this);
                else sheet.RemoveModifier(ring, this);
            }

            GUILayout.EndArea();
        }
    }
}
