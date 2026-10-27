"""Render report.json into one self-contained HTML file. Reads only the JSON on disk."""

from __future__ import annotations

import json
from pathlib import Path

from jinja2 import Environment

from extractor_tester.models import Report

_TEMPLATE = """<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8">
<title>extractor-tester report</title>
<style>
body{font-family:sans-serif;margin:1.5em;color:#222}
table{border-collapse:collapse;margin:.5em 0}
th,td{border:1px solid #ccc;padding:.2em .6em;text-align:left}
th.sortable{cursor:pointer}
.warn{color:#b45309;font-weight:bold}
details{margin:.4em 0 .4em 1em}
code,pre{background:#f4f4f4}
</style></head><body>
{%- macro metric(m) -%}
{{ m.key }}: {{ m.count }}/{{ m.denominator }} ({{ "%.1f"|format(m.rate * 100) }}%)
{%- if m.examples %}
<details><summary>{{ m.examples|length }} examples</summary>
{%- for e in m.examples %}
<div>item {{ e.item_id }}
{%- if e.path_old or e.path_new %} &middot; {{ e.path_old }} / {{ e.path_new }}{% endif %}
 &middot; old <code>{{ e.old_value }}</code> &middot; new <code>{{ e.new_value }}</code>
{%- if e.note %} &middot; {{ e.note }}{% endif %}</div>
{%- endfor %}
</details>
{%- endif -%}
{%- endmacro -%}
<h1>extractor-tester report</h1>
<p>old: {{ r.inputs.get("old") }} &nbsp; new: {{ r.inputs.get("new") }}
 &nbsp; started {{ r.started_at.isoformat() }} &nbsp; v{{ r.tool_version }}</p>
<p id="totals">Items
{%- for k, v in r.totals.items() %} {{ k }} {{ v }}
{{- " &middot;"|safe if not loop.last }}{% endfor %}
 &middot; {{ r.items_only_old.count }} only old &middot; {{ r.items_only_new.count }} only new</p>
{% if r.warnings %}<h2>Warnings</h2>
<ul>{% for w in r.warnings %}<li class="warn">{{ w }}</li>{% endfor %}</ul>{% endif %}
<h2>Type families</h2>
<table id="families"><thead><tr>
{%- for h in ["family", "pairs", "identical", "only old", "only new", "median sim", "warning"] %}
<th class="sortable">{{ h }}</th>{% endfor %}</tr></thead><tbody>
{%- for f in r.families %}
<tr><td>{{ f.family }}</td><td>{{ f.pairs }}</td>
<td>{{ "%.0f"|format(f.identical.rate * 100) }}%</td>
<td>{{ f.only_in_old.count }}</td><td>{{ f.only_in_new.count }}</td>
<td>{{ f.edit_similarity.get("median", "") }}</td>
<td>{% if f.cut_lines_warning %}<span class="warn">&#9888;</span>{% endif %}</td></tr>
{%- endfor %}</tbody></table>
{% for f in r.families %}
<details class="family"><summary>{{ f.family }}
{%- if f.cut_lines_warning %} <span class="warn">&#9888;</span>{% endif %}</summary>
<h3>Fields</h3>
<ul>{% for m in f.fields %}<li>{{ metric(m) }}</li>{% else %}<li>none</li>{% endfor %}</ul>
<h3>Text</h3>
<ul>
<li>edit similarity: {% for k, v in f.edit_similarity.items() %}{{ k }} {{ v }}; {% endfor %}</li>
<li>word-set similarity: {% for k, v in f.jaccard.items() %}{{ k }} {{ v }}; {% endfor %}</li>
{% for m in f.relations %}<li>{{ metric(m) }}</li>{% endfor %}
<li>methods: {% for k, v in f.methods.items() %}{{ k }} {{ v }}; {% endfor %}</li>
<li>cut lines: {{ "%.1f"|format(f.lines_cut_pct) }}%</li>
</ul>
<h3>Matching tiers</h3>
<ul>{% for m in f.tiers %}<li>{{ metric(m) }}</li>{% endfor %}</ul>
<h3>Hints</h3>
<ul>{% for m in f.hints %}<li>{{ metric(m) }}</li>{% endfor %}</ul>
<h3>File errors</h3>
<ul><li>{{ metric(f.decode_errors) }}</li>
{% for m in f.file_errors %}<li>{{ metric(m) }}</li>{% endfor %}</ul>
</details>
{% endfor %}
<h2>Item metadata changes</h2>
<ul>{% for m in r.item_fields %}<li>{{ metric(m) }}</li>{% else %}<li>none</li>{% endfor %}</ul>
<h2>Errors by code</h2>
<ul>{% for m in r.errors %}<li>{{ metric(m) }}</li>{% else %}<li>none</li>{% endfor %}</ul>
<h2>Run statistics</h2>
<ul>
<li>wall seconds: {{ r.run_stats.wall_seconds }}</li>
<li>workers: {{ r.run_stats.workers }} (replaced {{ r.run_stats.workers_replaced }})</li>
{% for k, v in r.run_stats.stage_seconds.items() %}
<li>stage {{ k }}: {{ v }}s</li>{% endfor %}
{% for k, v in r.run_stats.text_seconds_by_family.items() %}
<li>text {{ k }}: {{ v }}s</li>{% endfor %}
{% for s in r.run_stats.slowest_items %}<li>slow item: {{ s }}</li>{% endfor %}
</ul>
<script>
document.querySelectorAll("th.sortable").forEach(function(th){
th.addEventListener("click",function(){
var t=th.closest("table"),b=t.tBodies[0],i=th.cellIndex,
d=th.dataset.dir==="asc"?-1:1;th.dataset.dir=d===1?"asc":"desc";
Array.from(b.rows).sort(function(x,y){
var a=x.cells[i].textContent.trim(),c=y.cells[i].textContent.trim(),
n=parseFloat(a),m=parseFloat(c);
return(isNaN(n)||isNaN(m)?a.localeCompare(c):n-m)*d;
}).forEach(function(r){b.appendChild(r);});});});
</script>
</body></html>
"""

_ENV = Environment(autoescape=True, finalize=lambda v: "—" if v is None else v)


def render(report_json_path: Path, html_path: Path) -> None:
    """Read report.json from disk and write the HTML report."""
    report = Report.model_validate(json.loads(Path(report_json_path).read_text("utf-8")))
    html = _ENV.from_string(_TEMPLATE).render(r=report)
    Path(html_path).write_text(html, encoding="utf-8")
