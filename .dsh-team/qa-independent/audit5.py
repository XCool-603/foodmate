# -*- coding: utf-8 -*-
"""Independent QA round 5: masks, imageUrl, aliases, sample content."""
import json, io, os, collections

SEED = r"C:\Work\美食伴侣\src\FoodMate.Infrastructure\Data\Seed"
OUT = r"C:\Work\美食伴侣\.dsh-team\qa-independent\audit5.txt"
FILES = ["dishes.seed.json","dishes.seed.more.json","dishes.seed.dessert.json",
         "dishes.seed.drink.json","dishes.seed.more2.json","dishes.seed.recipes.json"]
buf=[]; p=lambda *a: buf.append(" ".join(str(x) for x in a))
data={f: json.loads(io.open(os.path.join(SEED,f),encoding="utf-8-sig").read()) for f in FILES}

p("=== mealTimes / seasons mask ranges (valid: meal 0..15, season 0..15) ===")
MTNAMES=["None","Breakfast","Lunch","Lunch|Breakfast","Dinner","Dinner|Breakfast","Dinner|Lunch","Dinner|Lunch|Breakfast",
         "LateNight","LateNight|Breakfast","LateNight|Lunch","LateNight|Lunch|Breakfast","LateNight|Dinner",
         "LateNight|Dinner|Breakfast","LateNight|Dinner|Lunch","All"]
SSNAMES=["AllYear","Spring","Summer","Summer|Spring","Autumn","Autumn|Spring","Autumn|Summer","Autumn|Summer|Spring",
         "Winter","Winter|Spring","Winter|Summer","Winter|Summer|Spring","Winter|Autumn","Winter|Autumn|Spring",
         "Winter|Autumn|Summer","Winter|Autumn|Summer|Spring"]
def toint(v, names):
    if isinstance(v,int): return v
    if isinstance(v,str):
        if v in names: return names.index(v)
        if v.isdigit(): return int(v)
        # composite like "Lunch, Dinner"
        parts=[q.strip() for q in v.replace("|",",").split(",") if q.strip()]
        if all(q in names for q in parts):
            r=0
            for q in parts: r |= (1 << names.index(q))
            return r
    return None
mt=collections.Counter(); ss=collections.Counter(); bad=[]; raw=collections.Counter()
for f,arr in data.items():
    for x in arr:
        if "mealTimes" in x:
            v=toint(x["mealTimes"], MTNAMES); raw[("mt",type(x["mealTimes"]).__name__)]+=1
            if v is None: bad.append((f,x["name"],"mealTimes",x["mealTimes"]))
            else:
                mt[v]+=1
                if not (0 <= v <= 15): bad.append((f,x["name"],"mealTimes",x["mealTimes"]))
        if "seasons" in x:
            v=toint(x["seasons"], SSNAMES); raw[("ss",type(x["seasons"]).__name__)]+=1
            if v is None: bad.append((f,x["name"],"seasons",x["seasons"]))
            else:
                ss[v]+=1
                if not (0 <= v <= 15): bad.append((f,x["name"],"seasons",x["seasons"]))
p("value types:", dict(raw))
p("mealTimes values:", sorted(mt.items()))
p("seasons values:", sorted(ss.items()))
p("out of range:", bad)

p("")
p("=== imageUrl / canMakeAtHome ===")
iu=[]; cmh=collections.Counter()
for f,arr in data.items():
    for x in arr:
        if x.get("imageUrl"): iu.append((f,x["name"],x["imageUrl"]))
        if "canMakeAtHome" in x: cmh[(f,x["canMakeAtHome"])]+=1
p("imageUrl set count:", len(iu), iu[:20])
p("canMakeAtHome by file:", dict(cmh))
cmhfalse=[]
for f,arr in data.items():
    for x in arr:
        if x.get("canMakeAtHome") is False: cmhfalse.append((f,x["name"]))
p("canMakeAtHome=false:", cmhfalse)

p("")
p("=== aliases ===")
noalias=0; alias_dup=[]; alias_eq_name=[]; alias_collide=[]
allnames=set()
for f in FILES[:-1]:
    for x in data[f]: allnames.add(x["name"])
for f,arr in data.items():
    for x in arr:
        a=x.get("aliases") or []
        if not a: noalias+=1
        if len(set(a))!=len(a): alias_dup.append((f,x["name"],a))
        if x["name"] in a: alias_eq_name.append((f,x["name"],a))
        for t in a:
            if t in allnames and t != x["name"]: alias_collide.append((f,x["name"],t))
p("dishes without aliases:", noalias)
p("alias dup within dish:", alias_dup)
p("alias == own name:", alias_eq_name)
p("alias collides with another dish name:", alias_collide)

p("")
p("=== tags ===")
tagc=collections.Counter(); notags=[]
for f in FILES[:-1]:
    for x in data[f]:
        t=x.get("tags") or []
        if not t: notags.append((f,x["name"]))
        for tt in t: tagc[tt]+=1
p("dishes without tags:", notags)
p("distinct tags:", len(tagc), "top:", tagc.most_common(15))
p("tags used once:", sum(1 for k,v in tagc.items() if v==1))

p("")
p("=== sample content of new dishes (10 random-ish picks) ===")
picks = ["西湖龙井","珍珠奶茶","杨枝甘露","马卡龙","提拉米苏","佛跳墙","肉燕","三大炮","钵钵鸡","班尼迪克蛋"]
idx={}
for f,arr in data.items():
    for x in arr: idx.setdefault(x["name"],[]).append((f,x))
for n in picks:
    for f,x in idx.get(n,[]):
        p("  [%s] %s  cuisine=%s category=%s spicy=%s price=%s-%s cal=%s pop=%s" % (f,n,x.get("cuisine"),x.get("category"),x.get("spicyLevel"),x.get("priceMinCents"),x.get("priceMaxCents"),x.get("calories"),x.get("popularity")))
        p("      ingredients: " + "; ".join("%s %s" % (i.get("name"), i.get("amount")) for i in (x.get("ingredients") or [])))
        p("      tags: %s | desc: %s" % (x.get("tags"), x.get("description")))
        if x.get("recipe"):
            p("      recipe steps=%d: %s" % (len(x["recipe"]["steps"]), " || ".join(s["text"] for s in x["recipe"]["steps"])))

io.open(OUT,"w",encoding="utf-8").write("\n".join(buf))
print("written", len(buf))
