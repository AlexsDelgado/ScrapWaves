"""Primitivas de síntesis para los SFX de Dumpster Fire (estilo mid-low poly / scrap punk).

Todo se renderiza mono a SR Hz y se degrada al final (bitcrush + sample-hold) para el
acabado sucio y de baja resolución que combina con la estética del juego.
"""
import numpy as np
from scipy import signal

SR = 44100


def t_axis(dur):
    return np.arange(int(dur * SR)) / SR


def lerp_curve(n, start, end, power=1.0):
    """Curva start->end; power > 1 concentra el cambio al inicio."""
    x = np.linspace(0.0, 1.0, n)
    return start + (end - start) * (1.0 - (1.0 - x) ** power)


def osc(freq, shape="square", duty=0.5):
    """Oscilador con frecuencia por muestra (array) o fija. Formas: sine, square, saw, tri."""
    freq = np.asarray(freq, dtype=np.float64)
    phase = np.cumsum(freq / SR) % 1.0
    if shape == "sine":
        return np.sin(2 * np.pi * phase)
    if shape == "square":
        return np.where(phase < duty, 1.0, -1.0)
    if shape == "saw":
        return 2.0 * phase - 1.0
    if shape == "tri":
        return 4.0 * np.abs(phase - 0.5) - 1.0
    raise ValueError(shape)


def noise(n, rng):
    return rng.uniform(-1.0, 1.0, n)


def env_exp(n, decay_s, attack_s=0.002):
    """Ataque lineal corto + caída exponencial (decay_s = tiempo a ~-60 dB)."""
    t = np.arange(n) / SR
    e = np.exp(-6.9 * t / max(decay_s, 1e-4))
    a = max(int(attack_s * SR), 1)
    e[:a] *= np.linspace(0.0, 1.0, a)
    return e


def lowpass(x, cutoff, order=2):
    sos = signal.butter(order, min(cutoff, SR * 0.45), "low", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def highpass(x, cutoff, order=2):
    sos = signal.butter(order, cutoff, "high", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def bandpass(x, lo, hi, order=2):
    sos = signal.butter(order, [lo, min(hi, SR * 0.45)], "band", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def lp_sweep(x, f_start, f_end, power=1.0):
    """Paso bajo de un polo con corte variable en el tiempo."""
    n = len(x)
    fc = lerp_curve(n, f_start, f_end, power)
    alpha = 1.0 - np.exp(-2 * np.pi * fc / SR)
    y = np.empty(n)
    acc = 0.0
    for i in range(n):
        acc += alpha[i] * (x[i] - acc)
        y[i] = acc
    return y


def metal_ping(freq, dur, rng, decay=0.08, partials=(1.0, 2.76, 5.40, 8.93)):
    """Parciales inarmónicos que decaen: golpe de chatarra / lata."""
    n = int(dur * SR)
    out = np.zeros(n)
    for i, ratio in enumerate(partials):
        jitter = 1.0 + rng.uniform(-0.03, 0.03)
        out += osc(freq * ratio * jitter, "sine") * env_exp(n, decay / (1 + 0.6 * i)) / (1 + i)
    return out


def pad(x, n):
    out = np.zeros(n)
    out[: min(len(x), n)] = x[:n]
    return out


def mix(*layers, n=None):
    n = n or max(len(l) for l in layers)
    out = np.zeros(n)
    for l in layers:
        out += pad(l, n)
    return out


def at(x, start_s, n):
    """Coloca x desplazado start_s dentro de un buffer de n muestras."""
    out = np.zeros(n)
    s = int(start_s * SR)
    if s < n:
        seg = x[: n - s]
        out[s:s + len(seg)] = seg
    return out


def ring_mod(x, freq):
    return x * osc(np.full(len(x), freq), "sine")


def soft_clip(x, drive=2.0):
    return np.tanh(x * drive) / np.tanh(drive)


def lofi(x, bits=12, hold=1):
    """Bitcrush + sample-hold: el acabado mid-low poly."""
    if hold > 1:
        x = np.repeat(x[::hold], hold)[: len(x)]
    levels = 2 ** (bits - 1)
    return np.round(x * levels) / levels


def finish(x, peak=0.89, fade_ms=4):
    """Quita DC, normaliza y aplica fade-out para evitar clicks."""
    x = x - np.mean(x)
    m = np.max(np.abs(x))
    if m > 0:
        x = x / m * peak
    f = max(int(fade_ms * SR / 1000), 1)
    x[-f:] *= np.linspace(1, 0, f)
    return x


def write_wav(path, x):
    from scipy.io import wavfile
    wavfile.write(path, SR, (np.clip(x, -1, 1) * 32767).astype(np.int16))


PLATE = (1.0, 1.593, 2.136, 2.295, 2.653, 2.918, 3.156, 3.5)


def modal(freq, dur, rng, decay=0.2, ratios=PLATE, spread=0.015, tilt=0.75):
    """Chapa/lata golpeada: suma de modos amortiguados (más graves duran más, más agudos mueren antes)."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    out = np.zeros(n)
    for i, r in enumerate(ratios):
        f = freq * r * (1 + rng.uniform(-spread, spread))
        if f > SR * 0.45:
            continue
        d = decay / (1 + 0.55 * i)
        out += np.sin(2 * np.pi * f * t + rng.uniform(0, 6.28)) * np.exp(-6.9 * t / d) * (tilt ** i)
    a = max(int(0.0003 * SR), 1)
    out[:a] *= np.linspace(0, 1, a)
    return out


def debris(n, rng, count, t_max, f_lo=900, f_hi=6000, gain=0.5, t_min=0.0):
    """Trocitos de chatarra rebotando: clicks metálicos cortos en tiempos y tonos al azar."""
    out = np.zeros(n)
    for _ in range(count):
        tink = modal(rng.uniform(f_lo, f_hi), 0.06, rng, rng.uniform(0.01, 0.04), ratios=(1.0, 2.7))
        tink *= rng.uniform(0.2, 1.0) * gain
        out += at(tink, rng.uniform(t_min, t_max), n)
    return out


def reverb(x, rng, t60=0.35, wet=0.2, damp=3500):
    """Sala metálica corta: IR de ruido con caída exponencial y filtrada."""
    m = int(t60 * SR)
    ir = lowpass(noise(m, rng), damp) * np.exp(-6.9 * np.arange(m) / SR / t60)
    ir[: int(0.004 * SR)] *= np.linspace(0, 1, int(0.004 * SR))
    wetsig = signal.fftconvolve(x, ir)[: len(x) + m]
    wetsig = wetsig / (np.max(np.abs(wetsig)) + 1e-9) * np.max(np.abs(x))
    return mix(x * (1 - wet * 0.5), wetsig * wet, n=len(x) + m)


def transient(n, rng, dur=0.004, hp=1500):
    """Chasquido inicial: ruido muy corto filtrado hacia agudos."""
    k = int(dur * SR)
    out = np.zeros(n)
    out[:k] = highpass(noise(k + 64, rng), hp)[:k] * np.linspace(1, 0, k)
    return out


def loopify(x, xfade_s=0.15):
    """Hace un loop sin costura: la cola se mezcla (crossfade) con el inicio."""
    xf = int(xfade_s * SR)
    body = x[:-xf].copy()
    tail = x[-xf:]
    fade = np.linspace(0.0, 1.0, xf)
    body[:xf] = body[:xf] * fade + tail * (1.0 - fade)
    return body
