# -*- coding: utf-8 -*-
"""Independent QA round 7: allergen flag coverage."""
import json, io, os, collections
SEED = r"C:\Work\美食伴侣\src\FoodMate.Infrastructure\Data\Seed"
OUT = r"C:\Work\美食伴侣\.dsh-team\qa-independent\audit7.txt"
FILES = ["dishes.seed.json","dishes.seed.more.json","dishes.seed.dessert.json",
         "dishes.seed.drink.json","dishes.seed.more2.json","dishes.seed.recipes.json"]
buf=[]; p=lambda *a: buf.append(" ".join(str(x) for x in a))
data={f: json.loads(io.open(os.path.join(SEED,f),encoding="utf-8-sig").read()) for f in FILES}
p("=== isCommonAllergen coverage per file ===")
for f,arr in data.items():
    tot=0; flagged=0; explicit=0; dishes_with=0
    for x in arr:
        ing=x.get("ingredients") or []
        if not ing: continue
        has=False
        for i in ing:
            tot+=1
            if "isCommonAllergen" in i:
                explicit+=1
                if i["isCommonAllergen"]: flagged+=1; has=True
        if has: dishes_with+=1
    p("  %-28s ingredients=%d  explicit-flag=%d  flag=true=%d  dishes-with->=1-allergen=%d" % (f,tot,explicit,flagged,dishes_with))
p("")
p("=== new-file dishes with NO allergen flagged, but ingredient name looks allergenic ===")
KEYS={"牛奶":"奶","奶粉":"奶","芝士":"奶","马斯卡彭芝士":"奶","黄油":"奶","淡奶油":"奶","椰浆":"椰","酸奶":"奶","炼乳":"奶",
      "鸡蛋":"蛋","蛋白":"蛋","蛋黄":"蛋","面粉":"麸质","面包":"麸质","面条":"麸质","河粉":"麸质","松饼":"麸质","饼干":"麸质",
      "花生":"花生","杏仁粉":"坚果","核桃":"坚果","腰果":"坚果","芝麻":"芝麻","黄豆":"大豆","豆浆":"大豆","豆腐":"大豆","酱油":"大豆","生抽":"大豆","豆瓣酱":"大豆",
      "虾":"甲壳","蟹":"甲壳","螃蟹":"甲壳","蚝油":"贝类","鲍鱼":"贝类","海参":"贝类","鱿鱼":"头足","章鱼":"头足",
      "鱼":"鱼","鲈鱼":"鱼","三文鱼":"鱼","鳕鱼":"鱼","鱼丸":"鱼","鱼露":"鱼","虾油":"甲壳","鱼头":"鱼"}
hits=[]
for f in ("dishes.seed.dessert.json","dishes.seed.drink.json","dishes.seed.more2.json"):
    for x in data[f]:
        bad=[]
        for i in (x.get("ingredients") or []):
            nm=str(i.get("name",""))
            if i.get("isCommonAllergen"): continue
            for k,v in KEYS.items():
                if k in nm: bad.append((nm,v)); break
        if bad: hits.append((f,x["name"],bad))
p("dishes with unflagged allergenic-looking ingredient:", len(hits))
for h in hits[:60]: p("   ", h)
p("")
p("=== same scan over the pre-existing files ===")
hits2=[]
for f in ("dishes.seed.json","dishes.seed.more.json"):
    for x in data[f]:
        bad=[]
        for i in (x.get("ingredients") or []):
            nm=str(i.get("name",""))
            if i.get("isCommonAllergen"): continue
            for k,v in KEYS.items():
                if k in nm: bad.append((nm,v)); break
        if bad: hits2.append((f,x["name"],bad))
p("pre-existing dishes with unflagged allergenic-looking ingredient:", len(hits2))
for h in hits2[:30]: p("   ", h)
io.open(OUT,"w",encoding="utf-8").write("\n".join(buf))
print("written", len(buf))
