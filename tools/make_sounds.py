#!/usr/bin/env python3
"""Synthesises AVAMoonCresta's sound effects and tunes into ../src/AVAMoonCresta/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAMoonCresta", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The rocket's guns: a bright double pip; the aliens bursting, a bird splitting, a meteor's dull clank.
    write("Fire.wav", cat(tone(0.05, 2200, 1600, "square", release=0.6), tone(0.07, 1800, 900, "square", release=0.8)), 0.22)
    write("Explode.wav", mix(tone(0.35, 3200, 200, "noise", release=0.8), tone(0.25, 700, 120, "square", release=0.8, gain=0.5)), 0.38)
    write("Split.wav", mix(tone(0.3, 600, 1800, "saw", release=0.6), tone(0.3, 2400, 900, "noise", release=0.7, gain=0.5)), 0.32)
    write("Meteor.wav", mix(tone(0.6, 1800, 60, "noise", release=0.6), tone(0.5, 200, 40, "saw", release=0.6, gain=0.8)), 0.45)
    write("MeteorHit.wav", tone(0.06, 400, 300, "tri", release=0.8), 0.22)
    write("Bomb.wav", tone(0.25, 1400, 500, "square", release=0.5), 0.16)
    write("Swoop.wav", tone(0.6, 600, 1800, "tri", release=0.3, vibrato=0.08, vib_rate=10), 0.22)
    # Losing a stage, and the whole rocket.
    write("StageLost.wav", mix(tone(0.7, 2600, 80, "noise", release=0.5), tone(0.6, 600, 60, "saw", release=0.5, gain=0.6)), 0.46)
    write("Death.wav", cat(mix(tone(1.0, 3200, 50, "noise", release=0.5), tone(1.0, 500, 30, "saw", release=0.5, gain=0.7)),
                           *[tone(0.15, hz(n), kind="tri", release=0.4) for n in ["G5", "D5", "B4", "G4"]]), 0.5)
    write("WaveStart.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.4) for n in ["G4", "D5", "G5"]]), 0.32)
    write("WaveClear.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.4) for n in ["G5", "B5", "D6", "G6"]]), 0.34)
    # Docking: the call to dock, the thruster, the clunk and fanfare of a good dock, and a crash.
    write("DockStart.wav", cat(*[tone(0.12, hz(n), kind="tri", release=0.4) for n in ["D5", "G5", "D5", "G5"]]), 0.34)
    write("Thrust.wav", mix(tone(0.28, 800, 500, "noise", attack=0.03, release=0.4), tone(0.28, 70, 60, "saw", release=0.4, gain=0.5)), 0.2)
    write("Docked.wav", cat(mix(tone(0.15, 200, 90, "square", release=0.8), tone(0.1, 1600, 700, "noise", release=0.8, gain=0.4)),
                            *[tone(0.1, hz(n), kind="pulse", release=0.4) for n in ["G5", "B5", "D6", "G6", "D6", "G6"]]), 0.42)
    write("DockCrash.wav", mix(tone(1.0, 2200, 40, "noise", release=0.5), tone(0.9, 300, 30, "saw", release=0.5, gain=0.8)), 0.5)
    write("ExtraStage.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["G5", "D6", "G5", "D6", "G6"]]), 0.38)

    # The engines: a low, steady drone with a slow warble. Loops (four seconds, whole cycles).
    n = int(RATE * 4)
    drone = []
    for i in range(n):
        t = i / RATE
        drone.append(math.sin(2 * math.pi * 60 * t) * (0.6 + 0.2 * math.sin(2 * math.pi * 1 * t)) + 0.25 * math.sin(2 * math.pi * 90.5 * t)
                     + 0.05 * math.sin(2 * math.pi * 720 * t) * (0.5 + 0.5 * math.sin(2 * math.pi * 0.25 * t)))
    write("Music.wav", drone, 0.2)

    # The title: a bright space-age tune in G (an original composition).
    lead = [("G5", 0.5), ("D5", 0.5), ("G5", 0.5), ("B5", 0.5), ("D6", 1), ("B5", 1), ("C6", 0.5), ("B5", 0.5), ("A5", 0.5), ("G5", 0.5), ("A5", 2),
            ("A5", 0.5), ("E5", 0.5), ("A5", 0.5), ("C6", 0.5), ("E6", 1), ("C6", 1), ("D6", 0.5), ("C6", 0.5), ("B5", 0.5), ("A5", 0.5), ("B5", 2),
            ("D6", 0.5), ("B5", 0.5), ("G5", 1), ("E6", 0.5), ("C6", 0.5), ("A5", 1), ("F#5", 1), ("A5", 1), ("G5", 3)]
    bass = [("G2", 1), ("G2", 1), ("G2", 1), ("B2", 1), ("C3", 1), ("C3", 1), ("D3", 2),
            ("A2", 1), ("A2", 1), ("A2", 1), ("C3", 1), ("D3", 1), ("D3", 1), ("G2", 2),
            ("G2", 2), ("C3", 2), ("D3", 2), ("G2", 3)]
    write("TitleTune.wav", cat(tune(160, lead, bass, kind="pulse"), silence(1.2)), 0.42)
    write("StartTune.wav", tune(170, [("G4", 0.5), ("B4", 0.5), ("D5", 0.5), ("G5", 0.5), ("F#5", 0.5), ("G5", 2)], [("G2", 1), ("D3", 1), ("G2", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(72, [("D5", 1), ("B4", 1), ("A4", 1), ("F#4", 1), ("G4", 4)], [("G2", 2), ("E2", 2), ("D2", 2), ("G2", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
