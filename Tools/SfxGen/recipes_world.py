"""Recetas de armas, habilidades, enemigos y avisos. Mismo enfoque que recipes.py:
fuentes físicas en capas + reverb metálico corto + lo-fi leve (12 bits).
"""
import numpy as np
from synth import (SR, at, bandpass, crackle, debris, env_exp, highpass, lerp_curve, loopify, lofi, lowpass,
                   lp_sweep, mix, modal, noise, osc, pink, reverb, smooth_noise, soft_clip, transient)


def boom(n, f0, f1, decay, power=3):
    return osc(lerp_curve(n, f0, f1, power), "sine") * env_exp(n, decay, 0.001)


# ---------------------------------------------------------------- bloques reutilizables

def whoosh(n, rng, f0, f1, peak=0.3, power=1.0):
    """Aire desplazado: ruido con corte que barre y envolvente en campana (pico en `peak` de la duración)."""
    x = highpass(lp_sweep(noise(n, rng), f0, f1, power), 120)
    t = np.linspace(0, 1, n)
    return x * np.sin(np.pi * t ** (np.log(0.5) / np.log(max(peak, 0.05)))) ** 1.5


def pop(dur, f0, f1, decay=0.03):
    """Burbuja que revienta: seno con tono ascendente muy corto."""
    n = int(dur * SR)
    return osc(lerp_curve(n, f0, f1, 1.0), "sine") * env_exp(n, decay, 0.0005)


def growl(n, rng, f=60, rate=11.0, cutoff=700):
    """Gruñido grave: sierra + ruido modulados en amplitud y filtrados."""
    t = np.arange(n) / SR
    am = 0.6 + 0.4 * np.sin(2 * np.pi * rate * t + rng.uniform(0, 6.28))
    return lowpass(osc(np.full(n, f), "saw") * 0.7 + noise(n, rng) * 0.4, cutoff) * am


def servo(n, f0, f1, cutoff=1400):
    """Motor eléctrico / hidráulica: sierra filtrada de tono variable."""
    return lowpass(osc(lerp_curve(n, f0, f1, 1.0), "saw"), cutoff)


def zap(n, rng, decay=0.12):
    """Descarga eléctrica: ruido agudo modulado a frecuencia de red."""
    t = np.arange(n) / SR
    return highpass(noise(n, rng), 2500) * (0.5 + 0.5 * np.sign(np.sin(2 * np.pi * 120 * t))) * env_exp(n, decay, 0.0003)


def splat(n, rng, decay=0.12):
    """Impacto húmedo: ruido pasa-banda grave con caída rápida."""
    return bandpass(noise(n, rng), 150, 1400) * env_exp(n, decay, 0.0008)


def explosion(rng, dur, f_body, decay, debris_n, t60, wet=0.3):
    """Explosión parametrizable: presión, bocanada, cuerpo grave, escombros y cola de sala."""
    n = int(dur * SR)
    blast = lp_sweep(noise(n, rng), 9000, 250, 1.6) * env_exp(n, decay * 0.7, 0.0005)
    body = boom(n, f_body, f_body * 0.3, decay)
    rumble = lowpass(noise(n, rng), 180) * env_exp(n, decay * 1.6, 0.01) * 0.6
    x = soft_clip(mix(blast * 0.9, body * 1.1, rumble, transient(n, rng, 0.005, 1200) * 0.7, n=n), 1.7)
    return reverb(x, rng, t60, wet)


def finish_fx(x, rng, t60, wet):
    return lofi(reverb(x, rng, t60, wet), 12)


# ---------------------------------------------------------------- armas

def rocket_launch(rng, v):
    """Salida del cohete: un estampido seco, casi una explosión chica, con un siseo corto. Nada de chorro largo."""
    n = int(0.55 * SR)
    kick = boom(n, 150 * (1 + 0.08 * v["a"]), 42, 0.12) * 0.8
    burst = lp_sweep(noise(n, rng), 3600, 300, 1.8) * env_exp(n, 0.15, 0.0004) * 1.8
    hiss = bandpass(noise(n, rng), 1500, 5000) * env_exp(n, 0.1, 0.003) * 0.25
    x = soft_clip(mix(kick, burst * 1.3, hiss, lowpass(transient(n, rng, 0.01, 1200), 6500) * 1.0, n=n), 2.4)
    return lofi(reverb(x, rng, 0.22, 0.14)[:n], 12)


def rocket_explosion(rng, v):
    return lofi(explosion(rng, 1.4, 70 * (1 + 0.1 * v["a"]), 0.35, 14, 0.7, 0.3), 12)


def mortar_launch(rng, v):
    """Tubo que escupe el proyectil: 'thoomp' hueco con resonancia del cañón."""
    n = int(0.7 * SR)
    thump = boom(n, 135 * (1 + 0.08 * v["a"]), 48, 0.16)
    tube = modal(170, 0.6, rng, 0.25, ratios=(1.0, 2.0, 3.01, 4.02), spread=0.005) * 0.3
    puff = lp_sweep(noise(n, rng), 3000, 300, 1.5) * env_exp(n, 0.12, 0.001) * 0.8
    x = soft_clip(mix(thump * 1.1, tube, puff, transient(n, rng, 0.004, 900) * 0.5, n=n), 1.6)
    return finish_fx(x, rng, 0.45, 0.22)


def mortar_explosion(rng, v):
    """Más grave y lento que el cohete: se siente en el suelo."""
    return lofi(explosion(rng, 1.8, 48 * (1 + 0.08 * v["a"]), 0.55, 10, 0.9, 0.35), 12)


def blade_slash(rng, v):
    """Tajo: un corte de aire corto y un destello metálico breve. Termina antes de que acabe el golpe."""
    n = int(0.22 * SR)
    air = whoosh(n, rng, 1400, 6500, 0.38) * 0.95
    shing = at(modal(2600 * (1 + 0.08 * v["a"]), 0.12, rng, 0.05, ratios=(1.0, 1.9, 3.3), spread=0.02) * 0.1, 0.04, n)
    x = mix(air, shing, n=n)
    x[-int(0.03 * SR):] *= np.linspace(1, 0, int(0.03 * SR))
    return lofi(x, 12)


def flame_loop(rng, v):
    """Lanzallamas continuo sin costura: rugido de fuego (ruido rosa grave con oleadas) y chisporroteo de leña.

    Todo el contenido está por debajo de ~5 kHz y sin tonos: sin siseo agudo ni sonidos metálicos.
    """
    n = int(2.6 * SR)
    swell = 0.55 + 0.45 * smooth_noise(n, rng, 3.5)
    flutter = 0.88 + 0.12 * smooth_noise(n, rng, 14)
    body = bandpass(pink(n, rng), 120, 2400) * swell
    mid = bandpass(pink(n, rng), 400, 3200) * (0.5 + 0.5 * smooth_noise(n, rng, 5)) * 0.8
    low = lowpass(noise(n, rng), 160) * (0.6 + 0.4 * smooth_noise(n, rng, 2)) * 0.15
    base = (body + mid + low) * flutter
    cap = 2.2 * float(np.sqrt(np.mean(base ** 2)))
    pops = np.clip(crackle(n, rng, 13, 0.55), -cap, cap)
    x = base + pops
    x = lowpass(soft_clip(x, 1.6), 5200)
    return lofi(loopify(x, 0.25), 12)


def flame_burst(rng, v):
    """Llamarada de la habilidad: ignición grave y un rugido de fuego denso que se apaga."""
    n = int(1.1 * SR)
    whoomp = boom(n, 85, 36, 0.2) * 0.25
    roar = bandpass(pink(n, rng), 120, 2600) * (np.minimum(np.arange(n) / SR / 0.04, 1) * env_exp(n, 0.7, 0.0)) * 5.0
    mid = bandpass(pink(n, rng), 400, 3200) * env_exp(n, 0.5, 0.01) * 3.0
    pops = crackle(n, rng, 16, 0.4) * env_exp(n, 0.9, 0.0)
    x = lowpass(soft_clip(mix(whoomp, roar * 1.1, mid, pops, n=n), 1.7), 5200)
    return finish_fx(x, rng, 0.35, 0.14)


# ---------------------------------------------------------------- habilidades

def cannon_ability(rng, v):
    """Habilidad del autocannon: UN disparo, más grande y más fuerte que el normal (no una ráfaga)."""
    n = int(0.75 * SR)
    crack = lowpass(transient(n, rng, 0.014, 1300), 6500) * 1.6
    blast = lp_sweep(noise(n, rng), 3800, 220, 1.7) * env_exp(n, 0.24, 0.0004) * 3.4
    thump = boom(n, 150 * (1 + 0.06 * v["a"]), 34, 0.26) * 0.9
    sub = osc(lerp_curve(n, 62, 30, 2), "sine") * env_exp(n, 0.2, 0.001) * 0.45
    ring = modal(430 * (1 + 0.06 * v["b"]), 0.5, rng, 0.22, ratios=(1.0, 2.4, 4.3)) * 0.16
    x = soft_clip(mix(crack, blast, thump, sub, ring, n=n), 3.0)
    return lofi(reverb(x, rng, 0.35, 0.2)[:n], 12)


def rocket_ability(rng, v):
    """Racimo: tres salidas escalonadas."""
    n = int(1.3 * SR)
    m = int(0.7 * SR)
    out = np.zeros(n)
    for k in range(3):
        s = mix(boom(m, 120, 45, 0.09), whoosh(m, rng, 800, 5500, 0.2) * 0.8, transient(m, rng, 0.004, 1500) * 0.5, n=m)
        out += at(s, 0.16 * k, n)
    return finish_fx(soft_clip(out, 1.5), rng, 0.4, 0.2)


def mortar_ability(rng, v):
    """Barrage: cuatro 'thoomp' seguidos, cada uno más grave."""
    n = int(1.5 * SR)
    m = int(0.5 * SR)
    out = np.zeros(n)
    for k in range(4):
        s = mix(boom(m, 150 - 12 * k, 52 - 4 * k, 0.15),
                lp_sweep(noise(m, rng), 2800, 300, 1.5) * env_exp(m, 0.1, 0.001) * 0.7,
                modal(170 - 6 * k, 0.4, rng, 0.2, ratios=(1.0, 2.0, 3.0), spread=0.005)[:m] * 0.25, n=m)
        out += at(s, 0.22 * k, n)
    return finish_fx(soft_clip(out, 1.5), rng, 0.5, 0.22)


def blade_thrust(rng, v):
    """Estocada: aire rápido, golpe seco y un destello corto."""
    n = int(0.26 * SR)
    air = whoosh(n, rng, 2500, 7000, 0.3) * 0.8
    hit = boom(n, 150, 60, 0.06) * 0.7
    shing = at(modal(3100, 0.14, rng, 0.05, ratios=(1.0, 2.1, 3.7), spread=0.02) * 0.12, 0.07, n)
    x = mix(air, hit, shing, n=n)
    x[-int(0.03 * SR):] *= np.linspace(1, 0, int(0.03 * SR))
    return lofi(x, 12)


def blade_dash(rng, v):
    """Dash atómico: barrido de aire, raspado de metal y un golpe corto al final."""
    n = int(0.5 * SR)
    m = int(0.2 * SR)
    air = whoosh(n, rng, 500, 6000, 0.5) * 0.9
    scrape = bandpass(noise(n, rng), 1500, 6000) * np.minimum(np.arange(n) / SR / 0.04, 1) * env_exp(n, 0.15, 0.0) * 0.25
    end = at(mix(boom(m, 110, 45, 0.08), modal(900, 0.15, rng, 0.06)[:m] * 0.15, n=m), 0.28, n)
    x = mix(air, scrape, end, n=n)
    x[-int(0.03 * SR):] *= np.linspace(1, 0, int(0.03 * SR))
    return lofi(x, 12)


def dash(rng, v):
    """Dash del jugador: ráfaga de propulsión y empuje del cuerpo."""
    n = int(0.45 * SR)
    burst = whoosh(n, rng, 500, 4500, 0.18) * 0.9
    push = boom(n, 100, 50, 0.1, 2) * 0.7
    return finish_fx(mix(burst, push, n=n), rng, 0.2, 0.1)


def slide(rng, v):
    """Derrape sobre chatarra: raspado granulado con piedritas."""
    n = int(0.8 * SR)
    t = np.arange(n) / SR
    grit = bandpass(noise(n, rng), 300, 3500) * (np.minimum(t / 0.04, 1) * np.exp(-6.9 * t / 0.5))
    grit *= 0.7 + 0.3 * np.sign(np.sin(2 * np.pi * (60 + 20 * v["a"]) * t))
    low = lowpass(noise(n, rng), 250) * env_exp(n, 0.3, 0.01) * 0.6
    return finish_fx(mix(grit * 0.7, low, n=n), rng, 0.25, 0.1)


def overheat_warning(rng, v):
    """Calor al límite: presión de vapor que sube, metal que cruje al dilatarse (tics cada vez más seguidos) y retumbo.

    Pensado para leerse como "algo se está recalentando", sin bocina ni tono musical.
    """
    n = int(1.7 * SR)
    t = np.arange(n) / SR
    rise = np.clip(t / 1.4, 0, 1) ** 1.6
    steam = bandpass(noise(n, rng), 1500, 6500) * rise * 0.28
    rumble = bandpass(pink(n, rng), 60, 700) * (0.25 + 0.75 * rise) * 1.3
    ticks = np.zeros(n)
    at_t = 0.05
    gap = 0.2
    while at_t < 1.5:
        tick = modal(rng.uniform(900, 1700), 0.07, rng, 0.03, ratios=(1.0, 2.2), spread=0.04) * (0.25 + 0.5 * rise[int(at_t * SR)])
        ticks += at(tick, at_t, n)
        gap = max(0.04, gap * 0.82)
        at_t += gap
    creak = lowpass(osc(lerp_curve(n, 90, 140, 1.0), "saw"), 500) * rise * 0.25
    x = soft_clip(mix(steam, rumble, ticks, creak, n=n), 1.8)
    x[-int(0.08 * SR):] *= np.linspace(1, 0, int(0.08 * SR))
    return lofi(x, 12)


def overheat_alarm(rng, v):
    """Sirena de ataque aéreo / alerta nuclear: lamento largo que sube y baja sobre un retumbo y fuego.

    Un ciclo completo (~3,6 s) pensado para repetirse seguido mientras dure el peligro. La subida es rápida
    y la bajada lenta (inercia del rotor); el eco grande se calcula sobre dos ciclos para que el empalme no se note.
    """
    cycle = 3.6
    n = int(cycle * SR)
    t = np.arange(n) / SR
    rise = 1.9
    up = np.clip(t / rise, 0, 1)
    down = np.clip((t - rise) / (cycle - rise), 0, 1)
    up = up * up * (3 - 2 * up)
    down = down * down * (3 - 2 * down)
    lo, hi = 360 * (1 + 0.04 * v["a"]), 1020 * (1 + 0.04 * v["a"])
    f = np.where(t < rise, lo + (hi - lo) * up, hi - (hi - lo) * down)

    def horn(freq):
        w = osc(freq, "saw") + 0.7 * osc(freq * 2, "saw") + 0.5 * osc(freq * 3, "saw") + 0.3 * osc(freq * 5, "saw")
        return lowpass(w, 3400)

    voice = horn(f) + 0.8 * horn(f * 1.006)
    voice *= 0.85 + 0.15 * np.sin(2 * np.pi * 13 * t)
    sub_t = 0.5 + 0.5 * np.sin(2 * np.pi * t / 0.9 - 1.0)
    sub = osc(np.full(n, 50.0), "sine") * (0.5 + 0.5 * sub_t) * 0.5
    fire = lowpass(noise(n, rng), 220) * (0.55 + 0.45 * np.sin(2 * np.pi * t / cycle * 2)) * 0.5
    dry = soft_clip(voice * 0.55 + sub + fire, 2.0)
    edge = int(0.03 * SR)
    dry[:edge] *= np.linspace(0, 1, edge)
    dry[-edge:] *= np.linspace(1, 0, edge)
    wet = reverb(np.concatenate([dry, dry]), rng, 1.1, 0.3, 3000)
    return lofi(wet[n:2 * n], 12)


# ---------------------------------------------------------------- enemigos

def bomber_spawn(rng, v):
    """Dron bombardero: hélices que se acercan y un zumbido eléctrico que sube."""
    n = int(1.0 * SR)
    t = np.arange(n) / SR
    rotors = lowpass(osc(np.full(n, 95), "saw"), 900) * (0.6 + 0.4 * np.sign(np.sin(2 * np.pi * 38 * t))) * np.minimum(t / 0.5, 1)
    whine = lowpass(osc(lerp_curve(n, 700, 1300, 1.0), "saw"), 2500) * np.minimum(t / 0.6, 1) * 0.25
    air = highpass(noise(n, rng), 1500) * np.minimum(t / 0.5, 1) * 0.15
    return finish_fx(mix(rotors * 0.5, whine, air, n=n) * env_exp(n, 2.0, 0.0), rng, 0.3, 0.15)


def bomber_death(rng, v):
    """Dron derribado: lamento que cae y explosión chica."""
    n = int(1.5 * SR)
    m = int(0.6 * SR)
    fall = servo(m, 1100, 160, 2500) * env_exp(m, 0.5, 0.005) * 0.4
    blast = at(explosion(rng, 0.9, 85, 0.22, 8, 0.45, 0.25), 0.55, n)
    return lofi(mix(fall, blast, n=n), 12)


def charger_spawn(rng, v):
    """Embestidor: resoplido grave y dos pisadas pesadas."""
    n = int(1.1 * SR)
    m = int(0.3 * SR)
    snort = growl(n, rng, 55, 9, 600) * env_exp(n, 0.7, 0.05) * 0.8
    stomps = np.zeros(n)
    for k in range(2):
        stomps += at(mix(boom(m, 80, 30, 0.12), lowpass(noise(m, rng), 900) * env_exp(m, 0.05, 0.001) * 0.6, n=m), 0.35 + 0.28 * k, n)
    return finish_fx(soft_clip(mix(snort, stomps, n=n), 1.5), rng, 0.4, 0.2)


def charger_death(rng, v):
    """Bestia que se desploma: quejido que baja y golpe contra el suelo."""
    n = int(1.4 * SR)
    g = int(0.9 * SR)
    f = int(0.5 * SR)
    groan = growl(g, rng, 70, 7, 550) * env_exp(g, 0.5, 0.02) * np.linspace(1, 0.5, g)
    fall = at(mix(boom(f, 70, 28, 0.25), lp_sweep(noise(f, rng), 2500, 200, 1.5) * env_exp(f, 0.15, 0.001), n=f), 0.5, n)
    return finish_fx(soft_clip(mix(groan * 0.8, fall, n=n), 1.5), rng, 0.45, 0.2)


def destroyer_spawn(rng, v):
    """Mecha pesado: servos que arrancan, clang de acople y vapor hidráulico."""
    n = int(1.9 * SR)
    t = np.arange(n) / SR
    k = int(1.0 * SR)
    motor = servo(n, 70, 210, 1000) * np.minimum(t / 0.8, 1) * env_exp(n, 1.8, 0.0) * 0.45
    clang = at(mix(modal(150, 1.0, rng, 0.5, spread=0.03) * 0.5, boom(k, 70, 30, 0.2) * 0.8, n=k), 0.9, n)
    steam = at(highpass(noise(n, rng), 2500) * env_exp(n, 0.5, 0.01) * 0.25, 0.9, n)
    return finish_fx(soft_clip(mix(motor, clang, steam, n=n), 1.5), rng, 0.6, 0.25)


def destroyer_death(rng, v):
    """Mecha caído: chasquidos eléctricos, derrumbe de chapa y motor agonizante."""
    n = int(2.2 * SR)
    sparks = np.zeros(n)
    for _ in range(5):
        sparks += at(zap(int(0.25 * SR), rng, 0.1) * rng.uniform(0.3, 0.6), rng.uniform(0.0, 0.8), n)
    dying = servo(n, 180, 35, 900) * env_exp(n, 0.9, 0.01) * 0.4
    collapse = at(explosion(rng, 1.4, 60, 0.4, 16, 0.8, 0.3), 0.5, n)
    return lofi(mix(sparks * 0.5, dying, collapse, n=n), 12)


def flyer_spawn(rng, v):
    """Dron artillero: propulsión de aire corta y chirrido de servo."""
    n = int(0.6 * SR)
    m = int(0.25 * SR)
    jet = whoosh(n, rng, 1500, 5000, 0.4) * 0.6
    chirp = servo(m, 500, 1400, 3000) * env_exp(m, 0.15, 0.005) * 0.25
    return finish_fx(mix(jet, at(chirp, 0.2, n), n=n), rng, 0.3, 0.15)


def flyer_death(rng, v):
    """Cortocircuito y caída: chispazo, motor que se apaga y golpe de chatarra."""
    n = int(1.0 * SR)
    m = int(0.5 * SR)
    c = int(0.4 * SR)
    short = zap(n, rng, 0.2) * 0.6
    fall = servo(m, 1000, 140, 2500) * env_exp(m, 0.3, 0.005) * 0.3
    crash = at(mix(boom(c, 130, 50, 0.1), modal(420, 0.35, rng, 0.15)[:c] * 0.3, n=c), 0.45, n)
    return finish_fx(mix(short, fall, crash, n=n), rng, 0.3, 0.15)


def worm_spawn(rng, v):
    """Gusano gigante emergiendo: temblor, tierra que revienta y rugido húmedo."""
    n = int(2.3 * SR)
    t = np.arange(n) / SR
    k = int(1.0 * SR)
    quake = lowpass(noise(n, rng), 90) * np.minimum(t / 0.8, 1) * env_exp(n, 1.8, 0.0) * 1.2 * (0.7 + 0.3 * np.sin(2 * np.pi * 7 * t))
    burst = at(mix(lp_sweep(noise(k, rng), 3500, 200, 1.5) * env_exp(k, 0.3, 0.003), boom(k, 70, 28, 0.3), n=k), 0.8, n)
    roar = at(growl(k, rng, 50, 14, 800) * env_exp(k, 0.6, 0.05) * 0.5, 0.95, n)
    return finish_fx(soft_clip(mix(quake, burst, roar, n=n), 1.5), rng, 0.6, 0.25)


def worm_death(rng, v):
    """Gusano muriendo: estallido húmedo grande y gorgoteo que se apaga."""
    n = int(1.8 * SR)
    gurgle = np.zeros(n)
    for k in range(7):
        gurgle += at(pop(0.07, rng.uniform(120, 250), rng.uniform(300, 600), 0.04) * rng.uniform(0.3, 0.7), 0.15 + 0.15 * k + rng.uniform(0, 0.05), n)
    roar = growl(n, rng, 48, 10, 700) * env_exp(n, 0.8, 0.02) * 0.5
    x = soft_clip(mix(splat(n, rng, 0.25), gurgle * 0.5, roar, boom(n, 65, 25, 0.3) * 0.9, n=n), 1.5)
    return finish_fx(x, rng, 0.5, 0.2)


def slime_spawn(rng, v):
    """Slime infernal: burbujas que revientan y un golpe de llama."""
    n = int(1.0 * SR)
    pops = np.zeros(n)
    for k in range(6):
        pops += at(pop(0.06, rng.uniform(180, 300), rng.uniform(500, 900), 0.03) * rng.uniform(0.4, 0.9), 0.05 + 0.1 * k + rng.uniform(0, 0.04), n)
    fire = at(whoosh(int(0.6 * SR), rng, 600, 4000, 0.3) * 0.5, 0.3, n)
    squelch = bandpass(noise(n, rng), 200, 1200) * env_exp(n, 0.2, 0.01) * 0.4
    return finish_fx(mix(pops * 0.8, fire, squelch, n=n), rng, 0.3, 0.15)


def slime_death(rng, v):
    """Slime reventando: salpicón húmedo, llamarada y chisporroteo."""
    n = int(1.3 * SR)
    flare = at(whoosh(int(0.8 * SR), rng, 700, 5000, 0.2) * 0.6, 0.05, n)
    sizzle = highpass(noise(n, rng), 4000) * env_exp(n, 0.6, 0.02) * 0.2
    pops = np.zeros(n)
    for k in range(5):
        pops += at(pop(0.05, rng.uniform(200, 400), rng.uniform(600, 1100), 0.025) * 0.5, 0.2 + 0.12 * k + rng.uniform(0, 0.05), n)
    x = soft_clip(mix(splat(n, rng, 0.18), flare, sizzle, pops, boom(n, 90, 40, 0.12) * 0.7, n=n), 1.5)
    return finish_fx(x, rng, 0.35, 0.18)


WORLD_RECIPES = {
    "rocket_launch": rocket_launch,
    "rocket_explosion": rocket_explosion,
    "mortar_launch": mortar_launch,
    "mortar_explosion": mortar_explosion,
    "blade_slash": blade_slash,
    "flame_loop": flame_loop,
    "flame_burst": flame_burst,
    "cannon_ability": cannon_ability,
    "rocket_ability": rocket_ability,
    "mortar_ability": mortar_ability,
    "blade_thrust": blade_thrust,
    "blade_dash": blade_dash,
    "dash": dash,
    "slide": slide,
    "overheat_warning": overheat_warning,
    "overheat_alarm": overheat_alarm,
    "bomber_spawn": bomber_spawn, "bomber_death": bomber_death,
    "charger_spawn": charger_spawn, "charger_death": charger_death,
    "destroyer_spawn": destroyer_spawn, "destroyer_death": destroyer_death,
    "flyer_spawn": flyer_spawn, "flyer_death": flyer_death,
    "worm_spawn": worm_spawn, "worm_death": worm_death,
    "slime_spawn": slime_spawn, "slime_death": slime_death,
}
