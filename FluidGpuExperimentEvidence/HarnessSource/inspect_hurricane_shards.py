"""Numerically inspect source-alpha contours before Unity authoring; no image pixels are edited."""
from pathlib import Path
import json
import math
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
FOLDER = ROOT / 'Assets/_Project/Features/Bartending/FluidGpuExperiment/Art/Shards/Hurricane'

def sub(a, b): return a[0] - b[0], a[1] - b[1]
def dot(a, b): return a[0] * b[0] + a[1] * b[1]
def cross(a, b): return a[0] * b[1] - a[1] * b[0]
def area(poly): return sum(cross(a, b) for a, b in zip(poly, poly[1:] + poly[:1])) / 2

def simplify(points, epsilon=1.25):
    first, last = points[0], points[-1]
    line = sub(last, first)
    length = dot(line, line)
    largest, split = 0, 0
    for i in range(1, len(points) - 1):
        t = min(1, max(0, dot(sub(points[i], first), line) / length)) if length else 0
        closest = first[0] + t * line[0], first[1] + t * line[1]
        distance = dot(sub(points[i], closest), sub(points[i], closest))
        if distance > largest: largest, split = distance, i
    return (simplify(points[:split+1], epsilon)[:-1] + simplify(points[split:], epsilon)
            if largest > epsilon ** 2 else [first, last])

def simple(poly):
    for i in range(len(poly)):
        for j in range(i + 1, len(poly)):
            if j == i + 1 or (i == 0 and j == len(poly) - 1): continue
            a, b, c, d = poly[i], poly[(i+1) % len(poly)], poly[j], poly[(j+1) % len(poly)]
            if (cross(sub(b,a),sub(c,a)) * cross(sub(b,a),sub(d,a)) <= 0
                and cross(sub(d,c),sub(a,c)) * cross(sub(d,c),sub(b,c)) <= 0
                and max(min(a[0],b[0]),min(c[0],d[0])) <= min(max(a[0],b[0]),max(c[0],d[0]))
                and max(min(a[1],b[1]),min(c[1],d[1])) <= min(max(a[1],b[1]),max(c[1],d[1]))): return False
    return True

report = []
for source in sorted(FOLDER.glob('*.png')):
    with Image.open(source) as image:
        alpha = image.convert('RGBA').getchannel('A')
        width, height = image.size
        pixels = list(alpha.get_flattened_data())
    solid = {(x, height-1-y) for y in range(height) for x in range(width) if pixels[y*width+x] > 8}
    edges = {}
    def add(a,b): edges.setdefault(a,[]).append(b)
    for x, y in sorted(solid):
        if (x,y-1) not in solid: add((x,y),(x+1,y))
        if (x+1,y) not in solid: add((x+1,y),(x+1,y+1))
        if (x,y+1) not in solid: add((x+1,y+1),(x,y+1))
        if (x-1,y) not in solid: add((x,y+1),(x,y))
    loops = []
    while edges:
        start = min(edges,key=lambda p:(p[1],p[0]))
        point, incoming, loop = start, (1,0), []
        while True:
            loop.append(point)
            ends = edges[point]
            nxt = max(ends, key=lambda p:math.atan2(cross(incoming,sub(p,point)),dot(incoming,sub(p,point))))
            ends.remove(nxt)
            if not ends: del edges[point]
            incoming, point = sub(nxt, point), nxt
            if point == start: break
        loops.append(loop)
    outside = sorted((p for p in loops if area(p) > 0), key=area, reverse=True)
    poly = outside[0]
    outer_area = area(poly)
    islands = sum(area(p) for p in outside[1:])
    holes = sum(-area(p) for p in loops if area(p)<0)
    assert islands <= max(2,outer_area*.001), (source.name, 'islands', islands)
    assert holes <= max(2,outer_area*.001), (source.name, 'holes', holes)
    split = max(range(1,len(poly)), key=lambda i:dot(sub(poly[i],poly[0]),sub(poly[i],poly[0])))
    epsilon=1.25
    while epsilon >= .09:
        reduced=simplify(poly[:split+1],epsilon)[:-1]+simplify(poly[split:]+poly[:1],epsilon)[:-1]
        if len(reduced)>=3 and simple(reduced): break
        epsilon /= 2
    assert simple(reduced), source.name
    signed_area=area(reduced)
    centroid=tuple(sum((a[k]+b[k])*cross(a,b) for a,b in zip(reduced,reduced[1:]+reduced[:1])) / (6*signed_area) for k in (0,1))
    reflex=sum(cross(sub(reduced[(i+1)%len(reduced)],reduced[i]),sub(reduced[(i+2)%len(reduced)],reduced[(i+1)%len(reduced)]))<0 for i in range(len(reduced)))
    report.append(dict(name=source.name, source_alpha_pixels=len(solid), holes_pixels=holes,
                       disconnected_pixels=islands, contour_points=len(poly), simplified_points=len(reduced),
                       reflex_vertices=reflex, tolerance_pixels=epsilon, area_error_ratio=abs(signed_area-outer_area)/outer_area,
                       centroid_pixels=centroid, source_polygon=reduced))

(ROOT/'FluidGpuExperimentEvidence/hurricane-shard-contour-analysis.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps([{k:v for k,v in row.items() if k!='source_polygon'} for row in report],indent=2))
