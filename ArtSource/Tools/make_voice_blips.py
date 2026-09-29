"""Pitidos de "voz" para el diálogo de jefes: blips cortos y procedurales, uno por cada pocas letras.

Uso (desde la raíz del repo):
    python ArtSource/Tools/make_voice_blips.py [--voice stalker]
Escribe Assets/Audio/Dialogue/Voice_<Voz>_<n>.wav (mono, 22.05 kHz, 16 bits). Cada voz es un preset de
frecuencia, forma de onda y "suciedad"; se asignan al DialogueSpeaker (campo Voice Blips).
"""
import argparse
import math
import os
import random
import struct
import wave

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(REPO, "Assets", "Audio", "Dialogue")
RATE = 22050

# base: Hz de cada variante; drop: cuánto cae el tono dentro del blip; crush: pasos de bitcrush (0 = limpio);
# noise: mezcla de ruido; pulse: ancho de pulso de la cuadrada (0.5 = cuadrada pareja).
VOICES = {
    "stalker": dict(base=(150, 168, 182, 196, 214, 232), ms=72, drop=0.28, crush=12, noise=0.16, pulse=0.3, gain=0.55),
}


def blip(freq, ms, drop, crush, noise, pulse, gain, seed):
    rng = random.Random(seed)
    n = int(RATE * ms / 1000)
    phase, out = 0.0, []
    for i in range(n):
        t = i / n
        f = freq * (1.0 - drop * t)
        phase = (phase + f / RATE) % 1.0
        # Pulso que se abre y cierra: suena más "boca mecánica" que una cuadrada fija.
        width = pulse + 0.12 * math.sin(2 * math.pi * 9 * t)
        v = 1.0 if phase < width else -1.0
        v = 0.75 * v + 0.25 * math.sin(2 * math.pi * phase * 2)
        v = (1 - noise) * v + noise * rng.uniform(-1, 1)
        if crush:
            v = round(v * crush) / crush
        attack, release = min(1.0, t / 0.08), min(1.0, (1 - t) / 0.35)
        out.append(v * gain * attack * release)
    return out


def write(path, samples):
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, s)) * 32767)) for s in samples))


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--voice", default="stalker", choices=sorted(VOICES))
    args = ap.parse_args()
    preset = VOICES[args.voice]
    os.makedirs(OUT, exist_ok=True)
    name = args.voice.capitalize()
    for i, freq in enumerate(preset["base"]):
        samples = blip(freq, preset["ms"], preset["drop"], preset["crush"], preset["noise"], preset["pulse"],
                       preset["gain"], seed=i)
        write(os.path.join(OUT, f"Voice_{name}_{i}.wav"), samples)
    print("OK", OUT, len(preset["base"]))
