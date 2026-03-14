import argparse
import json
import textwrap
from pathlib import Path

import matplotlib.pyplot as plt


def _to_str(value):
    if value is None:
        return ""
    return str(value)


def _to_int(value):
    try:
        return int(value)
    except Exception:
        return 0


def _wrap_name(name, max_chars=30):
    text = _to_str(name)
    parts = [p.strip() for p in text.split(",") if p.strip()]
    if len(parts) <= 1:
        wrapped = textwrap.wrap(text, width=max_chars)
        return "\n".join(wrapped) if wrapped else text

    lines = []
    current = ""
    for part in parts:
        candidate = f"{current}, {part}" if current else part
        if len(candidate) <= max_chars:
            current = candidate
        else:
            if current:
                lines.append(current + ",")
            current = part
    if current:
        lines.append(current)
    return "\n".join(lines)


def _build_chart_rows(rows):
    chart_rows = []
    for row in rows:
        edited_name = (
            row.get("Edited Name")
            or row.get("editedName")
            or row.get("EditedName")
            or ""
        )
        count = _to_int(row.get("Count") if "Count" in row else row.get("count"))
        if edited_name and count > 0:
            chart_rows.append({"Edited Name": edited_name, "Count": count})

    chart_rows.sort(key=lambda r: r["Count"], reverse=True)
    return chart_rows


def main():
    parser = argparse.ArgumentParser(description="Generate nested pie chart PNG from comparison rows")
    parser.add_argument("--input-json", required=True, help="Input JSON file containing comparison rows")
    parser.add_argument("--output-png", required=True, help="Output PNG path")
    args = parser.parse_args()

    input_path = Path(args.input_json)
    output_path = Path(args.output_png)

    rows = json.loads(input_path.read_text(encoding="utf-8"))
    chart_rows = _build_chart_rows(rows)

    if not chart_rows:
        fig, ax = plt.subplots(figsize=(12, 8))
        ax.text(0.5, 0.5, "No comparison data", ha="center", va="center")
        ax.set_axis_off()
        plt.tight_layout()
        output_path.parent.mkdir(parents=True, exist_ok=True)
        fig.savefig(output_path, dpi=150)
        plt.close(fig)
        return

    counts = [r["Count"] for r in chart_rows]
    min_count = min(counts)
    max_count = max(counts)
    if max_count == min_count:
        normalized = [0.6] * len(chart_rows)
    else:
        normalized = [(c - min_count) / (max_count - min_count) for c in counts]

    blues_cmap = plt.get_cmap("Blues")
    colors = [blues_cmap(0.25 + 0.65 * n) for n in normalized]

    inner_values = [1] * len(chart_rows)
    outer_values = counts
    total_outer = sum(outer_values)

    def outer_autopct(pct):
        count = int(round(pct * total_outer / 100.0))
        return f"{count}" if count > 0 else ""

    fig, ax = plt.subplots(figsize=(12, 8))

    outer_pie = ax.pie(
        outer_values,
        radius=1.0,
        labels=None,
        colors=colors,
        startangle=90,
        autopct=outer_autopct,
        pctdistance=0.86,
        wedgeprops={"width": 0.32, "edgecolor": "white", "linewidth": 1},
    )
    outer_wedges = outer_pie[0]
    outer_autotexts = outer_pie[2] if len(outer_pie) > 2 else []

    ax.pie(
        inner_values,
        radius=0.68,
        labels=None,
        colors=colors,
        startangle=90,
        wedgeprops={"width": 0.30, "edgecolor": "white", "linewidth": 1},
    )

    ax.set_title("Edited Model Name - Nested Pie", fontsize=18)
    ax.axis("equal")

    legend_labels = [
        f"{_wrap_name(r['Edited Name'])} ({r['Count']})" for r in chart_rows
    ]
    ax.legend(
        outer_wedges,
        legend_labels,
        title="Edited Name",
        loc="center left",
        bbox_to_anchor=(1.02, 0.5),
        frameon=False,
    )

    for autotext in outer_autotexts:
        autotext.set_color("white")
        autotext.set_fontsize(10)
        autotext.set_fontweight("bold")

    plt.tight_layout()
    output_path.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(output_path, dpi=150)
    plt.close(fig)


if __name__ == "__main__":
    main()
