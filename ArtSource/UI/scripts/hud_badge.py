"""Chapita de nivel que se atornilla en la esquina de cada slot (el número lo pone Unity)."""
import os

_base = os.path.join(os.path.dirname(__file__), "hud_parts.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("HudBadge")
plate("Chapa", rrect(0, 0, 0.066, 0.046, 0.01), 0.012, "laton", y=0.006, bev=0.003)
plate("Level_Fondo", rrect(0.009, 0.008, 0.057, 0.038, 0.005, 2), 0.004, "negro", y=-0.002, bev=0)
mark("Level", 0.009, 0.008, 0.057, 0.038)
