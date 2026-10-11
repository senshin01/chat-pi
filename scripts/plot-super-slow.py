"""Optional design plot: python scripts/plot-super-slow.py (NumPy, Matplotlib)."""
from pathlib import Path
import matplotlib
matplotlib.use("Agg")
matplotlib.rcParams["svg.hashsalt"] = "chatpi-super-slow-v1"
import matplotlib.pyplot as plt
import numpy as np

rate = 44100
frequency = np.linspace(0, rate / 2, 1001)
colors = {2: "#24799c", 4: "#b05728"}
# Measured steady-state tones from SuperSlowTests, rounded to five decimals.
measured = {2: [-0.01102, -1.12611, -4.84055], 4: [-0.01378, -1.40308, -5.96666]}
fig, axes = plt.subplots(1, 2, figsize=(10, 3.6), layout="constrained")
for factor in (2, 4):
    response = (np.sinc(frequency / rate) / np.sinc(frequency / (rate * factor))) ** 2
    axes[0].plot(frequency / 1000, 20 * np.log10(response), color=colors[factor], label=f"{factor}x analytical")
    axes[0].scatter([1, 10, 20], measured[factor], color=colors[factor], s=22, zorder=3)
    indices = np.arange(-factor, factor + 1)
    impulse = np.maximum(0, 1 - np.abs(indices) / factor)
    axes[1].plot(indices / (rate * factor) * 1e6, impulse, color=colors[factor],
                 marker="s" if factor == 2 else "o", markersize=8 if factor == 2 else 4,
                 markerfacecolor="none" if factor == 2 else colors[factor], label=f"{factor}x")
axes[0].set(xlabel="Frequency (kHz)", ylabel="Gain (dB)", title="Super slow: 44.1 kHz input", xlim=(0, 22.05), ylim=(-8.2, .2))
axes[0].text(.03, .05, "Dots: measured test tones", transform=axes[0].transAxes, fontsize=9)
axes[1].set(xlabel="Time from impulse center (microseconds)", ylabel="Impulse amplitude", title="Positive triangular response", ylim=(-.05, 1.1))
for axis in axes:
    axis.grid(alpha=.25)
    axis.legend(frameon=False)
    axis.spines[["top", "right"]].set_visible(False)
destination = Path(__file__).resolve().parents[1] / "docs/super-slow-response.svg"
fig.savefig(destination, metadata={"Date": None})
destination.write_text("\n".join(line.rstrip() for line in destination.read_text(encoding="utf-8").splitlines()) + "\n", encoding="utf-8")
