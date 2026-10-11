# -*- coding: utf-8 -*-
"""Independent QA analysis of the LIVE API (C2, C3, C4) + seed-vs-DB cross check."""
import json, io, collections, os

QA = r"C:\Work\美食伴侣\.dsh-team\qa"
OUT = os.path.join(QA, "api_report.txt")
R = io.StringIO()
def p(*a): print(*a, file=R)

lst = json.load(open(os.path.join(QA,"dishes.list.json"), encoding="utf-8-sig"))
det = json.load(open(os.path.join(QA,"dishes.details.json"), encoding="utf-8-sig"))
p(f"list items={len(lst)}  detail items={len(det)}")

# ---------------- C4: duplicate names in DB ----------------
p(""); p("="*78); p("C4-a. DB DUPLICATE DISH NAMES (from GET /api/v1/dishes)"); p("="*78)
c = collections.Counter((d.get("name") or "").strip().lower() for d in lst)
dups = {k:v for k,v in c.items() if v>1}
p(f"  total items={len(lst)}  distinct names(case-insensitive)={len(c)}")
p(f"  duplicates: {dups if dups else 'NONE'}")
blank = [d.get("id") for d in lst if not (d.get("name") or "").strip()]
p(f"  blank names: {blank if blank else 'NONE'}")

# ---------------- C2: category grouping ----------------
p(""); p("="*78); p("C2. CATEGORY GROUPING (categoryLabel over ALL pages)"); p("="*78)
cat = collections.Counter(d.get("categoryLabel") for d in lst)
for k,v in sorted(cat.items(), key=lambda x:-x[1]):
    p(f"  {str(k):12s} {v:4d}")
p(f"  sum={sum(cat.values())}  (must equal {len(lst)})")
p("")
p(f"  >>> 甜点 (Dessert) = {cat.get('甜点',0)}   threshold >=30  -> {'PASS' if cat.get('甜点',0)>=30 else 'FAIL'}")
p(f"  >>> 饮品 (Drink)   = {cat.get('饮品',0)}   threshold >=25  -> {'PASS' if cat.get('饮品',0)>=25 else 'FAIL'}")

# ---------------- C1 ----------------
p(""); p("="*78); p("C1. TOTAL DISH COUNT"); p("="*78)
p(f"  /api/v1/dishes data.total reported by API = (see list fetch log) ; items returned = {len(lst)}")
p(f"  /health/ready checks.dishCount = 301")
p(f"  threshold >=300 -> {'PASS' if len(lst)>=300 else 'FAIL'}   margin = {len(lst)-300}")

# ---------------- C3: recipes ----------------
p(""); p("="*78); p("C3. RECIPE COMPLETENESS (GET /api/v1/dishes/{id})"); p("="*78)
withr=[]; nor=[]; short=[]; weird=[]
for d in det:
    r = d.get("recipe")
    if r is None:
        nor.append(d.get("name")); continue
    steps = r.get("steps") or []
    n=len(steps)
    if n>=3: withr.append((d.get("name"), n))
    else: short.append((d.get("name"), n))
p(f"  details fetched          = {len(det)}")
p(f"  recipe != null           = {len(withr)+len(short)}")
p(f"  recipe == null           = {len(nor)}")
p(f"  recipe != null AND steps>=3 = {len(withr)}   threshold >=40 -> {'PASS' if len(withr)>=40 else 'FAIL'}")
p(f"  recipe != null AND steps<3  = {len(short)}  -> {'PASS (none)' if not short else 'FAIL'}")
for x in short[:20]: p(f"     *** {x}")
p(f"  step-count histogram: {dict(sorted(collections.Counter(n for _,n in withr).items()))}")
if withr: p(f"  min={min(n for _,n in withr)} max={max(n for _,n in withr)}")

# hasRecipe flag vs actual
flag_mismatch=[]
for d in det:
    hb = d.get("hasRecipe")
    actual = d.get("recipe") is not None
    if hb is not None and bool(hb)!=actual:
        flag_mismatch.append((d.get("name"), hb, actual))
p(f"  hasRecipe flag inconsistent with recipe payload: {len(flag_mismatch)}")
for x in flag_mismatch[:20]: p(f"     *** {x}")

# ---------------- detail field sanity ----------------
p(""); p("="*78); p("C3-b. DETAIL-PAYLOAD SANITY"); p("="*78)
prob=collections.defaultdict(list)
for d in det:
    n=d.get("name")
    pmin,pmax=d.get("priceMinCents"),d.get("priceMaxCents")
    if pmin is not None and pmax is not None and pmin>pmax: prob["priceMin>priceMax"].append((n,pmin,pmax))
    cal=d.get("calories")
    if cal is not None and (cal<=0 or cal>3000): prob["calories<=0 or >3000"].append((n,cal))
    sl=d.get("spicyLevel")
    if sl is not None and (sl<0 or sl>5): prob["spicyLevel out of 0..5"].append((n,sl))
    if not (d.get("ingredients") or []): prob["no ingredients in DB"].append(n)
    if d.get("categoryLabel") in (None,""): prob["missing categoryLabel"].append(n)
    if d.get("cuisineLabel") in (None,""): prob["missing cuisineLabel"].append(n)
    if d.get("isBuiltin") is not True: prob["not isBuiltin"].append(n)
if prob:
    for k,v in sorted(prob.items()):
        p(f"  {k}: {len(v)}")
        for x in v[:20]: p(f"     *** {x}")
else:
    p("  NONE")

# ---------------- seed vs DB ----------------
p(""); p("="*78); p("X. SEED vs DB CONSISTENCY"); p("="*78)
merged = json.load(open(os.path.join(QA,"merged_sim.json"), encoding="utf-8"))
seed_names = set(n.lower() for n in merged["order"])
db_names   = set((d.get("name") or "").strip().lower() for d in lst)
p(f"  seed merged unique names = {len(seed_names)}")
p(f"  DB names                 = {len(db_names)}")
p(f"  in seed not in DB        = {sorted(seed_names-db_names)[:20]}")
p(f"  in DB not in seed        = {sorted(db_names-seed_names)[:20]}")

open(OUT,"w",encoding="utf-8").write(R.getvalue())
print("WROTE", OUT)
