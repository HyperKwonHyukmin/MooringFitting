import numpy as np
import pyvista as pv
import copy
import os


class createFigure:
  def __init__(self, BDF, node_cls, element_cls, NodeSPC_list, MF_dict, winchLC):
    self.BDF = BDF
    self.node_cls = node_cls
    self.element_cls = element_cls
    self.NodeSPC_list = NodeSPC_list
    self.MF_dict = MF_dict
    self.winchLC = winchLC

    # rigid를 형성하는 Node들만 솎아내기 (빨간색으로 따로 표시해주기 위해)
    self.rigidNodes_list = []
    for key in MF_dict:
      for node2 in MF_dict[key]['rigid_list']:
        temp_list = [MF_dict[key]['MF_nodeID'], node2]
        self.rigidNodes_list.append(temp_list)

    # 캡쳐 fig 위치를 저장할 딕셔너리 생성
    self.fig_file_dict = {}



  def Run(self):
    # 전체 모델 fig 캡쳐 저장 경로
    full_fig_file = self.BDF.replace('bdf', 'png')
    self.Show(self.node_cls, self.element_cls, self.rigidNodes_list, self.NodeSPC_list, node_label=False,
              text_label=False, save_path=full_fig_file, bounday_point_size=10)
    self.fig_file_dict['full_fig_file'] = full_fig_file  # 캡쳐파일 딕셔너리에 저장


    # winch 하중이 포함된 전체 모델 캡쳐 수행, 유효한 Load Case 개수만큼 캡쳐 수행
    Valid_LoadCase_list = self.winchLC['Valid_LoadCase']
    for LC in Valid_LoadCase_list: # 유효한 Load Case를 순회하며 Winch node ID와 Winch name 그리고 방향 벡터를 만든다
      winchLC_annotations = []
      for force in self.winchLC['Calculated_force'][LC]:
        force_node_id = self.winchLC['Mooring_NodeID_dict'][force]
        direction_list = []
        for force_direction in ['forceX', 'forceY', 'forceZ']:
          if self.winchLC['Calculated_force'][LC][force][force_direction] > 0:
            direction_list.append(1000)
          elif self.winchLC['Calculated_force'][LC][force][force_direction] == 0:
            direction_list.append(0)
          elif self.winchLC['Calculated_force'][LC][force][force_direction] < 0:
            direction_list.append(-1000)
        winchLC_dict = {'node_id' : force_node_id, 'winch_name' : force, 'direction' : direction_list}
        winchLC_annotations.append(winchLC_dict)

      # 캡쳐 수행 시작
      bdf_folder = os.path.dirname(self.BDF)
      winch_fig_file = LC + '.png'
      winch_fig = os.path.join(bdf_folder, winch_fig_file)
      self.Show(self.node_cls, self.element_cls, self.rigidNodes_list, self.NodeSPC_list, node_label=False,
                text_label=False, save_path=winch_fig, bounday_point_size=10, winchLC_annotations=winchLC_annotations)

      self.fig_file_dict[LC] = winch_fig


    # MF 부분 모델 캡쳐 저장 경로
    for key in self.MF_dict:
      MF_name = self.MF_dict[key]['Name']
      MF_nodeID = self.MF_dict[key]['MF_nodeID']
      copy_node_cls = copy.deepcopy(self.node_cls)
      copy_element_cls = copy.deepcopy(self.element_cls)
      copy_rigidNodes_list = copy.deepcopy(self.rigidNodes_list)

      partial_node_cls, partial_element_cls, partial_rigidNodes_list = createFigure.NodeFilterInRange(copy_node_cls,
                                                                                                      copy_element_cls,
                                                                                                      copy_rigidNodes_list,
                                                                                                      MF_nodeID,
                                                                                                      range=1500)
      # Partial node_cls 범위에 다른 구역의 MF_nodeID가 있다면 이를 제외시킨다.

      MF_nodeID_list = []

      for i in self.MF_dict:
        MF_nodeID_list.append(self.MF_dict[i]['MF_nodeID'])


      for MF_ID in MF_nodeID_list:
        if MF_ID in list(partial_node_cls.GetID_list()) and MF_ID != MF_nodeID:
          partial_node_cls.Remove(MF_ID)

      partial_boundary_list = [i for i in self.NodeSPC_list if i in partial_node_cls.GetID_list()]

      bdf_folder = os.path.dirname(self.BDF)
      fig_name = MF_name + '.png'
      MF_fig = os.path.join(bdf_folder, fig_name)
      self.Show(partial_node_cls, partial_element_cls, partial_rigidNodes_list,
                partial_boundary_list, node_label=True, text_label=True, save_path=MF_fig, bounday_point_size=25)

      self.fig_file_dict[MF_name] = MF_fig


  def Show(self, node_cls, element_cls, rigidNodes_list, NodeSPC_list, node_label=False, text_label=False,
           save_path=None, bounday_point_size=None, winchLC_annotations=None):
    # 노드 ID를 0부터 시작하는 인덱스로 매핑
    node_mapping = {ID: idx for idx, (ID, _) in enumerate(node_cls)}

    # 노드 데이터를 NumPy 배열로 변환
    nodes = []
    for ID, Value in node_cls:
      nodes.append([Value['X'], Value['Y'], Value['Z']])
    nodes_np = np.array(nodes)

    # 요소 데이터를 NumPy 배열로 변환 (노드 ID를 매핑된 인덱스로 변환)
    elements = []
    element_centers = []  # 요소 중심 좌표 저장
    for ID, Value in element_cls:
      mapped_nodes = [node_mapping[node] for node in Value['nodes']]
      elements.append(mapped_nodes)

      # 요소 중심 좌표 계산
      node_positions = nodes_np[mapped_nodes]
      center = np.mean(node_positions, axis=0)
      element_centers.append((center, ID))

    elements_np = np.array(elements)
    rigid_np = np.array(rigidNodes_list)
    boundary_conditions = NodeSPC_list if NodeSPC_list else []

    # PyVista 데이터 생성
    truss = pv.PolyData()
    truss.points = nodes_np

    # 요소를 PyVista 형식으로 변환
    truss.lines = np.hstack([[2] + element.tolist() for element in elements_np])

    # 강체 노드를 연결하는 선 생성
    if rigid_np.size > 0:
      rigid_lines = []
      for rigid_pair in rigid_np:
        rigid_lines.append([2] + [node_mapping[rigid_pair[0]], node_mapping[rigid_pair[1]]])
      rigid_truss = pv.PolyData()
      rigid_truss.points = nodes_np
      rigid_truss.lines = np.hstack(rigid_lines)

    # 노드 데이터 생성
    points = pv.PolyData(nodes_np)

    # PyVista 시각화 설정
    plotter = pv.Plotter(window_size=(1500, 1000), off_screen=True)
    plotter.set_background("white")

    # 트러스 추가 (반짝이는 효과와 부드러운 렌더링)
    plotter.add_mesh(
      truss, color="green", line_width=6, lighting=True, specular=0.6, specular_power=15, label="Truss"
    )

    # 강체 요소 추가 (붉은색)
    if rigid_np.size > 0:
      plotter.add_mesh(
        rigid_truss, color="red", line_width=2, lighting=True, specular=0.6, specular_power=15, label="Rigid Elements"
      )

    # 노드 추가 (부드러운 구형 렌더링)
    plotter.add_mesh(
      points, color="midnightblue", point_size=6, render_points_as_spheres=True, label="Nodes"
    )

    # 요소 번호 추가 (배경 흰색, 테두리 검은색 설정)
    element_labels = [str(ID) for _, ID in element_centers]
    element_positions = np.array([center for center, _ in element_centers])
    if node_label:
      plotter.add_point_labels(
        element_positions, element_labels, font_size=12, bold=True,
        fill_shape=True, shape_color='white', shape_opacity=1.0, shape='rounded_rect'
      )

    if text_label:
      # 보고서에 표시할 MF의 text가 저장될 딕셔너리 생성
      node_labels = {}

      for ID in self.MF_dict:
        # print('self.MF_dict[ID] : ', ID, self.MF_dict[ID])
        MF_nodeID = self.MF_dict[ID]['MF_nodeID']
        MF_name = self.MF_dict[ID]['Name']

        # 노드 ID를 key로, 표시할 텍스트를 value로 저장
        if MF_nodeID in node_cls.GetID_list():
          node_labels[MF_nodeID] = MF_name

        if MF_nodeID in node_mapping: # MF의 하중을 화살표로 표시
          start_point = nodes[node_mapping[MF_nodeID]]
          force_vector = np.array([float(self.MF_dict[ID]['Force_X']), float(self.MF_dict[ID]['Force_Y']), 0.0])

          # 벡터 크기(Norm) 계산
          norm = np.linalg.norm(force_vector)
          direction = (force_vector / norm) * 300

          # print('start_point, direction', start_point, direction)

          arrow = pv.Arrow(start=start_point, direction=direction, scale='auto')
          plotter.add_mesh(arrow, color="blue", opacity=1.0)

      label_positions = np.array([nodes_np[node_mapping[node_id]] for node_id in node_labels.keys()])
      label_positions = label_positions.reshape(-1, 3)  # (N, 3) 형태 보장
      label_texts = [node_labels[node_id] for node_id in node_labels.keys()]
      plotter.add_point_labels(
        label_positions, label_texts,
        font_size=20,  # 텍스트 크기 증가
        text_color = 'red',
        bold=True,
        fill_shape=True, shape_color='white',  # 배경을 흰색으로 변경
        render_points_as_spheres=False,  # 렌더링 오류 방지
        always_visible=True  # 카메라 각도에 상관없이 항상 표시
      )



    # 경계 조건 추가
    if boundary_conditions:
      boundary_points = np.array([nodes_np[node_mapping[node_id]] for node_id in boundary_conditions])
      boundary_symbols = pv.PolyData(boundary_points)
      plotter.add_mesh(
        boundary_symbols, color="red", point_size=bounday_point_size, render_points_as_spheres=True,
        label="Boundary Conditions"
      )

    if winchLC_annotations:
      for winchLC in winchLC_annotations:
        node_id = winchLC["node_id"]
        text = winchLC["winch_name"]
        direction = np.array(winchLC["direction"])


        if node_id in node_mapping:
          start_point = nodes[node_mapping[node_id]]
          end_point = start_point + direction  # 화살표 길이 조절

          # 📌 라벨 위치 보정 (화살표 끝에서 조금 더 떨어진 위치에 배치)
          label_offset = np.array([0, 0, 200])  # Z축 방향으로 200만큼 이동
          label_position = end_point + label_offset

          # print('start_point, direction2\ : ', start_point, direction)

          arrow = pv.Arrow(start=start_point, direction=direction, scale='auto')
          plotter.add_mesh(arrow, color="yellow", opacity=1.0)  # 💛 노란색 화살표로 변경

          plotter.add_point_labels(
            [label_position], [text],
            font_size=16, text_color='black',  # 🖤 검은색 텍스트로 변경
            always_visible=True,
            fill_shape=True,  # 배경을 채우기
            shape_color='white',  # ⬜ 흰색 배경 설정
            shape_opacity=1.0,  # 완전 불투명하게 설정
            shape='rounded_rect'  # 라벨 모양을 둥근 직사각형으로 설정
          )

    # 축 표시 및 스타일 설정
    plotter.show_axes()

    # 확대 비율 조정
    plotter.camera.zoom(1.2)

    # 조명 추가
    light1 = pv.Light(
      position=(30000, 30000, 50000),
      focal_point=(0, 0, 0),
      intensity=0.8,
      color="white"
    )
    light2 = pv.Light(
      position=(-30000, -30000, 50000),
      focal_point=(0, 0, 0),
      intensity=0.6,
      color="white"
    )
    plotter.add_light(light1)
    plotter.add_light(light2)

    # # 그림 파일로 저장 (옵션)
    if save_path:
      plotter.screenshot(save_path)

    # # 시각화 표시
    # plotter.show()

    # 🔹 HTML로 저장
    # plotter.export_html("3D_model.html")  # 🚀 저장된 HTML을 브라우저에서 열 수 있음


  # MF Rigid 위치 모델 캡쳐
  @staticmethod
  def NodeFilterInRange(node_cls, element_cls, rigidNodes_list, MF_nodeID, range):
    # 제거할 Node와 Element 모음 리스트
    deleteNodes_list = []
    deleteElements_list = []

    # 남겨둘 Range 범위 설정
    MF_X, MF_Y, _ = node_cls[MF_nodeID]['X'], node_cls[MF_nodeID]['Y'], node_cls[MF_nodeID]['Z']
    max_X, min_X = MF_X + range, MF_X - range  # X와 Y의 range 범위 잡기
    max_Y, min_Y = MF_Y + range, MF_Y - range

    # Range 범위를 벗어나는 node 제거
    for nodeID, Value in node_cls:
      if (min_X >= Value['X'] or max_X <= Value['X']) or (min_Y >= Value['Y'] or max_Y <= Value['Y']):
        deleteNodes_list.append(nodeID)
    for nodeID in deleteNodes_list:
      node_cls.Remove(nodeID)

    # Range 범위를 벗어나는 Element 제거
    for eleID, eleValue in element_cls:
      if not all(element not in deleteNodes_list for element in eleValue['nodes']):
        deleteElements_list.append(eleID)
    for eleID in deleteElements_list:
      element_cls.Remove(eleID)

    rigidNodes_list = [nodes for nodes in rigidNodes_list if MF_nodeID in nodes]

    return node_cls, element_cls, rigidNodes_list



