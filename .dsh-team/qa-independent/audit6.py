# -*- coding: utf-8 -*-
"""Independent QA round 6: do the 145 new dishes collide with the pre-existing 156 (name or alias)?"""
import json, io, os, collections, unicodedata, re
SEED = r"C:\Work\美食伴侣\src\FoodMate.Infrastructure\Data\Seed"
OUT = r"C:\Work\美食伴侣\.dsh-team\qa-independent\audit6.txt"
FILES = ["dishes.seed.json","dishes.seed.more.json","dishes.seed.dessert.json",
         "dishes.seed.drink.json","dishes.seed.more2.json","dishes.seed.recipes.json"]
buf=[]; p=lambda *a: buf.append(" ".join(str(x) for x in a))
data={f: json.loads(io.open(os.path.join(SEED,f),encoding="utf-8-sig").read()) for f in FILES}

OLD=[x for f in ("dishes.seed.json","dishes.seed.more.json") for x in data[f]]
NEW=[(f,x) for f in ("dishes.seed.dessert.json","dishes.seed.drink.json","dishes.seed.more2.json") for x in data[f]]
oldnames={x["name"] for x in OLD}
oldaliases={a for x in OLD for a in (x.get("aliases") or [])}
newnames=[x["name"] for f,x in NEW]
p("old dishes:", len(OLD), "new dishes:", len(NEW))
p("new name equals an old dish name:", sorted(set(newnames) & oldnames))
p("new name equals an old alias:", sorted(set(newnames) & oldaliases))

def norm(s):
    s=unicodedata.normalize("NFKC",str(s))
    return re.sub(r"[\s\u3000·・\-—_()（）\[\]【】,，。.、!！?？]+","",s).lower()
oldnorm={norm(n):n for n in oldnames}
oldalnorm={norm(a):a for a in oldaliases}
p("")
p("new name normalized-equal to an old dish name:")
for n in newnames:
    if norm(n) in oldnorm: p("   ", n, "==", oldnorm[norm(n)])
p("new name normalized-equal to an old alias:")
for n in newnames:
    if norm(n) in oldalnorm: p("   ", n, "==", oldalnorm[norm(n)])
p("")
p("new name contains / is contained by an old name (<=3 char diff):")
cnt=0
for n in newnames:
    a=norm(n)
    for b,orig in oldnorm.items():
        if a!=b and (a in b or b in a) and abs(len(a)-len(b))<=3:
            p("   ", n, "<->", orig); cnt+=1
p("count:", cnt)
p("")
p("new-vs-new containment (<=3 char diff):")
nn=[(norm(x["name"]),x["name"]) for f,x in NEW]
c2=0
for i in range(len(nn)):
    for j in range(i+1,len(nn)):
        a,b=nn[i][0],nn[j][0]
        if a!=b and (a in b or b in a) and abs(len(a)-len(b))<=3:
            p("   ", nn[i][1], "<->", nn[j][1]); c2+=1
p("count:", c2)

# aliases defined only in the old files but pointing at new dishes? already covered.
# sanity: every new dish category belongs to its file's theme
p("")
p("=== new-file theme check ===")
for f in ("dishes.seed.dessert.json","dishes.seed.drink.json","dishes.seed.more2.json"):
    cats=collections.Counter(x["category"] for x in data[f])
    p("  %-28s %s" % (f, dict(cats)))
p("")
p("=== new dishes missing optional but user-visible fields ===")
for f,x in NEW:
    miss=[k for k in ("description","aliases") if not x.get(k)]
    if miss: p("   ", f, x["name"], miss)
io.open(OUT,"w",encoding="utf-8").write("\n".join(buf))
print("written", len(buf))
