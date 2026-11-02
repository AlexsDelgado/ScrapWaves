"""Recetas de armas, habilidades, enemigos y avisos. Mismo enfoque que recipes.py:
fuentes físicas en capas + reverb metálico corto + lo-fi leve (12 bits).
"""
import numpy as np
from synth import (SR, at, bandpass, debris, env_exp, highpass, lerp_curve, loopify, lofi, lowpass,
                   lp_sweep, mix, modal, noise, osc, reverb, soft_clip, transient)


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
    rain = debris(n, rng, debris_n, dur * 0.7, 500, 5000, 0.28, 0.08)
    x = soft_clip(mix(blast * 0.9, body * 1.1, rumble, rain, transient(n, rng, 0.005, 1200) * 0.7, n=n), 1.7)
    return reverb(x, rng, t60, wet)


def finish_fx(x, rng, t60, wet):
    return lofi(reverb(x, rng, t60, wet), 12)


# ---------------------------------------------------------------- armas

def rocket_launch(rng, v):
    """Ignición del cohete: golpe de presión y chorro que arranca y se aleja."""
    n = int(1.0 * SR)
    kick = boom(n, 120, 45, 0.1)
    jet = whoosh(n, rng, 800, 6000, 0.25) * 0.9
    roar = lowpass(noise(n, rng), 900) * np.minimum(np.arange(n) / SR / 0.05, 1) * env_exp(n, 0.5, 0.0) * 0.5
    crackle = debris(n, rng, 10, 0.5, 1500, 6000, 0.12)
    x = soft_clip(mix(kick * 0.8, jet, roar, crackle, transient(n, rng, 0.004, 1500) * 0.5, n=n), 1.6)
    return finish_fx(x, rng, 0.4, 0.2)


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
    """Tajo: aire cortado y 'shing' de metal."""
    n = int(0.5 * SR)
    air = whoosh(n, rng, 900, 5500, 0.35) * 0.9
    shing = at(modal(2400 * (1 + 0.1 * v["a"]), 0.45, rng, 0.2, ratios=(1.0, 1.9, 3.3, 5.1), spread=0.02) * 0.14, 0.08, n)
    return finish_fx(mix(air, shing, n=n), rng, 0.25, 0.12)


def flame_loop(rng, v):
    """Lanzallamas continuo sin costura: rugido ancho, cuerpo grave y chisporroteo."""
    n = int(2.4 * SR)
    wob = lowpass(noise(n, rng), 9)
    wob = 0.65 + 0.35 * wob / (np.max(np.abs(wob)) + 1e-9)
    roar = lowpass(highpass(noise(n, rng), 300), 3000) * wob
    low = lowpass(noise(n, rng), 220) * 1.2
    crackle = debris(n, rng, 26, 2.3, 1800, 7000, 0.15)
    x = soft_clip(mix(roar * 0.7, low * 0.5, crackle, n=n), 1.3)
    return lofi(loopify(x, 0.2), 12)


def flame_burst(rng, v):
    """Habilidad del lanzallamas: ignición 'whoomp' y llamarada densa."""
    n = int(1.1 * SR)
    whoomp = boom(n, 90, 35, 0.2)
    flare = whoosh(n, rng, 500, 4500, 0.2) * 0.8
    roar = lowpass(noise(n, rng), 2200) * env_exp(n, 0.6, 0.02) * 0.7
    crackle = debris(n, rng, 14, 0.8, 1800, 7000, 0.14)
    x = soft_clip(mix(whoomp, flare, roar, crackle, n=n), 1.5)
    return finish_fx(x, rng, 0.45, 0.2)


# ---------------------------------------------------------------- habilidades

def cannon_ability(rng, v):
    """Ráfaga del cañón: motor que gira y siete disparos secos en cadena."""
    n = int(1.1 * SR)
    m = int(0.2 * SR)
    motor = servo(n, 120, 360, 1100) * np.minimum(np.arange(n) / SR / 0.15, 1) * env_exp(n, 1.2, 0.0) * 0.35
    shots = np.zeros(n)
    for k in range(7):
        s = mix(transient(m, rng, 0.004, 2000),
                lp_sweep(noise(m, rng), 6000, 600, 2.0) * env_exp(m, 0.06, 0.0005),
                boom(m, 140, 50, 0.07), n=m)
        shots += at(s * (0.9 - 0.04 * k), 0.12 + 0.085 * k, n)
    return finish_fx(soft_clip(mix(motor, shots, n=n), 1.6), rng, 0.35, 0.18)


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
    """Estocada: aire rápido y agudo, golpe seco, 'shing' corto."""
    n = int(0.45 * SR)
    air = whoosh(n, rng, 2500, 7000, 0.3) * 0.8
    hit = boom(n, 150, 60, 0.07) * 0.7
    shing = at(modal(3100, 0.3, rng, 0.12, ratios=(1.0, 2.1, 3.7), spread=0.02) * 0.15, 0.12, n)
    return finish_fx(mix(air, hit, shing, n=n), rng, 0.22, 0.1)


def blade_dash(rng, v):
    """Dash atómico: barrido largo de aire, raspado de metal, golpe al final."""
    n = int(0.8 * SR)
    m = int(0.3 * SR)
    air = whoosh(n, rng, 400, 6000, 0.45) * 0.9
    scrape = bandpass(noise(n, rng), 1500, 6000) * np.minimum(np.arange(n) / SR / 0.05, 1) * env_exp(n, 0.3, 0.0) * 0.3
    end = at(mix(boom(m, 110, 45, 0.1), modal(900, 0.3, rng, 0.1)[:m] * 0.2, n=m), 0.4, n)
    return finish_fx(mix(air, scrape, end, n=n), rng, 0.3, 0.15)


def dash(rng, v):
    """Dash del jugador: ráfaga de propulsión y empuje del cuerpo."""
    n = int(0.45 * SR)
    burst = whoosh(n, rng, 500, 4500, 0.18) * 0.9
    push = boom(n, 100, 50, 0.1, 2) * 0.7
    rattle = debris(n, rng, 4, 0.15, 1500, 4500, 0.15)
    return finish_fx(mix(burst, push, rattle, n=n), rng, 0.2, 0.1)


def slide(rng, v):
    """Derrape sobre chatarra: raspado granulado con piedritas."""
    n = int(0.8 * SR)
    t = np.arange(n) / SR
    grit = bandpass(noise(n, rng), 300, 3500) * (np.minimum(t / 0.04, 1) * np.exp(-6.9 * t / 0.5))
    grit *= 0.7 + 0.3 * np.sign(np.sin(2 * np.pi * (60 + 20 * v["a"]) * t))
    low = lowpass(noise(n, rng), 250) * env_exp(n, 0.3, 0.01) * 0.6
    stones = debris(n, rng, 12, 0.55, 1000, 4500, 0.2, 0.03)
    return finish_fx(mix(grit * 0.7, low, stones, n=n), rng, 0.25, 0.1)


def overheat_warning(rng, v):
    """Aviso: dos pulsos de bocina industrial grave sobre un siseo de presión."""
    n = int(1.0 * SR)
    t = np.arange(n) / SR
    f = 210 * (1 + 0.02 * v["a"])
    m = int(0.3 * SR)
    horn = np.zeros(n)
    for k in range(2):
        h = lowpass(osc(np.full(m, f), "saw") + 0.5 * osc(np.full(m, f * 1.5), "saw"), 1500)
        h *= np.minimum(np.arange(m) / SR / 0.02, 1) * np.minimum((m - np.arange(m)) / SR / 0.06, 1)
        horn += at(h, 0.05 + 0.4 * k, n)
    hiss = highpass(noise(n, rng), 3500) * np.minimum(t / 0.2, 1) * env_exp(n, 0.8, 0.0) * 0.25
    tick = modal(520, 0.2, rng, 0.08) * 0.2
    return finish_fx(soft_clip(mix(horn * 0.5, hiss, tick, n=n), 1.8), rng, 0.35, 0.18)


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
    crash = at(mix(boom(c, 130, 50, 0.1), modal(420, 0.35, rng, 0.15)[:c] * 0.3,
                   debris(c, rng, 6, 0.25, 800, 4500, 0.25), n=c), 0.45, n)
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
    "bomber_spawn": bomber_spawn, "bomber_death": bomber_death,
    "charger_spawn": charger_spawn, "charger_death": charger_death,
    "destroyer_spawn": destroyer_spawn, "destroyer_death": destroyer_death,
    "flyer_spawn": flyer_spawn, "flyer_death": flyer_death,
    "worm_spawn": worm_spawn, "worm_death": worm_death,
    "slime_spawn": slime_spawn, "slime_death": slime_death,
}
