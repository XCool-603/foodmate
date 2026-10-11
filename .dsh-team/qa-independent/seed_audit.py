# -*- coding: utf-8 -*-
"""Independent QA analysis of dish seed files. Written by the independent verifier."""
import json, io, os, re, collections

SEED = r"C:\Work\美食伴侣\src\FoodMate.Infrastructure\Data\Seed"
FILES = ["dishes.seed.json", "dishes.seed.more.json", "dishes.seed.dessert.json",
         "dishes.seed.drink.json", "dishes.seed.more2.json", "dishes.seed.recipes.json"]

CUISINE = ["Other","Sichuan","Cantonese","Hunan","Shandong","Jiangsu","Zhejiang","Fujian",
           "Anhui","Northeast","Northwest","YunnanGuizhou","Japanese","Korean","Western",
           "SoutheastAsian","Snack"]
CATEGORY = ["Unknown","Staple","Meat","Vegetable","Soup","Snack","Dessert","Drink","Breakfast","Hotpot"]

data = {}
order = []
print("=== 1. file parse / entry counts ===")
for f in FILES:
    p = os.path.join(SEED, f)
    raw = io.open(p, encoding="utf-8-sig").read()
    try:
        arr = json.loads(raw)
        data[f] = arr
        print("%-28s OK   entries=%d" % (f, len(arr)))
    except Exception as e:
        print("%-28s PARSE FAIL: %s" % (f, e))

print("\n=== 2. within-file duplicate names ===")
anydup = False
for f, arr in data.items():
    c = collections.Counter(x.get("name","") for x in arr)
    d = {k:v for k,v in c.items() if v > 1}
    if d:
        anydup = True
        print(f, d)
if not anydup: print("none")

print("\n=== 3. cross-file duplicate names (case-insensitive) ===")
owner = collections.defaultdict(list)
for f, arr in data.items():
    for x in arr:
        owner[str(x.get("name","")).strip().lower()].append(f)
dups = {k:v for k,v in owner.items() if len(v) > 1}
print("count:", len(dups))
mat = collections.Counter()
for k, v in dups.items():
    mat[tuple(sorted(set(v)))] += 1
for k, v in sorted(mat.items(), key=lambda t: -t[1]):
    print("  %4d  files=%s" % (v, k))
show = [k for k,v in dups.items() if not (len(set(v))==2 and "dishes.seed.recipes.json" in v)]
print("  dups NOT purely (other + recipes.json):", len(show))
for k in sorted(show)[:60]:
    print("     ", k, dups[k])

others = set()
for f in FILES:
    if f == "dishes.seed.recipes.json": continue
    for x in data[f]:
        others.add(str(x.get("name","")).strip().lower())
recs = data.get("dishes.seed.recipes.json", [])
recnames = set(str(x.get("name","")).strip().lower() for x in recs)
print("\nrecipes.json entries:", len(recs), " distinct:", len(recnames))
print("recipes.json names NOT present in other files:", len(recnames - others))
for n in sorted(recnames - others): print("   ORPHAN:", n)
print("non-recipe-file names with NO recipe entry:", len(others - recnames))
print("union of non-recipe files:", len(others))

print("\n=== 4. placeholder / fabricated-looking names ===")
pat = re.compile(r"(菜品\s*\d+|测试|test|todo|tbd|待定|xxx|占位|示例|placeholder|dish\s*\d+|^菜\d+$)", re.I)
hits = []
for f, arr in data.items():
    for i, x in enumerate(arr):
        n = str(x.get("name",""))
        if pat.search(n): hits.append((f, i, n))
print("hits:", len(hits))
for h in hits: print("  ", h)
short = []
for f, arr in data.items():
    for i, x in enumerate(arr):
        n = str(x.get("name","")).strip()
        if len(n) <= 1: short.append((f, i, n))
print("names of length<=1:", len(short), short[:20])

print("\n=== 5. enum validity ===")
bad = []
for f, arr in data.items():
    for i, x in enumerate(arr):
        if "cuisine" in x and x["cuisine"] not in CUISINE:
            bad.append((f, i, x.get("name"), "cuisine", x["cuisine"]))
        if "category" in x and x["category"] not in CATEGORY:
            bad.append((f, i, x.get("name"), "category", x["category"]))
print("bad enum refs:", len(bad))
for b in bad: print("  ", b)

print("\n=== 6. numeric anomalies ===")
anom = []
for f, arr in data.items():
    for i, x in enumerate(arr):
        n = x.get("name")
        pmin, pmax = x.get("priceMinCents"), x.get("priceMaxCents")
        if pmin is not None and pmax is not None and pmin > pmax:
            anom.append((f, i, n, "priceMin>priceMax", pmin, pmax))
        if pmin is not None and pmin <= 0: anom.append((f, i, n, "priceMin<=0", pmin))
        if pmax is not None and pmax <= 0: anom.append((f, i, n, "priceMax<=0", pmax))
        cal = x.get("calories")
        if cal is not None and (cal <= 0 or cal > 2000):
            anom.append((f, i, n, "calories", cal))
        sp = x.get("spicyLevel")
        if sp is not None and (sp < 0 or sp > 5):
            anom.append((f, i, n, "spicyLevel", sp))
        pop = x.get("popularity")
        if pop is not None and (pop < 0 or pop > 100):
            anom.append((f, i, n, "popularity", pop))
print("anomalies:", len(anom))
for a in anom: print("  ", a)

print("\n=== 7. distributions ===")
for key in ["spicyLevel", "popularity"]:
    c = collections.Counter()
    for f, arr in data.items():
        for x in arr:
            if x.get(key) is not None: c[x[key]] += 1
    print(key, sorted(c.items()))
cals = [x["calories"] for f,arr in data.items() for x in arr if x.get("calories") is not None]
print("calories min/max:", min(cals), max(cals), "n=", len(cals))
pops = [x["popularity"] for f,arr in data.items() for x in arr if x.get("popularity") is not None]
print("popularity min/max:", min(pops), max(pops), "n=", len(pops))

print("\n=== 8. C7: field completeness of the 145 new dishes ===")
REQ = ["cuisine","category","spicyLevel","priceMinCents","priceMaxCents","calories","ingredients","popularity"]
viol = []
for f in ["dishes.seed.dessert.json","dishes.seed.drink.json","dishes.seed.more2.json"]:
    arr = data.get(f, [])
    for i, x in enumerate(arr):
        for k in REQ:
            if k not in x or x[k] is None:
                viol.append((f, i, x.get("name"), "MISSING:"+k))
        ing = x.get("ingredients")
        if not isinstance(ing, list):
            viol.append((f, i, x.get("name"), "ingredients not list"))
        elif len(ing) < 2:
            viol.append((f, i, x.get("name"), "ingredients<2", len(ing)))
    print("%-28s n=%d" % (f, len(arr)))
print("C7 violations:", len(viol))
for v in viol[:60]: print("  ", v)

print("\n=== 9. ingredient structure ===")
ks = collections.Counter()
for f, arr in data.items():
    for x in arr:
        for ing in (x.get("ingredients") or []):
            if isinstance(ing, dict): ks[tuple(sorted(ing.keys()))] += 1
            else: ks[("NON-DICT", type(ing).__name__)] += 1
for k,v in ks.most_common(10): print("  ", v, k)

print("\n=== 10. recipes inside seed files ===")
for f, arr in data.items():
    recs_ = [x for x in arr if x.get("recipe")]
    if not recs_: continue
    lens = sorted(len((x["recipe"].get("steps") or [])) for x in recs_)
    print("%-28s recipes=%d steps min=%s max=%s" % (f, len(recs_), lens[0], lens[-1]))
    for x in recs_:
        if len(x["recipe"].get("steps") or []) < 3:
            print("     <3 steps:", x.get("name"), len(x["recipe"]["steps"]))

print("\n=== 11. top-level key usage per file ===")
for f, arr in data.items():
    c = collections.Counter()
    for x in arr: c.update(x.keys())
    print(f)
    print("   ", dict(c))

print("\n=== 12. step key shapes ===")
sk = collections.Counter()
for f, arr in data.items():
    for x in arr:
        for s in ((x.get("recipe") or {}).get("steps") or []):
            sk[tuple(sorted(s.keys()))] += 1
print(sk.most_common(5))

print("\n=== 13. duplicate recipe text across dishes (copy-paste check) ===")
sig = collections.defaultdict(list)
for f, arr in data.items():
    for x in arr:
        r = x.get("recipe")
        if not r: continue
        txt = "|".join(str((s or {}).get("text","")) for s in (r.get("steps") or []))
        sig[txt].append((f, x.get("name")))
dupsig = {k:v for k,v in sig.items() if len(v) > 1}
print("identical step-text groups:", len(dupsig))
for k,v in list(dupsig.items())[:10]:
    print("   ", v, "->", k[:80])
