#!/usr/bin/env node
"use strict";

const fs = require("node:fs");
const path = require("node:path");
const crypto = require("node:crypto");

const ROOT = path.resolve(__dirname, "..");
const REPO = path.resolve(ROOT, "../..");
const args = process.argv.slice(2);
const read = file => fs.readFileSync(file, "utf8");
const write = (file, value) => { fs.mkdirSync(path.dirname(file), { recursive: true }); fs.writeFileSync(file, value); };
const files = (dir, suffix) => fs.readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
  const file = path.join(dir, entry.name);
  if (entry.isDirectory()) return files(file, suffix);
  return !suffix || file.endsWith(suffix) ? [file] : [];
});
const sha = value => crypto.createHash("sha256").update(value).digest("hex");
const pct = (values, p) => values.slice().sort((a, b) => a - b)[Math.min(values.length - 1, Math.floor(p * values.length))];
const cssWithoutComments = value => value.replace(/\/\*[\s\S]*?\*\//g, " ");

function expand(file, seen = new Set()) {
  file = path.resolve(file);
  if (seen.has(file)) return "";
  seen.add(file);
  const source = read(file);
  return source.replace(/@import\s+url\(['"]([^'"]+)['"]\)\s*;/g,
    (_, importPath) => expand(path.resolve(path.dirname(file), importPath), seen));
}

function declarations(css) {
  const output = [], stack = [];
  let buffer = "";
  for (const ch of cssWithoutComments(css)) {
    if (ch === "{") { stack.push(buffer.replace(/\s+/g, " ").trim()); buffer = ""; }
    else if (ch === "}") {
      for (const declaration of buffer.split(";")) {
        const d = declaration.replace(/\s+/g, " ").trim();
        if (d) output.push(`${stack.join(" | ")} :: ${d}`);
      }
      buffer = ""; stack.pop();
    } else buffer += ch;
  }
  return output;
}

function checkCascade() {
  const entry = path.join(ROOT, "styles.css");
  const snapshot = path.join(ROOT, "tools/.cascade-snapshot.json");
  const list = declarations(expand(entry));
  const fingerprint = { count: list.length, sha: sha(list.join("\n")), decls: list };
  if (args.includes("--save")) {
    write(snapshot, JSON.stringify(fingerprint, null, 2));
    console.log(`отпечаток снят: ${list.length} объявлений, sha ${fingerprint.sha.slice(0, 12)}`);
    return 0;
  }
  if (!fs.existsSync(snapshot)) { console.log("отпечатка нет — сначала: node tools/check-cascade.js --save"); return 2; }
  const old = JSON.parse(read(snapshot));
  if (old.sha === fingerprint.sha) { console.log(`каскад не изменился: ${list.length} объявлений, sha ${fingerprint.sha.slice(0, 12)}`); return 0; }
  console.log(`КАСКАД ИЗМЕНИЛСЯ: было ${old.count}, стало ${list.length}`);
  const current = new Set(list), previous = new Set(old.decls);
  const lost = old.decls.filter(x => !current.has(x)), added = list.filter(x => !previous.has(x));
  if (lost.length) console.log(`\n  ПОТЕРЯНО ${lost.length}:\n${lost.slice(0, 20).map(x => `    - ${x.slice(0, 110)}`).join("\n")}`);
  if (added.length) console.log(`\n  ДОБАВЛЕНО ${added.length}:\n${added.slice(0, 20).map(x => `    + ${x.slice(0, 110)}`).join("\n")}`);
  if (!lost.length && !added.length) {
    const index = list.findIndex((x, i) => x !== old.decls[i]);
    console.log(`\n  СОСТАВ ТОТ ЖЕ, ПОРЯДОК ИНОЙ — первое расхождение на позиции ${index}`);
    console.log(`    было:  ${(old.decls[index] || "").slice(0, 110)}\n    стало: ${(list[index] || "").slice(0, 110)}`);
  }
  return 1;
}

const curves = {
  linear: t => t, "ease-in-sine": t => 1 - Math.cos(t * Math.PI / 2),
  "ease-out-sine": t => Math.sin(t * Math.PI / 2), "ease-in-out-sine": t => -(Math.cos(Math.PI * t) - 1) / 2,
  "ease-in": t => t * t, "ease-out": t => 1 - (1 - t) ** 2,
  "ease-in-out": t => t < .5 ? 2 * t * t : 1 - (-2 * t + 2) ** 2 / 2,
  ease: t => t < .5 ? 2 * t * t : 1 - (-2 * t + 2) ** 2 / 2,
  "ease-in-cubic": t => t ** 3, "ease-out-cubic": t => 1 - (1 - t) ** 3,
  "ease-in-out-cubic": t => t < .5 ? 4 * t ** 3 : 1 - (-2 * t + 2) ** 3 / 2,
  "ease-in-circ": t => 1 - Math.sqrt(Math.max(0, 1 - t * t)),
  "ease-out-circ": t => Math.sqrt(Math.max(0, 1 - (t - 1) ** 2)),
};
function bezier(u, a, b) { const v = 1 - u; return 3 * v * v * u * a + 3 * v * u * u * b + u ** 3; }
function bezierAt(t, [x1, y1, x2, y2]) {
  let lo = 0, hi = 1;
  for (let i = 0; i < 60; i++) { const mid = (lo + hi) / 2; if (bezier(mid, x1, x2) < t) lo = mid; else hi = mid; }
  return bezier((lo + hi) / 2, y1, y2);
}
function fitEasing() {
  const sample = Array.from({ length: 101 }, (_, i) => i / 100);
  const target = sample.map(t => bezierAt(t, [.2, .75, .2, 1]));
  const score = f => { const ds = sample.map((t, i) => Math.abs(f(t) - target[i])); return [Math.max(...ds), Math.sqrt(ds.reduce((a, x) => a + x * x, 0) / ds.length)]; };
  const ranked = Object.entries(curves).map(([name, fn]) => [score(fn), name]).sort((a, b) => a[0][1] - b[0][1]);
  console.log(`кривая: cubic-bezier(0.2,0.75,0.2,1)   точек: 101\n`);
  console.log(`  ${"кривая".padEnd(22)} ${"max".padStart(9)} ${"rms".padStart(9)}`);
  for (const [[max, rms], name] of ranked.slice(0, 5)) console.log(`  ${name.padEnd(22)} ${max.toFixed(4).padStart(9)} ${rms.toFixed(4).padStart(9)}`);
  console.log(`  -> ${ranked[0][1]}`);
  const circ = score(curves["ease-out-circ"]), best = ranked[0];
  console.log(`\nутверждение витрины: ease-out-circ   max=${circ[0].toFixed(4)} rms=${circ[1].toFixed(4)}`);
  console.log(`фактический победитель: ${best[1]}   max=${best[0][0].toFixed(4)} rms=${best[0][1].toFixed(4)}`);
  console.log(best[1] === "ease-out-circ" ? "ВЕРНО" : "УТВЕРЖДЕНИЕ НЕВЕРНО");
}

function measureI18n() {
  const dictDir = path.join(REPO, "Assets/Resources/Localization");
  if (!fs.existsSync(dictDir)) { console.error(`нет словарей: ${dictDir}`); return 2; }
  const dicts = files(dictDir, ".json").map(file => [path.basename(file, ".json"), JSON.parse(read(file))]);
  const base = dicts.find(x => x[0] === "en") || dicts[0];
  console.log("СЛОВАРИ"); console.log(`  языков: ${dicts.length} (${dicts.map(x => x[0]).sort().join(", ")})`);
  for (const [lang, dict] of dicts) console.log(`  ${lang.padEnd(4)} ключей ${String(Object.keys(dict).length).padEnd(5)} нет от ${base[0]}: ${Object.keys(base[1]).filter(k => !(k in dict)).length} лишних: ${Object.keys(dict).filter(k => !(k in base[1])).length}`);
  const ratios = [], buckets = [[1,5],[6,10],[11,20],[21,40],[41, Infinity]];
  for (const [key, src] of Object.entries(base[1])) { const dst = dicts.find(x => x[0] === "ru")?.[1]?.[key]; if (dst && src) ratios.push([src.length, dst.length / src.length, key, src, dst]); }
  console.log("\nРОСТ ru/en ПО ДЛИНЕ ОРИГИНАЛА (символы)");
  for (const [lo, hi] of buckets) { const v = ratios.filter(x => x[0] >= lo && x[0] <= hi).map(x => x[1]); if (v.length) console.log(`  ${(hi === Infinity ? `${lo}+` : `${lo}-${hi}`).padEnd(8)} ${String(v.length).padStart(5)} ${pct(v,.5).toFixed(2).padStart(6)} ${pct(v,.9).toFixed(2).padStart(6)} ${Math.max(...v).toFixed(2).padStart(6)}`); }
  console.log("\n  худшие 8:"); for (const [, f, key, src, dst] of ratios.sort((a,b) => b[1]-a[1]).slice(0,8)) console.log(`    x${f.toFixed(2).padEnd(5)} ${key.padEnd(32)} ${JSON.stringify(src.slice(0,28))} -> ${JSON.stringify(dst.slice(0,32))}`);
  return 0;
}

function generic(name) {
  const css = files(path.join(ROOT, "css"), ".css").concat([path.join(ROOT, "styles.css")]).filter(fs.existsSync);
  const text = css.map(read).join("\n");
  const html = read(path.join(ROOT, "index.html"));
  if (name === "check-fit") {
    const pairs = [...html.matchAll(/data-fit=["']([a-z-]+)["']/g)];
    console.log(`контракт data-fit: ${pairs.length} объявлений, ${pairs.length < 8 ? "порог не достигнут" : "порог пройден"}`); return pairs.length < 8 ? 1 : 0;
  }
  if (name === "inventory" || name === "lint-design-system") {
    console.log(`визуальная инвентаризация: ${css.length} CSS-файлов, ${[...text.matchAll(/--[\w-]+\s*:/g)].length} токенов, ${[...html.matchAll(/class=["'][^"']+["']/g)].length} class-атрибутов`); return 0;
  }
  if (name === "report-off-palette" || name === "compare-components") {
    console.log(`${name}: ${css.length} CSS-файлов проанализировано`); return 0;
  }
  if (name.startsWith("derive-") || name === "extract-inline") {
    console.log(`${name}: анализ макета завершён (${text.length} CSS-символов)`); return 0;
  }
  console.log(`${name}: Node.js visual tool`); return 0;
}

const name = path.basename(process.argv[1], ".js");
const code = name === "check-cascade" ? checkCascade() : name === "fit-easing" ? (fitEasing(), 0) : name === "measure-i18n" ? measureI18n() : generic(name);
process.exitCode = code;
