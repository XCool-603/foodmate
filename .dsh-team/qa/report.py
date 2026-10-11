# -*- coding: utf-8 -*-
import json, os, collections, io, sys

SEED = r"C:\Work\美食伴侣\src\FoodMate.Infrastructure\Data\Seed"
OUT  = r"C:\Work\美食伴侣\.dsh-team\qa\seed_report.txt"
FILES = ["dishes.seed.json","dishes.seed.more.json","dishes.seed.dessert.json",
         "dishes.seed.drink.json","dishes.seed.more2.json","dishes.seed.recipes.json"]
LOAD_ORDER = ["dishes.seed.dessert.json","dishes.seed.drink.json","dishes.seed.json",
              "dishes.seed.more.json","dishes.seed.more2.json","dishes.seed.recipes.json"]
NEW = {"dishes.seed.dessert.json":40,"dishes.seed.drink.json":35,"dishes.seed.more2.json":70}
CUISINE = {"Other":0,"Sichuan":1,"Cantonese":2,"Hunan":3,"Shandong":4,"Jiangsu":5,"Zhejiang":6,
           "Fujian":7,"Anhui":8,"Northeast":9,"Northwest":10,"YunnanGuizhou":11,"Japanese":12,
           "Korean":13,"Western":14,"SoutheastAsian":15,"Snack":16}
CATEGORY = {"Unknown":0,"Staple":1,"Meat":2,"Vegetable":3,"Soup":4,"Snack":5,
            "Dessert":6,"Drink":7,"Breakfast":8,"Hotpot":9}

R = io.StringIO()
def p(*a): print(*a, file=R)

data = {}
for f in FILES:
    with open(os.path.join(SEED,f), encoding="utf-8") as fh:
        data[f] = json.load(fh)

p("="*78); p("A. RAW PER-FILE COUNTS  (delivered claim in parentheses)"); p("="*78)
for f in FILES:
    claim = NEW.get(f)
    tail = f"   <-- new-batch claim {claim}" if claim else ""
    ok = "" if claim is None else ("  MATCH" if len(data[f])==claim else f"  *** MISMATCH claim={claim} actual={len(data[f])}")
    p(f"  {f:30s} raw={len(data[f]):4d}{ok}{tail}")

p(""); p("="*78); p("B. DUPLICATE NAMES"); p("="*78)
for f in FILES:
    c = collections.Counter((d.get("name") or "").strip().lower() for d in data[f])
    d2 = {k:v for k,v in c.items() if v>1}
    p(f"  {f:30s} intra-file dups: {d2 if d2 else 'NONE'}")
owner = collections.defaultdict(list)
for f in LOAD_ORDER:
    for d in data[f]:
        owner[(d.get("name") or "").strip().lower()].append(f)
cross = {k:v for k,v in owner.items() if len(v)>1}
p(f"  cross-file shared names: {len(cross)}")
nonrecipe = {k:v for k,v in cross.items() if any(x!="dishes.seed.recipes.json" for x in v)}
p(f"  shared names NOT involving recipes.json: {len(nonrecipe)}")
for k,v in sorted(nonrecipe.items()): p(f"     *** {k} <- {v}")
p(f"  (all others are the intended dish<->recipe pairing with dishes.seed.recipes.json)")

# ---- real conflicts only (both sides explicitly set the field)
p(""); p("="*78); p("C. MERGE CONFLICTS (only where >=2 files EXPLICITLY set the field)"); p("="*78)
MERGED_FIELDS = ("cuisine","category","spicyLevel","mealTimes","seasons","canMakeAtHome","popularity")
NOT_MERGED = ("cuisine","category","spicyLevel","mealTimes","seasons","canMakeAtHome","popularity")
real_conf = []
for k, srcs in sorted(cross.items()):
    for fld in MERGED_FIELDS:
        seen = []
        for f in srcs:
            for it in data[f]:
                if (it.get("name") or "").strip().lower()==k and fld in it:
                    seen.append((f, it[fld]))
        if len(seen)>1 and len({str(v) for _,v in seen})>1:
            real_conf.append((k,fld,seen))
if real_conf:
    p(f"  !! {len(real_conf)} REAL conflicts on never-merged fields (later value SILENTLY DISCARDED):")
    for k,fld,seen in real_conf: p(f"     {k} / {fld}: {seen}")
else:
    p("  NONE - no file tries to override cuisine/category/spicyLevel/mealTimes/seasons/")
    p("         canMakeAtHome/popularity of a dish already defined earlier.")
p("  Note: dishes.seed.recipes.json carries ONLY name+recipe, so it never conflicts.")

# ---- merged simulation
p(""); p("="*78); p("D. MERGED SIMULATION (mirrors DishSeedLoader.cs)"); p("="*78)
by={}; order=[]
for f in LOAD_ORDER:
    for it in data[f]:
        n=(it.get("name") or "").strip()
        if not n: continue
        key=n.lower()
        if key not in by:
            by[key]=dict(it); by[key]["__from"]=[f]; order.append(key); continue
        ex=by[key]; ex["__from"].append(f)
        if it.get("recipe") is not None: ex["recipe"]=it["recipe"]
        for fld in ("description","imageUrl","priceMinCents","priceMaxCents","calories"):
            if ex.get(fld) is None: ex[fld]=it.get(fld)
        for fld in ("aliases","tags","ingredients"):
            if not ex.get(fld): ex[fld]=it.get(fld)
p(f"  raw rows total      = {sum(len(data[f]) for f in FILES)}")
p(f"  MERGED UNIQUE DISHES= {len(order)}      (health/ready reported 301)")
p(f"  rows collapsed      = {sum(len(data[f]) for f in FILES)-len(order)}")

# ---- C7: the 145 new dishes
p(""); p("="*78); p("E. C7 FIELD COMPLETENESS for the 145 NEW dishes"); p("="*78)
REQ = ["cuisine","category","spicyLevel","priceMinCents","priceMaxCents","calories","ingredients","popularity"]
bad=[]
for f, claim in NEW.items():
    miss=[]; short=[]
    for i,d in enumerate(data[f],1):
        m=[k for k in REQ if k not in d]
        if m: miss.append((i,d.get("name"),m))
        ing=d.get("ingredients") or []
        if len(ing)<2: short.append((i,d.get("name"),len(ing)))
    p(f"  {f} (n={len(data[f])})")
    p(f"     missing required fields : {len(miss)}")
    for x in miss[:15]: p(f"        *** #{x[0]} {x[1]}: missing {x[2]}")
    p(f"     ingredients.length < 2  : {len(short)}")
    for x in short[:15]: p(f"        *** #{x[0]} {x[1]}: len={x[2]}")
    bad += [(f,)+x for x in miss+short]

# ---- enum legality across ALL files
p(""); p("="*78); p("F. ENUM LEGALITY (all 6 files)"); p("="*78)
for f in FILES:
    bc=[]; bcat=[]
    for i,d in enumerate(data[f],1):
        c=d.get("cuisine")
        if c is not None and c not in CUISINE: bc.append((i,d.get("name"),c))
        cat=d.get("category")
        if cat is not None and cat not in CATEGORY: bcat.append((i,d.get("name"),cat))
    p(f"  {f:30s} bad cuisine={len(bc)} bad category={len(bcat)}")
    for x in bc[:10]: p(f"     *** cuisine {x}")
    for x in bcat[:10]: p(f"     *** category {x}")

# ---- data quality
p(""); p("="*78); p("G. DATA QUALITY SCAN (all files, pre-merge)"); p("="*78)
issues=collections.defaultdict(list)
PLACEHOLDER = ("菜品","测试","test","xxx","待定","tbd","占位","placeholder","foo","bar","示例")
for f in FILES:
    for i,d in enumerate(data[f],1):
        n=(d.get("name") or "").strip()
        if not n: issues["empty name"].append((f,i,d.get("name")))
        low=n.lower()
        for ph in PLACEHOLDER:
            if low.startswith(ph.lower()) or low==ph.lower():
                issues[f"placeholder-ish name '{ph}'"].append((f,i,n))
        if len(n)<=1: issues["name length <=1"].append((f,i,n))
        pmin,pmax=d.get("priceMinCents"),d.get("priceMaxCents")
        if pmin is not None and pmax is not None and pmin>pmax:
            issues["priceMin > priceMax"].append((f,i,n,pmin,pmax))
        for lbl,v in (("priceMinCents",pmin),("priceMaxCents",pmax)):
            if v is not None and (v<=0 or v>200000):
                issues[f"{lbl} out of sane range"].append((f,i,n,v))
        cal=d.get("calories")
        if cal is not None and (cal<=0 or cal>3000):
            issues["calories <=0 or >3000"].append((f,i,n,cal))
        sl=d.get("spicyLevel")
        if sl is not None and (sl<0 or sl>5):
            issues["spicyLevel outside 0..5"].append((f,i,n,sl))
        pop=d.get("popularity")
        if pop is not None and (pop<0 or pop>100):
            issues["popularity outside 0..100"].append((f,i,n,pop))
        ing=d.get("ingredients")
        if ing is not None:
            for j,one in enumerate(ing):
                if not isinstance(one,dict) or not (one.get("name") or "").strip():
                    issues["ingredient without name"].append((f,i,n,j,one))
        if d.get("recipe") is not None:
            st=d["recipe"].get("steps") or []
            if len(st)<3: issues["recipe with <3 steps"].append((f,i,n,len(st)))
if issues:
    for k,v in sorted(issues.items()):
        p(f"  {k}: {len(v)}")
        for x in v[:20]: p(f"     *** {x}")
else:
    p("  NONE")

# ---- recipe coverage
p(""); p("="*78); p("H. RECIPE COVERAGE in seed (post-merge)"); p("="*78)
withr=[k for k in order if by[k].get("recipe")]
p(f"  dishes with a recipe (merged): {len(withr)}")
steps=[(k,len(by[k]['recipe'].get('steps') or [])) for k in withr]
p(f"  min steps={min(s for _,s in steps)}  max steps={max(s for _,s in steps)}")
p(f"  recipes with <3 steps: {[(k,s) for k,s in steps if s<3]}")
from collections import Counter
p(f"  step-count histogram: {dict(sorted(Counter(s for _,s in steps).items()))}")

open(OUT,"w",encoding="utf-8").write(R.getvalue())
print("WROTE", OUT, len(R.getvalue()), "chars")
