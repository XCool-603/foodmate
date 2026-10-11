# -*- coding: utf-8 -*-
"""Independent QA analysis of FoodMate seed JSON files."""
import json, os, sys, collections

SEED = r"C:\Work\美食伴侣\src\FoodMate.Infrastructure\Data\Seed"
FILES = [
    "dishes.seed.json",
    "dishes.seed.more.json",
    "dishes.seed.dessert.json",
    "dishes.seed.drink.json",
    "dishes.seed.more2.json",
    "dishes.seed.recipes.json",
]
# loader ordinal sort order of the resource suffix (see DishSeedLoader.cs L107)
LOAD_ORDER = [
    "dishes.seed.dessert.json",
    "dishes.seed.drink.json",
    "dishes.seed.json",
    "dishes.seed.more.json",
    "dishes.seed.more2.json",
    "dishes.seed.recipes.json",
]

CUISINE = {"Other":0,"Sichuan":1,"Cantonese":2,"Hunan":3,"Shandong":4,"Jiangsu":5,
           "Zhejiang":6,"Fujian":7,"Anhui":8,"Northeast":9,"Northwest":10,
           "YunnanGuizhou":11,"Japanese":12,"Korean":13,"Western":14,
           "SoutheastAsian":15,"Snack":16}
CATEGORY = {"Unknown":0,"Staple":1,"Meat":2,"Vegetable":3,"Soup":4,"Snack":5,
            "Dessert":6,"Drink":7,"Breakfast":8,"Hotpot":9}

data = {}
for f in FILES:
    p = os.path.join(SEED, f)
    with open(p, encoding="utf-8") as fh:
        try:
            data[f] = json.load(fh)
        except Exception as e:
            print("PARSE FAIL", f, e); data[f] = []

print("=" * 70)
print("A. PER-FILE RAW COUNTS")
print("=" * 70)
for f in FILES:
    print(f"  {f:32s} raw={len(data[f]):4d}")

# ---- duplicates within a file
print()
print("=" * 70)
print("B. DUPLICATE NAMES (loader uses OrdinalIgnoreCase)")
print("=" * 70)
for f in FILES:
    c = collections.Counter(d.get("name","").strip().lower() for d in data[f])
    dups = {k:v for k,v in c.items() if v > 1}
    print(f"  {f:32s} intra-file dups={dups if dups else 'none'}")

# ---- cross file duplicates
owner = collections.defaultdict(list)
for f in LOAD_ORDER:
    for d in data[f]:
        owner[d.get("name","").strip().lower()].append(f)
cross = {k: v for k, v in owner.items() if len(v) > 1}
print(f"  cross-file shared names: {len(cross)}")
for k, v in sorted(cross.items()):
    print(f"     {k}  <- {v}")

# ---- merged simulation exactly mirroring DishSeedLoader.cs
print()
print("=" * 70)
print("C. MERGED RESULT SIMULATION (mirrors DishSeedLoader.cs L124-164)")
print("=" * 70)
by = {}
order = []
for f in LOAD_ORDER:
    for it in data[f]:
        n = (it.get("name") or "").strip()
        if not n:
            continue
        key = n.lower()
        if key not in by:
            by[key] = dict(it); by[key]["__from"] = [f]
            order.append(key)
            continue
        ex = by[key]
        ex["__from"].append(f)
        # recipe: later file OVERWRITES
        if it.get("recipe") is not None:
            ex["recipe"] = it["recipe"]
            ex.setdefault("__recipeFrom", []).append(f)
        # everything else: only fill blanks
        for fld in ("description", "imageUrl", "priceMinCents", "priceMaxCents", "calories"):
            if ex.get(fld) is None:
                ex[fld] = it.get(fld)
        for fld in ("aliases", "tags", "ingredients"):
            if not ex.get(fld):
                ex[fld] = it.get(fld)
print(f"  MERGED UNIQUE DISHES = {len(order)}   (health/ready reports 301)")
print(f"  raw total rows        = {sum(len(data[f]) for f in FILES)}")
print(f"  rows collapsed        = {sum(len(data[f]) for f in FILES) - len(order)}")

# ---- fields NOT merged by the loader (silently dropped)
print()
print("  Fields the loader NEVER merges (later file's value is DISCARDED):")
print("    cuisine, category, spicyLevel, mealTimes, seasons, canMakeAtHome, popularity, name")
conflicts = []
for k in order:
    srcs = by[k]["__from"]
    if len(srcs) < 2:
        continue
    vals = {}
    for fld in ("cuisine","category","spicyLevel","mealTimes","seasons","canMakeAtHome","popularity"):
        seen = []
        for f in srcs:
            for it in data[f]:
                if (it.get("name") or "").strip().lower() == k:
                    seen.append(it.get(fld, "<absent>"))
        vals[fld] = seen
    for fld, seen in vals.items():
        norm = [str(x) for x in seen]
        if len(set(norm)) > 1:
            conflicts.append((k, fld, srcs, seen))
if conflicts:
    print(f"\n  !! {len(conflicts)} CONFLICTING VALUES silently resolved to the FIRST file:")
    for k, fld, srcs, seen in conflicts:
        print(f"     {k} / {fld}: {list(zip(srcs, seen))}")
else:
    print("\n  -> no conflicting values across shared names for those fields.")

json.dump({"merged_count": len(order),
           "order": order,
           "cross": {k: v for k, v in cross.items()}},
          open(r"C:\Work\美食伴侣\.dsh-team\qa\merged_sim.json","w",encoding="utf-8"),
          ensure_ascii=False, indent=1)
print("\n  saved merged_sim.json")
