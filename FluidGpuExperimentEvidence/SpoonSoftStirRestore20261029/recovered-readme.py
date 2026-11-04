from pathlib import Path
p=Path('Assets/_Project/Features/Bartending/FluidGpuExperiment/README.md')
s=p.read_text(encoding='utf-8');lines=s.splitlines(keepends=True)
i=next(i for i,v in enumerate(lines) if v.startswith('- **스푼:**'))
lines[i]=lines[i].replace('그 용기의 액체에만 접촉한다.','그 용기의 액체와 겹친 채 움직이면 재료가 서서히 섞이고 젖은 부분 주변에 약한 흐름이 생긴다. 스푼의 액체 고체 충돌과 액체를 파내는 표시 마스크는 사용하지 않는다.').replace('Stir는 우클릭 중 해당 용기의 액체에 닿아 움직일 때 관찰하며','Stir는 우클릭 중 해당 용기의 액체와 겹쳐 움직일 때 관찰하며')
lines.insert(i+1,'  GPU 컴포넌트의 `spoonFlowSpeed`(기본 0.16u/s)와 `spoonFlowAcceleration`(0.6u/s²)은 흐름의 목표 속도·가속도 상한이며 전체 유체 속도의 상한은 아니다. `spoonMixRate`(1.6/s)는 움직임에 따른 잔 전체 재료 균일화 속도로, 흐름의 세기와 별도다. 멈추면 혼합 자극이 해제되고 0.3초의 짧은 감쇠 흐름 뒤 자연 유체 계산만 남는다.\n')
p.write_text(''.join(lines),encoding='utf-8')
