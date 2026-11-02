using System;
using UnityEngine;

public enum TutorialStep { Welcome, Movement, FirstEnemy, FirstPickup, Upgrade, Heat, Ability, HordePickup, Tinker, AutomaticCombat, FinalPickup, ItemChoice, Escape, Vent, Complete }
public enum TutorialControls { None, Movement, Fire, Interact, Ability }

[Serializable]
public class TutorialPage
{
    public TutorialStep Step;
    public string Title;
    [TextArea(3, 8)] public string Text;
    public TutorialControls Controls;
    public bool AdvanceWithE;
}

[CreateAssetMenu(menuName = "ScrapWaves/Tutorial/Definition")]
public sealed class TutorialDefinition : ScriptableObject
{
    public WeaponData StartingWeapon;
    [Tooltip("0 uses the live crafting price. Positive overrides must cover that price.")]
    [Min(0)] public int FirstUpgradePipes;
    [Tooltip("0 uses the live slot-2 price. Positive overrides must cover that price.")]
    [Min(0)] public int TinkerCommonEach;
    [Min(0.5f)] public float MarkerRadius = 2.5f;
    [Min(10f)] public float RecoveryRadius = 48f;
    public TutorialPage[] Pages = Array.Empty<TutorialPage>();

    public TutorialPage Page(TutorialStep step) => Array.Find(Pages, p => p != null && p.Step == step);
    public static int GrantAmount(int price, int designerOverride) => Mathf.Max(price, designerOverride);
}
