# -*- coding: utf-8 -*-
"""Round-2 independent static analysis of seed JSON deliverables."""
import json, os, sys, collections

SEED = r"C:\Work\美食伴侣\src\FoodMate.Infrastructure\Data\Seed"
FILES = ["dishes.seed.json", "dishes.seed.more.json", "dishes.seed.dessert.json",
         "dishes.seed.drink.json", "dishes.seed.more2.json", "dishes.seed.recipes.json"]

CAT = {0: "Unknown", 1: "主食", 2: "荤菜", 3: "素菜", 4: "汤羹", 5: "小吃",
       6: "甜点", 7: "饮品", 8: "早餐", 9: "火锅烧烤"}
CUISINE_MAX = 16

def load(f):
    p = os.path.join(SEED, f)
    with open(p, "r", encoding="utf-8-sig") as fh:
        return json.load(fh)

data = {}
for f in FILES:
    p = os.path.join(SEED, f)
    if not os.path.exists(p):
        print("MISSING FILE:", f)
        continue
    d = load(f)
    data[f] = d
    print("%-28s entries=%4d type=%s" % (f, len(d), type(d).__name__))

print()
print("=== per-file recipe count (top-level 'recipe' key present & non-null) ===")
for f, d in data.items():
    n = sum(1 for x in d if x.get("recipe"))
    print("  %-28s withRecipe=%d" % (f, n))

# ---- duplicate names across ALL files (loader merges by name) ----
print()
print("=== name uniqueness across all files ===")
namecount = collections.Counter()
where = collections.defaultdict(list)
for f, d in data.items():
    for i, x in enumerate(d):
        nm = x.get("name")
        if nm is None or not str(nm).strip():
            print("  EMPTY NAME in %s at index %d" % (f, i))
            continue
        namecount[nm] += 1
        where[nm].append(f)
dups = {k: v for k, v in namecount.items() if v > 1}
print("  distinct names = %d ; duplicate name groups = %d" % (len(namecount), len(dups)))
for k, v in list(dups.items())[:50]:
    print("    DUP x%d: %s  in %s" % (v, k, sorted(set(where[k]))))

# ---- base (non-recipe) vs recipes file ----
base_files = [f for f in FILES if f != "dishes.seed.recipes.json"]
base_names = set()
for f in base_files:
    for x in data.get(f, []):
        if x.get("name"):
            base_names.add(x["name"])
rec = data.get("dishes.seed.recipes.json", [])
rec_names = set(x["name"] for x in rec if x.get("name"))
orphans = sorted(rec_names - base_names)
print()
print("=== recipes file name matching ===")
print("  base distinct names      = %d" % len(base_names))
print("  recipes file distinct    = %d" % len(rec_names))
print("  recipe names NOT in base = %d" % len(orphans))
for o in orphans:
    print("    ORPHAN: %s" % o)

# ---- recipe step integrity ----
print()
print("=== recipe step integrity (recipes file) ===")
bad_shape = bad_text = bad_order = dup_order = 0
lt3 = []
dist = collections.Counter()
for x in rec:
    r = x.get("recipe")
    if not r:
        print("  NO RECIPE OBJECT: %s" % x.get("name"))
        continue
    steps = r.get("steps") or []
    dist[len(steps)] += 1
    if len(steps) < 3:
        lt3.append((x["name"], len(steps)))
    orders = []
    for s in steps:
        if not isinstance(s, dict) or "order" not in s or "text" not in s:
            bad_shape += 1
            print("  BAD STEP SHAPE: %s -> %r" % (x["name"], s))
            continue
        if not str(s.get("text") or "").strip():
            bad_text += 1
            print("  EMPTY STEP TEXT: %s order=%s" % (x["name"], s.get("order")))
        orders.append(s["order"])
    if orders and sorted(orders) != list(range(1, len(orders) + 1)):
        bad_order += 1
        print("  ORDER NOT 1..N: %s -> %s" % (x["name"], orders))
    if len(set(orders)) != len(orders):
        dup_order += 1
print("  steps distribution (n_steps: count): %s" % dict(sorted(dist.items())))
print("  entries with steps<3 = %d" % len(lt3))
for n, c in lt3:
    print("    LT3: %s steps=%d" % (n, c))
print("  badShape=%d emptyText=%d orderNot1..N=%d dupOrder=%d" % (bad_shape, bad_text, bad_order, dup_order))
print("  TOTAL steps>=3 in recipes file = %d" % sum(c for k, c in dist.items() if k >= 3))
# also count recipe meta fields
print()
print("  --- recipe meta sanity (recipes file) ---")
miss_serv = sum(1 for x in rec if not (x.get("recipe") or {}).get("servings"))
miss_cook = sum(1 for x in rec if not (x.get("recipe") or {}).get("cookMinutes"))
miss_diff = sum(1 for x in rec if (x.get("recipe") or {}).get("difficulty") is None)
print("  missing servings=%d cookMinutes=%d difficulty=%d" % (miss_serv, miss_cook, miss_diff))

# ---- C7 field completeness on the three NEW files ----
NEW = ["dishes.seed.dessert.json", "dishes.seed.drink.json", "dishes.seed.more2.json"]
REQ = ["cuisine", "category", "spicyLevel", "priceMinCents", "priceMaxCents",
       "calories", "ingredients", "popularity"]
print()
print("=== C7 field completeness (new deliverable files) ===")
for f in NEW:
    d = data.get(f, [])
    missing = collections.Counter()
    ing_lt2 = []
    bad_enum = []
    price_bad = []
    for x in d:
        for k in REQ:
            if k not in x or x[k] is None:
                missing[k] += 1
        ing = x.get("ingredients") or []
        if len(ing) < 2:
            ing_lt2.append((x.get("name"), len(ing)))
        c = x.get("category")
        cu = x.get("cuisine")
        if not isinstance(c, int) or c < 0 or c > 9:
            bad_enum.append((x.get("name"), "category", c))
        if not isinstance(cu, int) or cu < 0 or cu > CUISINE_MAX:
            bad_enum.append((x.get("name"), "cuisine", cu))
        pmin, pmax = x.get("priceMinCents"), x.get("priceMaxCents")
        if isinstance(pmin, int) and isinstance(pmax, int) and pmin > pmax:
            price_bad.append((x.get("name"), pmin, pmax))
    print("  %-28s entries=%3d missingFields=%s ingredients<2=%d badEnum=%d priceMin>Max=%d"
          % (f, len(d), dict(missing) if missing else "none", len(ing_lt2), len(bad_enum), len(price_bad)))
    for t in ing_lt2[:10]:
        print("      ING<2: %s -> %d" % t)
    for t in bad_enum[:10]:
        print("      BADENUM: %s" % (t,))
    for t in price_bad[:10]:
        print("      PRICEBAD: %s" % (t,))

# ---- category distribution of NEW files ----
print()
print("=== category distribution of new files (raw JSON) ===")
for f in NEW:
    d = data.get(f, [])
    cnt = collections.Counter()
    for x in d:
        c = x.get("category")
        cnt[CAT.get(c, "??%s" % c)] += 1
    print("  %-28s %s" % (f, dict(sorted(cnt.items()))))

# ---- grand total raw entries & categories across base files ----
print()
print("=== raw category distribution across base files ===")
cnt = collections.Counter()
for f in base_files:
    for x in data.get(f, []):
        cnt[CAT.get(x.get("category"), "??")] += 1
print("  %s" % dict(sorted(cnt.items())))
print("  raw base entries = %d" % sum(len(data.get(f, [])) for f in base_files))
print("  distinct base names = %d" % len(base_names))
