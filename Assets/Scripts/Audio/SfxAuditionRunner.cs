using UnityEngine;

/// <summary>
/// Escena de prueba de SFX: recorre solo todas las familias de <see cref="SynthSfxLibrary"/> con su nombre en pantalla,
/// y permite saltar, repetir y comparar tomas. Solo para la build de test (SfxTest); no se usa en el juego.
/// Teclas: Espacio pausa/sigue, flechas Der/Izq cambia de familia, Arr/Abj cambia de grupo, R repite,
/// 1-5 toma puntual, A auto on/off, +/- volumen.
/// </summary>
public class SfxAuditionRunner : MonoBehaviour
{
    private struct Item
    {
        public string Group;
        public string Key;
        public string Label;
        public bool Loop;

        public Item(string group, string key, string label, bool loop = false)
        {
            Group = group;
            Key = key;
            Label = label;
            Loop = loop;
        }
    }

    private static readonly Item[] Items =
    {
        new("Armas", "shot", "Cañón automático: disparo"),
        new("Armas", "rocket_launch", "Cohete: lanzamiento"),
        new("Armas", "rocket_explosion", "Cohete: explosión"),
        new("Armas", "mortar_launch", "Mortero: lanzamiento"),
        new("Armas", "mortar_explosion", "Mortero: explosión"),
        new("Armas", "blade_slash", "Hoja giratoria: tajo"),
        new("Armas", "flame_loop", "Lanzallamas: loop continuo (4 s)", true),
        new("Armas", "enemy_hit", "Impacto de bala / golpe a enemigo"),
        new("Habilidades", "cannon_ability", "Cañón: ráfaga (Q)"),
        new("Habilidades", "rocket_ability", "Cohete: racimo (Q)"),
        new("Habilidades", "mortar_ability", "Mortero: barrage (Q)"),
        new("Habilidades", "blade_thrust", "Hoja: estocada (Q)"),
        new("Habilidades", "blade_dash", "Hoja: dash atómico (Q)"),
        new("Habilidades", "flame_burst", "Lanzallamas: llamarada (Q)"),
        new("Habilidades", "dash", "Dash del jugador"),
        new("Habilidades", "slide", "Slide del jugador"),
        new("Jugador", "jump", "Salto"),
        new("Jugador", "land", "Aterrizaje"),
        new("Jugador", "hurt", "Daño recibido"),
        new("Jugador", "xp", "XP recogida"),
        new("Jugador", "levelup", "Level up"),
        new("Overheat", "overheat_warning", "Warning (80 % de calor)"),
        new("Overheat", "overheat_start", "Overheat: inicio"),
        new("Overheat", "overheat_end", "Overheat: fin"),
        new("Enemigos", "bomber_spawn", "Dron bombardero: spawn"),
        new("Enemigos", "bomber_death", "Dron bombardero: muerte"),
        new("Enemigos", "charger_spawn", "Charger: spawn"),
        new("Enemigos", "charger_death", "Charger: muerte"),
        new("Enemigos", "destroyer_spawn", "Destroyer: spawn"),
        new("Enemigos", "destroyer_death", "Destroyer: muerte"),
        new("Enemigos", "flyer_spawn", "Dron artillero: spawn"),
        new("Enemigos", "flyer_death", "Dron artillero: muerte"),
        new("Enemigos", "worm_spawn", "Gusano gigante: spawn"),
        new("Enemigos", "worm_death", "Gusano gigante: muerte"),
        new("Enemigos", "slime_spawn", "Slime infernal: spawn"),
        new("Enemigos", "slime_death", "Slime infernal: muerte"),
        new("Enemigos", "enemy_death", "Muerte genérica (fallback)"),
    };

    private const float GapBetweenTakes = 0.7f;
    private const float LoopSeconds = 4f;

    [SerializeField] private SynthSfxLibrary _library;

    private AudioSource _source;
    private int _index;
    private int _take;
    private bool _auto = true;
    private float _volume = 0.6f;
    private float _nextAt;
    private float _stopAt = -1f;
    private Vector2 _scroll;
    private GUIStyle _big;
    private GUIStyle _small;
    private GUIStyle _header;

    private void Awake()
    {
        Application.runInBackground = true;
        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.spatialBlend = 0f;
    }

    private void Start() => PlayCurrent();

    private int TakeCount => _library != null && _library.TryGetClips(Items[_index].Key, out AudioClip[] c) ? c.Length : 0;

    private void Update()
    {
        _source.volume = _volume;

        if (_stopAt > 0f && Time.unscaledTime >= _stopAt)
        {
            _source.Stop();
            _stopAt = -1f;
        }

        if (!_auto || Time.unscaledTime < _nextAt)
            return;

        _take++;
        if (_take >= TakeCount)
        {
            _index = (_index + 1) % Items.Length;
            _take = 0;
        }

        PlayCurrent();
    }

    private void PlayCurrent()
    {
        Item item = Items[_index];
        if (_library == null || !_library.TryGetClips(item.Key, out AudioClip[] clips))
        {
            _nextAt = Time.unscaledTime + 0.2f;
            return;
        }

        _take = Mathf.Clamp(_take, 0, clips.Length - 1);
        AudioClip clip = clips[_take];
        _source.Stop();
        _source.loop = item.Loop;
        _source.clip = clip;
        _source.Play();
        Debug.Log($"[SfxAudition] {item.Key} toma {_take + 1}: clip='{clip.name}' {clip.length:0.00}s loadState={clip.loadState} isPlaying={_source.isPlaying} vol={_source.volume:0.00} listenerVol={AudioListener.volume:0.00} paused={AudioListener.pause}");

        if (item.Loop)
        {
            _stopAt = Time.unscaledTime + LoopSeconds;
            _nextAt = _stopAt + GapBetweenTakes;
        }
        else
        {
            _stopAt = -1f;
            _nextAt = Time.unscaledTime + clip.length + GapBetweenTakes;
        }
    }

    private void Select(int index, int take = 0)
    {
        _index = (index % Items.Length + Items.Length) % Items.Length;
        _take = take;
        PlayCurrent();
    }

    private void JumpGroup(int direction)
    {
        string current = Items[_index].Group;
        int i = _index;
        for (int n = 0; n < Items.Length; n++)
        {
            i = (i + direction + Items.Length) % Items.Length;
            if (Items[i].Group != current)
                break;
        }

        // Al ir hacia atrás aterriza en el inicio del grupo anterior.
        if (direction < 0)
        {
            string group = Items[i].Group;
            while (Items[(i - 1 + Items.Length) % Items.Length].Group == group)
                i = (i - 1 + Items.Length) % Items.Length;
        }

        Select(i);
    }

    private void HandleKeys()
    {
        Event e = Event.current;
        if (e.type != EventType.KeyDown)
            return;

        switch (e.keyCode)
        {
            case KeyCode.Space: _auto = !_auto; if (_auto) _nextAt = Time.unscaledTime + 0.3f; break;
            case KeyCode.RightArrow: Select(_index + 1); break;
            case KeyCode.LeftArrow: Select(_index - 1); break;
            case KeyCode.UpArrow: JumpGroup(-1); break;
            case KeyCode.DownArrow: JumpGroup(1); break;
            case KeyCode.R: PlayCurrent(); break;
            case KeyCode.A: _auto = !_auto; break;
            case KeyCode.Equals:
            case KeyCode.KeypadPlus: _volume = Mathf.Clamp01(_volume + 0.1f); break;
            case KeyCode.Minus:
            case KeyCode.KeypadMinus: _volume = Mathf.Clamp01(_volume - 0.1f); break;
            default:
                if (e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha9)
                    Select(_index, e.keyCode - KeyCode.Alpha1);
                break;
        }
    }

    private void OnGUI()
    {
        HandleKeys();

        float scale = Mathf.Max(1f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float width = Screen.width / scale;
        float height = Screen.height / scale;

        _big ??= new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, wordWrap = true };
        _small ??= new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
        _header ??= new GUIStyle(_small) { fontStyle = FontStyle.Bold, fontSize = 16 };

        Item item = Items[_index];
        int takes = TakeCount;

        GUILayout.BeginArea(new Rect(20, 14, width * 0.55f, height - 28));
        GUILayout.Label($"{item.Group.ToUpper()}  ·  {_index + 1}/{Items.Length}", _small);
        GUILayout.Label(item.Label, _big);
        GUILayout.Label($"clave: {item.Key}    toma {_take + 1}/{takes}    {(_auto ? "AUTO" : "PAUSADO")}    volumen {_volume:0.0}", _small);
        GUILayout.Space(10);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("◀ Anterior", GUILayout.Height(34))) Select(_index - 1);
        if (GUILayout.Button("↻ Repetir", GUILayout.Height(34))) PlayCurrent();
        if (GUILayout.Button(_auto ? "❚❚ Pausar auto" : "▶ Seguir auto", GUILayout.Height(34))) _auto = !_auto;
        if (GUILayout.Button("Siguiente ▶", GUILayout.Height(34))) Select(_index + 1);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("Tomas:", _small, GUILayout.Width(50));
        for (int t = 0; t < takes; t++)
        {
            if (GUILayout.Button((t + 1).ToString(), GUILayout.Height(28), GUILayout.Width(40)))
                Select(_index, t);
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(8);
        _volume = GUILayout.HorizontalSlider(_volume, 0f, 1f);
        GUILayout.Space(6);
        AudioConfiguration cfg = AudioSettings.GetConfiguration();
        GUILayout.Label($"diag: sonando={_source.isPlaying}  clip={(_source.clip != null ? _source.clip.name + " (" + _source.clip.loadState + ")" : "null")}  librería={(_library != null ? "ok" : "NULL")}  salida={cfg.sampleRate} Hz / {cfg.speakerMode}  listener={AudioListener.volume:0.0}", _small);
        GUILayout.Space(6);
        GUILayout.Label("Espacio: pausa/sigue   ←/→: sonido   ↑/↓: grupo   R: repetir   1-5: toma   +/-: volumen", _small);
        GUILayout.EndArea();

        GUILayout.BeginArea(new Rect(width * 0.58f, 14, width * 0.40f, height - 28));
        _scroll = GUILayout.BeginScrollView(_scroll);
        string lastGroup = null;
        for (int i = 0; i < Items.Length; i++)
        {
            if (Items[i].Group != lastGroup)
            {
                lastGroup = Items[i].Group;
                GUILayout.Label(lastGroup, _header);
            }

            bool selected = i == _index;
            if (GUILayout.Button((selected ? "▶ " : "   ") + Items[i].Label, GUILayout.Height(24)))
                Select(i);
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
}
