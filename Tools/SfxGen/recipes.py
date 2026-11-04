"""Recetas de SFX. Cada receta recibe (rng, v) y devuelve un array mono.

Enfoque realista-sucio: fuentes físicas (transitorios de ruido, modos de chapa, escombros, vapor)
en capas, con reverb metálico corto. El acabado lo-fi es leve (12 bits) para mantener la identidad
scrap punk sin sonar a arcade. `v` es la variación [-1, 1] de cada toma.
"""
import numpy as np
from synth import (SR, at, bandpass, debris, env_exp, highpass, lerp_curve, lofi, lowpass,
                   lp_sweep, mix, modal, noise, osc, reverb, soft_clip, transient)


def boom(n, f0, f1, decay, power=3):
    """Cuerpo grave: seno con caída exponencial de tono."""
    return osc(lerp_curve(n, f0, f1, power), "sine") * env_exp(n, decay, 0.001)


def shot(rng, v):
    """Autocannon: un disparo de arma de fuego, grave y seco. Golpe de bombo, estallido filtrado y cuerpo.

    Sin agudos ni resonancias metálicas (eso sonaba a granizo sobre chapa): todo pasa por un paso bajo y la
    energía vive en el golpe grave y el estallido medio, con una cola corta.
    """
    n = int(0.38 * SR)
    kick = osc(lerp_curve(n, 210 * (1 + 0.08 * v["a"]), 52, 4), "sine") * env_exp(n, 0.1, 0.0004) * 0.8
    crack = bandpass(noise(n, rng), 1400, 4600) * env_exp(n, 0.014, 0.0001) * 3.4
    snap = lowpass(noise(n, rng), 2800) * env_exp(n, 0.04, 0.0002) * 1.8
    bark = lowpass(osc(lerp_curve(n, 340, 130, 3), "saw"), 1400) * env_exp(n, 0.06, 0.0004) * 0.6
    body = lowpass(noise(n, rng), 900) * env_exp(n, 0.16, 0.001) * 1.0
    x = lowpass(soft_clip(mix(kick, crack, snap, bark, body, n=n), 3.0), 5500)
    return lofi(reverb(x, rng, 0.2, 0.12, 2500)[:n], 12)


def jump(rng, v):
    """Impulso del traje: roce de chatarra, empuje del pie y un clink suelto."""
    n = int(0.28 * SR)
    t = np.arange(n) / SR
    rustle = bandpass(noise(n, rng), 500, 5000) * (np.minimum(t / 0.03, 1) * np.exp(-6.9 * t / 0.16))
    push = boom(n, 95 * (1 + 0.1 * v["a"]), 55, 0.09, 2) * 0.6
    clink = at(modal(rng.uniform(1800, 3200), 0.15, rng, 0.06, ratios=(1.0, 2.7, 4.9)), 0.02, n) * 0.12
    x = mix(rustle * 0.5, push, clink, n=n)
    return lofi(reverb(x, rng, 0.2, 0.12), 12)


def land(rng, v):
    """Caída sobre chatarra: golpe sordo, chapa que vibra y escombros que saltan."""
    n = int(0.6 * SR)
    thump = boom(n, 90 * (1 + 0.1 * v["a"]), 32, 0.2)
    slap = lp_sweep(noise(n, rng), 3500, 300, 2.0) * env_exp(n, 0.07, 0.0005)
    sheet = modal(210 * (1 + 0.2 * v["b"]), 0.5, rng, 0.25) * 0.18
    x = soft_clip(mix(thump * 1.1, slap * 0.7, sheet, n=n), 1.4)
    return lofi(reverb(x, rng, 0.3, 0.18), 12)


def hurt(rng, v):
    """Impacto en la coraza: golpe amortiguado, crujido de metal doblándose, chispa seca."""
    n = int(0.5 * SR)
    thump = boom(n, 130 * (1 + 0.1 * v["a"]), 45, 0.14)
    crunch = lowpass(noise(n, rng), 2400) * env_exp(n, 0.09, 0.0005)
    buckle = modal(330 * (1 + 0.15 * v["b"]), 0.45, rng, 0.2, spread=0.04) * 0.3
    spark = highpass(noise(n, rng), 5000) * env_exp(n, 0.04, 0.0003) * 0.35
    x = soft_clip(mix(thump, crunch * 0.8, buckle, spark, n=n), 2.0)
    return lofi(reverb(x, rng, 0.25, 0.15), 11)


def xp(rng, v):
    """Fragmento de chatarra recogido: dos tintineos de metal pequeños, uno sobre el otro."""
    n = int(0.35 * SR)
    f = rng.uniform(1900, 2700)
    a = modal(f, 0.3, rng, 0.12, ratios=(1.0, 2.76, 5.4), spread=0.01)
    b = at(modal(f * 1.32, 0.25, rng, 0.09, ratios=(1.0, 2.76, 5.4), spread=0.01), 0.045, n) * 0.7
    tick = transient(n, rng, 0.002, 4000) * 0.4
    return lofi(reverb(mix(a, b, tick, n=n), rng, 0.2, 0.1, 5000), 12)


def levelup(rng, v):
    """Ascenso mecánico: golpe de pistón, barrido de aire/vapor creciente y campana metálica larga."""
    n = int(1.1 * SR)
    t = np.arange(n) / SR
    piston = at(boom(int(0.3 * SR), 110, 38, 0.15), 0.0, n)
    sweep = lp_sweep(noise(n, rng), 300, 6000, 1.0) * (np.minimum(t / 0.25, 1) * np.exp(-6.9 * np.maximum(t - 0.3, 0) / 0.5)) * 0.35
    base = 440 * (1 + 0.03 * v["a"])
    bell = np.zeros(n)
    for k, (m, g, d) in enumerate(((1.0, 1.0, 0.9), (2.0, 0.55, 0.6), (2.4, 0.45, 0.5), (3.0, 0.3, 0.4), (4.2, 0.2, 0.25))):
        bell += at(modal(base * m, 1.0, rng, d, ratios=(1.0,), spread=0.004) * g, 0.28, n)
    clank = at(modal(260, 0.3, rng, 0.1), 0.26, n) * 0.25
    x = soft_clip(mix(piston * 0.9, sweep, bell * 0.4, clank, n=n), 1.3)
    return lofi(reverb(x, rng, 0.6, 0.3, 5000), 12)


def overheat_start(rng, v):
    """Presión al límite: rumble que sube, chirridos de metal, escape de vapor y bocina grave."""
    n = int(1.4 * SR)
    t = np.arange(n) / SR
    rumble = lp_sweep(noise(n, rng), 120, 900, 1.0) * np.linspace(0.2, 1.0, n)
    steam = highpass(noise(n, rng), 2500) * np.minimum(t / 0.4, 1) * np.exp(-6.9 * np.maximum(t - 0.6, 0) / 0.9) * 0.45
    f = 150 * (1 + 0.04 * v["a"])
    horn = lowpass(osc(np.full(n, f), "saw"), 900) * np.minimum(t / 0.1, 1) * np.exp(-6.9 * np.maximum(t - 0.5, 0) / 0.6) * 0.35
    creaks = np.zeros(n)
    for _ in range(5):
        creaks += at(modal(rng.uniform(300, 900), 0.2, rng, 0.07, spread=0.05), rng.uniform(0.0, 0.8), n) * 0.12
    hit = boom(n, 80, 30, 0.3)
    x = soft_clip(mix(rumble * 0.7, steam, horn, creaks, hit * 0.8, n=n), 1.8)
    return lofi(reverb(x, rng, 0.5, 0.2), 12)


def overheat_end(rng, v):
    """Venteo: chorro de vapor que se apaga, golpe de válvula y chapa que se contrae."""
    n = int(1.2 * SR)
    steam = lp_sweep(noise(n, rng), 9000, 700, 0.8) * env_exp(n, 0.7, 0.02)
    clunk = mix(boom(n, 100, 40, 0.12) * 0.8, modal(240, 0.5, rng, 0.2) * 0.25, n=n)
    x = soft_clip(mix(steam * 0.8, clunk, n=n), 1.4)
    return lofi(reverb(x, rng, 0.45, 0.2), 12)


def enemy_hit(rng, v):
    """Golpe seco sobre chatarra."""
    n = int(0.22 * SR)
    tick = transient(n, rng, 0.004, 1200)
    body = lowpass(noise(n, rng), 1800) * env_exp(n, 0.04, 0.0005)
    ping = modal(rng.uniform(500, 1100), 0.2, rng, 0.07) * 0.25
    thump = boom(n, 160 * (1 + 0.1 * v["a"]), 70, 0.05, 2) * 0.6
    return lofi(reverb(mix(tick * 0.8, body * 0.7, ping, thump, n=n), rng, 0.15, 0.1), 12)


def enemy_death(rng, v):
    """Colapso: estallido, estructura que se derrumba y lluvia de escombros."""
    n = int(0.9 * SR)
    burst = lp_sweep(noise(n, rng), 6000, 400, 1.6) * env_exp(n, 0.25, 0.0005)
    thump = boom(n, 110 * (1 + 0.1 * v["a"]), 36, 0.25)
    crash = modal(rng.uniform(260, 420), 0.7, rng, 0.35, spread=0.05) * 0.25
    x = soft_clip(mix(burst * 0.9, thump, crash, transient(n, rng, 0.004, 1500) * 0.6, n=n), 1.6)
    return lofi(reverb(x, rng, 0.4, 0.2), 12)


RECIPES = {
    "shot": shot,
    "jump": jump,
    "land": land,
    "hurt": hurt,
    "xp": xp,
    "levelup": levelup,
    "overheat_start": overheat_start,
    "overheat_end": overheat_end,
    "enemy_hit": enemy_hit,
    "enemy_death": enemy_death,
}


from recipes_world import WORLD_RECIPES  # noqa: E402

RECIPES.update(WORLD_RECIPES)
