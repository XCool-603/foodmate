# -*- coding: utf-8 -*-
"""Independent QA: round 2 — enum ranges, step order, near-dup names, outliers."""
import json, io, os, re, collections, unicodedata

SEED = r"C:\Work\美食伴侣\src\FoodMate.Infrastructure\Data\Seed"
OUT = r"C:\Work\美食伴侣\.dsh-team\qa-independent\audit2.txt"
FILES = ["dishes.seed.json", "dishes.seed.more.json", "dishes.seed.dessert.json",
         "dishes.seed.drink.json", "dishes.seed.more2.json", "dishes.seed.recipes.json"]
CUISINE = ["Other","Sichuan","Cantonese","Hunan","Shandong","Jiangsu","Zhejiang","Fujian",
           "Anhui","Northeast","Northwest","YunnanGuizhou","Japanese","Korean","Western",
           "SoutheastAsian","Snack"]
CATEGORY = ["Unknown","Staple","Meat","Vegetable","Soup","Snack","Dessert","Drink","Breakfast","Hotpot"]

buf = []
def p(*a):
    buf.append(" ".join(str(x) for x in a))

data = {}
for f in FILES:
    data[f] = json.loads(io.open(os.path.join(SEED, f), encoding="utf-8-sig").read())

# --- enum values, mixed int/string ---
p("=== enum value types & range ===")
for f, arr in data.items():
    ct = collections.Counter()
    for x in arr:
        if "cuisine" in x: ct[type(x["cuisine"]).__name__] += 1
    p(f, "cuisine types:", dict(ct))
bad = []
for f, arr in data.items():
    for i, x in enumerate(arr):
        for key, names in (("cuisine", CUISINE), ("category", CATEGORY)):
            if key not in x: continue
            v = x[key]
            if isinstance(v, bool): bad.append((f,i,x.get("name"),key,v,"bool"))
            elif isinstance(v, int):
                if not (0 <= v < len(names)): bad.append((f,i,x.get("name"),key,v,"out of range"))
            elif isinstance(v, str):
                if v not in names: bad.append((f,i,x.get("name"),key,v,"unknown name"))
            else: bad.append((f,i,x.get("name"),key,v,"bad type"))
p("bad enum values:", len(bad))
for b in bad: p("   ", b)

# --- step order sanity ---
p("")
p("=== step order sanity (per recipe) ===")
probs = []
for f, arr in data.items():
    for x in arr:
        r = x.get("recipe")
        if not r: continue
        steps = r.get("steps") or []
        orders = [s.get("order") for s in steps]
        if any(o is None for o in orders): probs.append((f,x["name"],"missing order",orders))
        elif len(set(orders)) != len(orders): probs.append((f,x["name"],"duplicate order",orders))
        elif sorted(orders) != list(range(len(orders))): probs.append((f,x["name"],"non 0..n-1",orders))
        for s in steps:
            if not str(s.get("text","")).strip(): probs.append((f,x["name"],"empty step text",orders))
p("step order problems:", len(probs))
for q in probs[:40]: p("   ", q)

# --- recipe field sanity ---
p("")
p("=== recipe field sanity ===")
rprobs = []
for f, arr in data.items():
    for x in arr:
        r = x.get("recipe")
        if not r: continue
        if not r.get("cookMinutes"): rprobs.append((f,x["name"],"cookMinutes",r.get("cookMinutes")))
        if r.get("servings") is None: rprobs.append((f,x["name"],"servings missing"))
        if not r.get("difficulty"): rprobs.append((f,x["name"],"difficulty",r.get("difficulty")))
        if not (r.get("tips") or "").strip(): rprobs.append((f,x["name"],"tips empty"))
p("recipe field problems:", len(rprobs))
for q in rprobs[:40]: p("   ", q)
cm = [x["recipe"]["cookMinutes"] for f,arr in data.items() for x in arr if x.get("recipe")]
p("cookMinutes min/max:", min(cm), max(cm))
diff = collections.Counter(x["recipe"]["difficulty"] for f,arr in data.items() for x in arr if x.get("recipe"))
p("difficulty dist:", sorted(diff.items()))

# --- near-duplicate names ---
p("")
p("=== near-duplicate names (normalized) ===")
def norm(s):
    s = unicodedata.normalize("NFKC", str(s))
    s = re.sub(r"[\s\u3000·・\-—_()（）\[\]【】,，。.、!！?？]+", "", s)
    return s.lower()
allnames = []
for f in FILES:
    if f == "dishes.seed.recipes.json": continue
    for x in data[f]:
        allnames.append((norm(x["name"]), x["name"], f))
byn = collections.defaultdict(list)
for n, orig, f in allnames: byn[n].append((orig,f))
nd = {k:v for k,v in byn.items() if len(v) > 1}
p("normalized-identical groups:", len(nd))
for k,v in nd.items(): p("   ", k, v)
# containment pairs
contain = []
keys = sorted(byn.keys())
for i in range(len(keys)):
    for j in range(len(keys)):
        if i == j: continue
        if keys[i] and keys[i] in keys[j] and len(keys[j]) - len(keys[i]) <= 2:
            contain.append((byn[keys[i]][0][0], byn[keys[j]][0][0]))
p("near-containment pairs (<=2 char diff):", len(contain))
for c in contain: p("   ", c)

# --- outliers ---
p("")
p("=== outliers ===")
rows = []
for f in FILES:
    if f == "dishes.seed.recipes.json": continue
    for x in data[f]:
        rows.append((x.get("calories"), x.get("priceMinCents"), x.get("priceMaxCents"), x.get("name"), f))
rows_sorted = sorted(rows, key=lambda r: (r[0] if r[0] is not None else 0))
p("lowest calories:")
for r in rows_sorted[:8]: p("   ", r)
p("highest calories:")
for r in rows_sorted[-8:]: p("   ", r)
p("price spread == 0 (min==max):", sum(1 for r in rows if r[1] == r[2]))
p("price min>max:", [r for r in rows if r[1] is not None and r[2] is not None and r[1] > r[2]])
ps = sorted(rows, key=lambda r: (r[1] if r[1] is not None else 0))
p("cheapest:", ps[:5]); p("priciest:", ps[-5:])
p("null price/cal:", [r for r in rows if r[1] is None or r[2] is None or r[0] is None])

# --- ingredient quality ---
p("")
p("=== ingredient quality ===")
bad_ing = []
amt = collections.Counter()
for f in FILES:
    if f == "dishes.seed.recipes.json": continue
    for x in data[f]:
        for ing in (x.get("ingredients") or []):
            if not isinstance(ing, dict): bad_ing.append((f,x["name"],"non-dict",ing)); continue
            nm = str(ing.get("name","")).strip()
            if not nm: bad_ing.append((f,x["name"],"empty ingredient name",ing))
            if not str(ing.get("amount","")).strip(): amt[f] += 1
            if re.search(r"(适量|少许)$", nm): bad_ing.append((f,x["name"],"unit in name",nm))
p("ingredient problems:", len(bad_ing))
for b in bad_ing[:20]: p("   ", b)
p("ingredients with empty amount by file:", dict(amt))

# --- duplicate ingredient name within one dish ---
p("")
dup_ing = []
for f in FILES:
    if f == "dishes.seed.recipes.json": continue
    for x in data[f]:
        names = [str(i.get("name","")).strip() for i in (x.get("ingredients") or []) if isinstance(i, dict)]
        c = collections.Counter(names)
        d = {k:v for k,v in c.items() if v > 1}
        if d: dup_ing.append((f, x["name"], d))
p("dishes with duplicate ingredient names:", len(dup_ing))
for q in dup_ing[:20]: p("   ", q)

# --- duplicate tags within one dish ---
dup_tags = []
for f in FILES:
    if f == "dishes.seed.recipes.json": continue
    for x in data[f]:
        t = x.get("tags") or []
        if len(set(t)) != len(t): dup_tags.append((f, x["name"], t))
p("dishes with duplicate tags:", len(dup_tags))
for q in dup_tags[:20]: p("   ", q)

# --- identical descriptions across dishes (copy-paste) ---
desc = collections.defaultdict(list)
for f in FILES:
    if f == "dishes.seed.recipes.json": continue
    for x in data[f]:
        d = (x.get("description") or "").strip()
        if d: desc[d].append((f, x["name"]))
dd = {k:v for k,v in desc.items() if len(v) > 1}
p("")
p("identical descriptions groups:", len(dd))
for k,v in list(dd.items())[:10]: p("   ", v, "->", k[:60])

# --- identical ingredient sets across dishes ---
iset = collections.defaultdict(list)
for f in FILES:
    if f == "dishes.seed.recipes.json": continue
    for x in data[f]:
        key = tuple(sorted(str(i.get("name","")) for i in (x.get("ingredients") or []) if isinstance(i,dict)))
        if key: iset[key].append((f, x["name"]))
ii = {k:v for k,v in iset.items() if len(v) > 1}
p("identical ingredient-set groups:", len(ii))
for k,v in list(ii.items())[:10]: p("   ", v)

io.open(OUT, "w", encoding="utf-8").write("\n".join(buf))
print("written", OUT, len(buf), "lines")
