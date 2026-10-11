# -*- coding: utf-8 -*-
"""Independent QA round 4: simulated-merge vs live DB equality, and the 145-new-dish check."""
import json, io, os, collections

SEED = r"C:\Work\美食伴侣\src\FoodMate.Infrastructure\Data\Seed"
DBF = r"C:\Work\美食伴侣\.dsh-team\qa\dishes.details.json"
OUT = r"C:\Work\美食伴侣\.dsh-team\qa-independent\audit4.txt"
FILES = ["dishes.seed.json","dishes.seed.more.json","dishes.seed.dessert.json",
         "dishes.seed.drink.json","dishes.seed.more2.json","dishes.seed.recipes.json"]
buf=[]; p=lambda *a: buf.append(" ".join(str(x) for x in a))

data={f: json.loads(io.open(os.path.join(SEED,f),encoding="utf-8-sig").read()) for f in FILES}
order = sorted(FILES, key=lambda f: "FoodMate.Infrastructure.Data.Seed."+f)
by={}; seq=[]
for f in order:
    for it in data[f]:
        n=it["name"]
        if n not in by:
            by[n]=json.loads(json.dumps(it)); seq.append(n); continue
        ex=by[n]
        if it.get("recipe") is not None: ex["recipe"]=it["recipe"]
        for k in ("description","imageUrl","priceMinCents","priceMaxCents","calories"):
            if ex.get(k) is None: ex[k]=it.get(k)
        for k in ("aliases","tags","ingredients"):
            if not ex.get(k): ex[k]=it.get(k)

db = json.loads(io.open(DBF, encoding="utf-8-sig").read())
LISTF = r"C:\Work\美食伴侣\.dsh-team\qa\dishes.list.json"
dbl = json.loads(io.open(LISTF, encoding="utf-8-sig").read())
listmap = {d["name"]: d for d in dbl}
for d in db:
    d["popularity"] = listmap[d["name"]]["popularity"]
    d["hasRecipe"] = listmap[d["name"]]["hasRecipe"]
    d["cuisineNum"] = listmap[d["name"]]["cuisine"]
    d["categoryNum"] = listmap[d["name"]]["category"]
p("list dump dishes:", len(dbl))
p("db dump dishes:", len(db))
dbnames = [d["name"] for d in db]
p("db unique names:", len(set(dbnames)))

sim = set(by); live = set(dbnames)
p("in seed but not in DB:", sorted(sim - live))
p("in DB but not in seed:", sorted(live - sim))

# 145 new dishes present?
NEW = [x["name"] for f in ("dishes.seed.dessert.json","dishes.seed.drink.json","dishes.seed.more2.json") for x in data[f]]
p("new-file dish count:", len(NEW), "unique:", len(set(NEW)))
p("new dishes missing from DB:", sorted(set(NEW) - live))
OLD = [x["name"] for f in ("dishes.seed.json","dishes.seed.more.json") for x in data[f]]
p("pre-existing dish count:", len(OLD), "unique:", len(set(OLD)))
p("pre-existing dishes missing from DB:", sorted(set(OLD) - live))

# recipes.json coverage in DB
recnames = set(x["name"] for x in data["dishes.seed.recipes.json"])
dbrec = {d["name"] for d in db if d.get("recipe")}
p("recipes.json names:", len(recnames))
p("recipes.json names without recipe in DB:", sorted(recnames - dbrec))
other_rec = dbrec - recnames
p("DB dishes with recipe NOT from recipes.json:", sorted(other_rec))

# per-field equality between simulated merge and DB for the 145 new dishes
p("")
p("=== field-by-field: simulated merge vs live DB (145 new dishes) ===")
CUISINE = ["Other","Sichuan","Cantonese","Hunan","Shandong","Jiangsu","Zhejiang","Fujian",
           "Anhui","Northeast","Northwest","YunnanGuizhou","Japanese","Korean","Western",
           "SoutheastAsian","Snack"]
CATEGORY = ["Unknown","Staple","Meat","Vegetable","Soup","Snack","Dessert","Drink","Breakfast","Hotpot"]
dbmap = {d["name"]: d for d in db}
mismatch=[]
for n in NEW:
    s = by[n]; d = dbmap[n]
    exp_cui = s["cuisine"] if isinstance(s["cuisine"],str) else CUISINE[s["cuisine"]]
    exp_cat = s["category"] if isinstance(s["category"],str) else CATEGORY[s["category"]]
    pairs = [("cuisine", CUISINE.index(exp_cui), d["cuisineNum"]),
             ("category", CATEGORY.index(exp_cat), d["categoryNum"]),
             ("spicyLevel", s["spicyLevel"], d["spicyLevel"]),
             ("priceMinCents", s["priceMinCents"], d["priceMinCents"]),
             ("priceMaxCents", s["priceMaxCents"], d["priceMaxCents"]),
             ("calories", s["calories"], d["calories"]),
             ("popularity", s["popularity"], d["popularity"]),
             ("ingredientCount", len(s["ingredients"]), len(d["ingredients"]))]
    for k, a, b in pairs:
        if a != b: mismatch.append((n, k, a, b))
p("field mismatches:", len(mismatch))
for m in mismatch[:40]: p("   ", m)

# also check the whole 301
mismatch2=[]
for n, s in by.items():
    d = dbmap.get(n)
    if d is None: continue
    exp_cui = s["cuisine"] if isinstance(s["cuisine"],str) else CUISINE[s["cuisine"]]
    exp_cat = s["category"] if isinstance(s["category"],str) else CATEGORY[s["category"]]
    if CUISINE.index(exp_cui) != d["cuisineNum"]: mismatch2.append((n,"cuisine",exp_cui,d["cuisineNum"]))
    if CATEGORY.index(exp_cat) != d["categoryNum"]: mismatch2.append((n,"category",exp_cat,d["categoryNum"]))
    if s.get("calories") is not None and s["calories"] != d["calories"]: mismatch2.append((n,"calories",s["calories"],d["calories"]))
    if s.get("popularity") is not None and s["popularity"] != d["popularity"]: mismatch2.append((n,"popularity",s["popularity"],d["popularity"]))
    if s.get("priceMinCents") is not None and s["priceMinCents"] != d["priceMinCents"]: mismatch2.append((n,"priceMin",s["priceMinCents"],d["priceMinCents"]))
    if s.get("priceMaxCents") is not None and s["priceMaxCents"] != d["priceMaxCents"]: mismatch2.append((n,"priceMax",s["priceMaxCents"],d["priceMaxCents"]))
    if s.get("spicyLevel") is not None and s["spicyLevel"] != d["spicyLevel"]: mismatch2.append((n,"spicy",s["spicyLevel"],d["spicyLevel"]))
p("")
p("=== whole-seed vs DB mismatches (301 dishes):", len(mismatch2))
for m in mismatch2[:40]: p("   ", m)

# recipe step counts equality for all 124
mm3=[]
for n, s in by.items():
    d = dbmap.get(n)
    if d is None: continue
    se = len((s.get("recipe") or {}).get("steps") or [])
    sd = len((d.get("recipe") or {}).get("steps") or [])
    if se != sd: mm3.append((n, se, sd))
p("recipe step-count mismatches:", len(mm3), mm3[:20])

io.open(OUT,"w",encoding="utf-8").write("\n".join(buf))
print("written", len(buf))
