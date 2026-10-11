# -*- coding: utf-8 -*-
"""Independent QA round 3: cross-check seed-file expectations vs live API DB."""
import json, io, os, collections

SEED = r"C:\Work\美食伴侣\src\FoodMate.Infrastructure\Data\Seed"
OUT = r"C:\Work\美食伴侣\.dsh-team\qa-independent\audit3.txt"
FILES = ["dishes.seed.json","dishes.seed.more.json","dishes.seed.dessert.json",
         "dishes.seed.drink.json","dishes.seed.more2.json","dishes.seed.recipes.json"]
CATEGORY = ["Unknown","Staple","Meat","Vegetable","Soup","Snack","Dessert","Drink","Breakfast","Hotpot"]
CUISINE = ["Other","Sichuan","Cantonese","Hunan","Shandong","Jiangsu","Zhejiang","Fujian",
           "Anhui","Northeast","Northwest","YunnanGuizhou","Japanese","Korean","Western",
           "SoutheastAsian","Snack"]
buf=[]; p=lambda *a: buf.append(" ".join(str(x) for x in a))

data={f: json.loads(io.open(os.path.join(SEED,f),encoding="utf-8-sig").read()) for f in FILES}

# simulate the loader: sorted ordinal by resource name == sorted by file name here
order = sorted(FILES, key=lambda f: "FoodMate.Infrastructure.Data.Seed."+f)
p("simulated load order:", order)
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
p("simulated merged dish count:", len(by))
p("simulated recipes:", sum(1 for d in by.values() if d.get("recipe")))
steps=[len(d["recipe"]["steps"]) for d in by.values() if d.get("recipe")]
p("simulated recipe step min/max:", min(steps), max(steps))
p("simulated recipes with <3 steps:", [(n,len(by[n]['recipe']['steps'])) for n in by if by[n].get('recipe') and len(by[n]['recipe']['steps'])<3])

# step order = 1..n exactly?
bad=[]
for n,d in by.items():
    r=d.get("recipe")
    if not r: continue
    o=sorted(s.get("order") for s in r["steps"])
    if o != list(range(1,len(o)+1)): bad.append((n,o))
p("recipes whose step order != 1..n:", len(bad), bad[:10])

p("")
p("=== category distribution expected from seed (simulated) ===")
c=collections.Counter(CATEGORY[d["category"]] if isinstance(d["category"],int) else d["category"] for d in by.values())
for k,v in c.most_common(): p("  ", v, k)
p("=== cuisine distribution expected from seed (simulated) ===")
c2=collections.Counter(CUISINE[d["cuisine"]] if isinstance(d["cuisine"],int) else d["cuisine"] for d in by.values())
for k,v in c2.most_common(): p("  ", v, k)

p("")
p("=== new-category dishes sourced from which file ===")
for f in FILES[:-1]:
    d6=[x["name"] for x in data[f] if (x.get("category")=="Dessert" or x.get("category")==6)]
    d7=[x["name"] for x in data[f] if (x.get("category")=="Drink" or x.get("category")==7)]
    p("  %-26s dessert=%d drink=%d" % (f, len(d6), len(d7)))

# name list per file for the report
p("")
p("=== dessert file names ===")
p("  " + " / ".join(x["name"] for x in data["dishes.seed.dessert.json"]))
p("=== drink file names ===")
p("  " + " / ".join(x["name"] for x in data["dishes.seed.drink.json"]))
p("=== more2 file names ===")
p("  " + " / ".join(x["name"] for x in data["dishes.seed.more2.json"]))

io.open(OUT,"w",encoding="utf-8").write("\n".join(buf))
print("written", len(buf))
