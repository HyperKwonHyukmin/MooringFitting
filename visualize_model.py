# -*- coding: utf-8 -*-
# 파일 위치: Services/Visualization/Scripts/visualize_model.py
import pandas as pd
import pyvista as pv
import numpy as np
import os
import sys

# --------------------------------------------------------------------------
# [설정] 시각화 스타일 파라미터
# --------------------------------------------------------------------------
SPC_COLOR = 'blue'        # 경계조건 색상
SPC_SIZE = 200.0          # 경계조건 크기
MF_ZOOM_DIST = 6000       # MF 상세 뷰 거리
WINCH_ZOOM_DIST = 6000    # Winch 상세 뷰 거리

# 화살표 스타일
ARROW_SCALE_RATIO = 0.3
ARROW_SHAFT_RAD = 0.01
ARROW_TIP_RAD = 0.04

# Element ID 라벨 스타일
ELEM_LABEL_SIZE = 17      
ELEM_LABEL_COLOR = 'black'
# --------------------------------------------------------------------------

def run_visualization(output_dir):
    print(f"python: Visualizing data from: {output_dir}")

    # 1. 파일 경로 설정
    node_file = os.path.join(output_dir, "Final_Nodes_Check.csv")
    elem_file = os.path.join(output_dir, "Final_Elements_Check.csv")
    rigid_file = os.path.join(output_dir, "Final_Rigids_Check.csv")
    spc_file = os.path.join(output_dir, "Final_SPC_Check.csv")
    mf_load_file = os.path.join(output_dir, "Report_LoadCalculation_MF.csv")
    winch_load_file = os.path.join(output_dir, "Report_LoadCalculation_Winch.csv")

    if not os.path.exists(node_file) or not os.path.exists(elem_file):
        print(f"python error: Essential CSV files missing.")
        return

    # 2. 데이터 로드 (Nodes)
    try:
        df_nodes = pd.read_csv(node_file)
        df_nodes.columns = [c.strip() for c in df_nodes.columns]
        nodes_map = {row['NodeID']: [row['X'], row['Y'], row['Z']] for _, row in df_nodes.iterrows()}
        node_coords = np.array(list(nodes_map.values()))
        node_id_to_idx = {nid: i for i, nid in enumerate(nodes_map.keys())}
    except Exception as e:
        print(f"python error loading nodes: {e}")
        return

    # 3. 데이터 로드 (Elements) & 중심점 계산
    lines = []
    elem_centers = []       
    elem_ids_labels = []    

    try:
        df_elems = pd.read_csv(elem_file)
        for _, row in df_elems.iterrows():
            try:
                nids = [int(x) for x in str(row['NodeIDs']).replace(';', ',').split(',') if x.strip()]
                valid_nids_idx = [node_id_to_idx[n] for n in nids if n in node_id_to_idx]
                
                if len(valid_nids_idx) >= 2:
                    lines.extend([len(valid_nids_idx)] + valid_nids_idx)

                # ID 라벨용 중심점 계산
                valid_coords = [nodes_map[n] for n in nids if n in nodes_map]
                if valid_coords:
                    center = np.mean(valid_coords, axis=0)
                    elem_centers.append(center)
                    elem_ids_labels.append(str(row['ElementID']))

            except: continue
    except: pass

    # 4. 데이터 로드 (Rigids)
    rigid_lines = []
    if os.path.exists(rigid_file):
        try:
            df_rigid = pd.read_csv(rigid_file)
            for _, row in df_rigid.iterrows():
                try:
                    ind = int(row['IndependentNodeID'])
                    deps = [int(x) for x in str(row['DependentNodeIDs']).split(';') if x.strip()]
                    if ind in node_id_to_idx:
                        ind_idx = node_id_to_idx[ind]
                        for dep in deps:
                            if dep in node_id_to_idx:
                                rigid_lines.extend([2, ind_idx, node_id_to_idx[dep]])
                except: continue
        except: pass

    # 5. 데이터 로드 (SPC)
    spc_points = []
    if os.path.exists(spc_file):
        try:
            df_spc = pd.read_csv(spc_file)
            for _, row in df_spc.iterrows():
                nid = int(row['NodeID'])
                if nid in nodes_map:
                    spc_points.append(nodes_map[nid])
        except Exception as e:
            print(f"python warning loading SPCs: {e}")

    # 6. 메쉬 생성
    structure_mesh = pv.PolyData(node_coords)
    if lines: structure_mesh.lines = np.hstack(lines)
    
    rigid_mesh = None
    if rigid_lines:
        rigid_mesh = pv.PolyData(node_coords)
        rigid_mesh.lines = np.hstack(rigid_lines)

    spc_mesh = None
    if spc_points:
        cloud = pv.PolyData(np.array(spc_points))
        cone_geom = pv.Cone(radius=SPC_SIZE/2, height=SPC_SIZE, direction=(0, 0, 1), resolution=3)
        spc_mesh = cloud.glyph(geom=cone_geom, scale=False, orient=False)

    # ---------------------------------------------------------
    # 7. 씬 생성 함수
    # ---------------------------------------------------------
    # [수정] show_elem_ids 파라미터 추가 (기본값 True)
    def create_scene(filename, focus_point=None, zoom_dist=None, loads=None, view_mode='iso', show_elem_ids=True):
        plotter = pv.Plotter(off_screen=True, window_size=[2000, 1500])
        plotter.set_background('white')

        # [Draw] Structure
        plotter.add_mesh(structure_mesh, color='green', line_width=2, label='Structure')
        if rigid_mesh:
            plotter.add_mesh(rigid_mesh, color='red', line_width=3, label='Rigid')
        
        # [Draw] SPC
        if spc_mesh:
            plotter.add_mesh(spc_mesh, color=SPC_COLOR, style='wireframe', line_width=2, label='Boundary Cond.')

        # [Draw] Element ID (옵션이 켜져 있을 때만)
        if show_elem_ids and elem_centers:
            plotter.add_point_labels(
                np.array(elem_centers), 
                elem_ids_labels,
                font_size=ELEM_LABEL_SIZE,
                text_color=ELEM_LABEL_COLOR,
                point_size=0,
                shape_opacity=0.0,
                show_points=False
            )

        # [Draw] Loads
        if loads:
            for load in loads:
                if load['NodeID'] not in nodes_map: continue
                start = np.array(nodes_map[load['NodeID']])
                vec = np.array(load['Vector'])
                mag = np.linalg.norm(vec)
                
                if mag > 1e-3:
                    direction = vec / mag
                    base_scale = zoom_dist if zoom_dist else 2000.0
                    scale = base_scale * ARROW_SCALE_RATIO
                    
                    arrow = pv.Arrow(start=start, direction=direction, scale=scale, 
                                     shaft_radius=ARROW_SHAFT_RAD, tip_radius=ARROW_TIP_RAD)
                    l_color = 'magenta' if load['Type'] == 'Winch' else 'red'
                    plotter.add_mesh(arrow, color=l_color)

                    if load['Value'] > 0:
                        label_pos = start + direction * (scale * 1.1)
                        label_text = f"{load['ID']}\n{load['Value']:.1f}T"
                        plotter.add_point_labels([label_pos], [label_text], 
                                                 font_size=18, text_color='black', 
                                                 shape_color='white', fill_shape=True)

        # [Camera Setting]
        if view_mode == 'top':
            plotter.view_xy()  
            plotter.camera.up = (0, 1, 0)
        elif focus_point is not None and zoom_dist is not None:
            fp = np.array(focus_point)
            cam_pos = fp + np.array([zoom_dist, -zoom_dist, zoom_dist]) * 0.8
            plotter.camera.position = cam_pos
            plotter.camera.focal_point = fp
            plotter.camera.up = (0, 0, 1)
        else:
            plotter.view_isometric()

        plotter.add_axes()
        save_path = os.path.join(output_dir, filename)
        plotter.screenshot(save_path)
        print(f"python: Generated {filename}")
        plotter.close()

    # ---------------------------------------------------------
    # 8. 하중 파싱
    # ---------------------------------------------------------
    all_loads = []
    
    if os.path.exists(mf_load_file):
        try:
            df = pd.read_csv(mf_load_file, on_bad_lines='skip')
            df.columns = [c.strip() for c in df.columns]
            for _, r in df.iterrows():
                res = str(r['Result']).strip() if 'Result' in r else 'Success'
                if res == 'Success':
                    all_loads.append({
                        'Type': 'MF', 'ID': r['MF_ID'], 'NodeID': int(r['NodeID']),
                        'Value': float(r['SWL(Ton)']),
                        'Vector': [float(r['Calc_Fx']), float(r['Calc_Fy']), float(r['Calc_Fz'])]
                    })
        except: pass
    
    if os.path.exists(winch_load_file):
        try:
            df = pd.read_csv(winch_load_file, on_bad_lines='skip')
            df.columns = [c.strip() for c in df.columns]
            for _, r in df.iterrows():
                 try: 
                    val = float(r['Input_Fx(Ton)']) if 'Input_Fx(Ton)' in r else 0.0
                    all_loads.append({
                        'Type': 'Winch', 'ID': r['WinchID'], 'NodeID': int(r['NodeID']),
                        'Value': val,
                        'Vector': [float(r['Final_Fx']), float(r['Final_Fy']), float(r['Final_Fz'])]
                    })
                 except: pass
        except: pass

    # ================= [이미지 생성 실행] =================

    # 1. 전체 모델 (Top View) - ★ ID 표시 끄기 (False)
    create_scene("View_01_Full_Model.png", loads=all_loads, view_mode='top', show_elem_ids=False)

    # 2. MF별 확대 - ID 표시 켜기 (True)
    for mf in [l for l in all_loads if l['Type'] == 'MF']:
        if mf['NodeID'] in nodes_map:
            create_scene(f"View_MF_{mf['ID']}.png", 
                         focus_point=nodes_map[mf['NodeID']], 
                         zoom_dist=MF_ZOOM_DIST,
                         loads=[mf],
                         show_elem_ids=True)

    # 3. Winch별 확대 (Grouping) - ID 표시 켜기 (True)
    winch_groups = {}
    for w in [l for l in all_loads if l['Type'] == 'Winch']:
        wid = w['ID']
        if wid not in winch_groups: winch_groups[wid] = []
        winch_groups[wid].append(w)

    for wid, w_loads in winch_groups.items():
        if not w_loads: continue
        first_load = w_loads[0]
        if first_load['NodeID'] in nodes_map:
            create_scene(f"View_Winch_{wid}.png", 
                         focus_point=nodes_map[first_load['NodeID']], 
                         zoom_dist=WINCH_ZOOM_DIST, 
                         loads=w_loads,
                         show_elem_ids=True)

if __name__ == "__main__":
    if len(sys.argv) > 1:
        run_visualization(sys.argv[1])
