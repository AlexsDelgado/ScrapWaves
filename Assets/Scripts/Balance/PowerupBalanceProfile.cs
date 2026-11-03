using System;
using UnityEngine;

/// <summary>
/// Perillas de balance de los power-ups temporales en un solo asset, para tunearlas sin tocar código y
/// sin perder los cambios al salir de Play Mode. Los valores por defecto son exactamente los que tenía el
/// código antes de existir el asset, así que un perfil recién creado (o sin perfil) no cambia el juego.
/// Lo consumen <see cref="TemporaryPowerupController"/> (efectos), <see cref="EnemyTemporaryPowerupDrop"/> (drop)
/// y <see cref="TemporaryPowerupPickup"/> (recogida). Lo reparte <see cref="PowerupBalanceHub"/>.
/// </summary>
[CreateAssetMenu(fileName = "PowerupBalanceProfile", menuName = "ScrapWaves/Balance/Powerup Balance Profile", order = 1)]
public class PowerupBalanceProfile : ScriptableObject
{
    /// <summary>Valor en el nivel 1 (Min) y en el tope de nivel (Max); entre medio se interpola con el nivel.</summary>
    [Serializable]
    public struct LevelRange
    {
        public float Min;
        public float Max;

        public LevelRange(float min, float max)
        {
            Min = min;
            Max = max;
        }

        public float Evaluate(float t) => Mathf.Lerp(Min, Max, t);
    }

    [Serializable]
    public class DamageBuff
    {
        [Min(0f), Tooltip("Segundos que dura el buff.")] public float Duration = 15f;
        [Tooltip("Multiplicador de daño.")] public LevelRange DamageMultiplier = new(1.5f, 2.5f);
        [Tooltip("Multiplicador de cadencia de ataque.")] public LevelRange AttackSpeedMultiplier = new(1.75f, 3f);
        [Tooltip("Puntos de probabilidad de crítico que suma (50 = +50%).")] public LevelRange CriticalChance = new(50f, 100f);
    }

    [Serializable]
    public class SpeedBuff
    {
        [Min(0f), Tooltip("Segundos que dura el buff.")] public float Duration = 15f;
        [Tooltip("Multiplicador de velocidad de movimiento.")] public LevelRange MovementSpeedMultiplier = new(2f, 4f);
        [Tooltip("Multiplicador de altura de salto.")] public LevelRange JumpHeightMultiplier = new(2f, 3f);
        [Min(0), Tooltip("Dashes extra = dashes actuales x este valor (2 deja los dashes en x3 en total). 0 = no toca los dashes.")]
        public int DashBonusFactor = 2;
        [Min(0), Tooltip("Saltos aéreos extra = saltos aéreos actuales x este valor (2 los deja en x3 en total). 0 = no toca los saltos.")]
        public int AirJumpBonusFactor = 2;
    }

    [Serializable]
    public class ScavengingBuff
    {
        [Min(0f), Tooltip("Segundos que dura el buff.")] public float Duration = 7.5f;
        [Tooltip("Multiplicador del rango de recogida.")] public LevelRange PickupRangeMultiplier = new(2f, 4f);
        [Tooltip("Puntos de Scavenging que suma. También sube la probabilidad de drop de powerups.")] public LevelRange Scavenging = new(25f, 50f);
        [Tooltip("Puntos de probabilidad de doble drop que suma.")] public LevelRange DoubleDrop = new(25f, 75f);
    }

    [Serializable]
    public class InvulnerabilityBuff
    {
        [Min(0f), Tooltip("Segundos de invulnerabilidad.")] public float Duration = 7.5f;
    }

    [Serializable]
    public class HealPowerup
    {
        [Tooltip("Rellena también la munición del arma manual y resetea el cooldown de la habilidad.")] public bool RefillAmmoAndAbility = true;
        [Min(0f), Tooltip("Segundos del efecto visual genérico (solo si no tiene VFX propio).")] public float FxDuration = 1.75f;
    }

    [Serializable]
    public class NukePowerup
    {
        [Tooltip("Daño en el centro.")] public LevelRange Damage = new(50f, 500f);
        [Tooltip("Radio en metros.")] public LevelRange Radius = new(20f, 40f);
        [Range(0f, 1f), Tooltip("Fracción del radio con daño pleno; más allá cae lineal hasta 0 en el borde.")] public float FullDamageRadiusFraction = 0.5f;
        [Min(0f), Tooltip("Fuerza de empuje en el centro (se multiplica por el falloff).")] public float Knockback = 18f;
        [Min(0f), Tooltip("Segundos del efecto visual genérico.")] public float FxDuration = 1.75f;
    }

    [Header("Escalado por nivel")]
    [SerializeField, Range(-1f, 1f), Tooltip("Forzar el factor de nivel (0 = nivel 1, 1 = tope) para probar los extremos sin subir de nivel. Negativo = automático (nivel actual / tope).")]
    private float _forcedLevelT = -1f;

    [Header("Drop al morir un enemigo")]
    [SerializeField, Range(0f, 1f), Tooltip("Probabilidad base con un enemigo común (0.01 = 1%).")]
    private float _normalDropChance = 0.01f;

    [SerializeField, Range(0f, 1f), Tooltip("Probabilidad base con un élite o jefe (0.05 = 5%).")]
    private float _eliteDropChance = 0.05f;

    [SerializeField, Min(1f), Tooltip("Scavenging de referencia: la probabilidad se multiplica por Scavenging / este valor.")]
    private float _referenceScavenging = 50f;

    [SerializeField, Tooltip("Peso de cada tipo al sortear qué powerup cae. Orden: ExtraDamage, ExtraSpeed, ExtraScavenging, Invulnerability, FullHeal, Nuke. 0 = nunca cae.")]
    private float[] _dropWeights = { 1f, 1f, 1f, 1f, 1f, 1f };

    [Header("Recogida")]
    [SerializeField, Min(0.01f), Tooltip("Distancia a la que se recoge al tocarlo.")]
    private float _pickupRadius = 1.5f;

    [SerializeField, Min(0f), Tooltip("Distancia a la que el imán lo empieza a atraer.")]
    private float _magnetRadius = 6f;

    [SerializeField, Min(0f), Tooltip("Velocidad con la que el imán lo atrae.")]
    private float _magnetSpeed = 12f;

    [Header("Efectos")]
    [SerializeField] private DamageBuff _extraDamage = new();
    [SerializeField] private SpeedBuff _extraSpeed = new();
    [SerializeField] private ScavengingBuff _extraScavenging = new();
    [SerializeField] private InvulnerabilityBuff _invulnerability = new();
    [SerializeField] private HealPowerup _fullHeal = new();
    [SerializeField] private NukePowerup _nuke = new();

    private static PowerupBalanceProfile s_Active;
    private static PowerupBalanceProfile s_Fallback;

    /// <summary>El perfil en uso: el que reparte el hub, o uno con los valores originales si no hay hub.</summary>
    public static PowerupBalanceProfile Current
    {
        get
        {
            if (s_Active != null)
                return s_Active;

            if (s_Fallback == null)
            {
                s_Fallback = CreateInstance<PowerupBalanceProfile>();
                s_Fallback.hideFlags = HideFlags.HideAndDontSave;
            }

            return s_Fallback;
        }
    }

    public static void SetActive(PowerupBalanceProfile profile) => s_Active = profile;

    public static void ClearActive(PowerupBalanceProfile profile)
    {
        if (s_Active == profile)
            s_Active = null;
    }

    /// <summary>Negativo = automático.</summary>
    public float ForcedLevelT => _forcedLevelT;
    public float NormalDropChance => _normalDropChance;
    public float EliteDropChance => _eliteDropChance;
    public float ReferenceScavenging => _referenceScavenging;
    public float PickupRadius => _pickupRadius;
    public float MagnetRadius => _magnetRadius;
    public float MagnetSpeed => _magnetSpeed;
    public DamageBuff ExtraDamage => _extraDamage;
    public SpeedBuff ExtraSpeed => _extraSpeed;
    public ScavengingBuff ExtraScavenging => _extraScavenging;
    public InvulnerabilityBuff Invulnerability => _invulnerability;
    public HealPowerup FullHeal => _fullHeal;
    public NukePowerup Nuke => _nuke;

    /// <summary>Factor de nivel 0–1: el forzado si hay uno, si no el que calcula el controlador.</summary>
    public float ResolveLevelT(float automaticT) => _forcedLevelT >= 0f ? Mathf.Clamp01(_forcedLevelT) : automaticT;

    /// <summary>Sortea un tipo según los pesos. Si todos son 0 cae en uniforme, como antes.</summary>
    public TemporaryPowerupType RollDropType()
    {
        int count = Enum.GetValues(typeof(TemporaryPowerupType)).Length;
        float total = 0f;
        for (int i = 0; i < count; i++)
            total += WeightOf(i);

        if (total <= 0f)
            return (TemporaryPowerupType)UnityEngine.Random.Range(0, count);

        float roll = UnityEngine.Random.value * total;
        for (int i = 0; i < count; i++)
        {
            roll -= WeightOf(i);
            if (roll <= 0f && WeightOf(i) > 0f)
                return (TemporaryPowerupType)i;
        }

        return (TemporaryPowerupType)(count - 1);
    }

    /// <summary>Probabilidad real de drop con un Scavenging dado, ya acotada a 0–1.</summary>
    public float DropChance(bool eliteOrBoss, float scavenging)
    {
        float baseChance = eliteOrBoss ? _eliteDropChance : _normalDropChance;
        return Mathf.Clamp01(baseChance * (scavenging / Mathf.Max(0.0001f, _referenceScavenging)));
    }

    private float WeightOf(int index) => _dropWeights != null && index < _dropWeights.Length ? Mathf.Max(0f, _dropWeights[index]) : 1f;

    private void OnValidate()
    {
        int count = Enum.GetValues(typeof(TemporaryPowerupType)).Length;
        if (_dropWeights == null || _dropWeights.Length != count)
        {
            float[] resized = new float[count];
            for (int i = 0; i < count; i++)
                resized[i] = _dropWeights != null && i < _dropWeights.Length ? _dropWeights[i] : 1f;
            _dropWeights = resized;
        }
    }
}
