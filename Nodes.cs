using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MooringFitting2026.Model.Geometry;


namespace MooringFitting2026.Model.Entities
{

  public class Nodes : IEnumerable<KeyValuePair<int, Point3D>>
  {
    // 내부 상태
    private readonly Dictionary<int, Point3D> _nodes = new Dictionary<int, Point3D>();
    private readonly Dictionary<string, HashSet<int>> _nodeLookup = new Dictionary<string, HashSet<int>>();
    public ICollection<int> Keys => _nodes.Keys;

    // 다음에 부여할 Node ID
    private int _nextNodeID = 1;

    /// <summary>
    /// 현재까지 사용된 가장 큰 Node ID (마지막으로 추가된 ID)
    /// </summary>
    public int LastNodeID { get; private set; } = 0;

    // 좌표 반올림 (소수점 첫째 자리)
    private static double RoundCoord(double value)
    {
      return Math.Round(value, 1, MidpointRounding.AwayFromZero);
    }

    private static Point3D RoundPoint(double x, double y, double z)
    {
      return new Point3D(RoundCoord(x), RoundCoord(y), RoundCoord(z));
    }

    // 저장된 Point3D로부터 lookup key 생성
    private static string MakeKey(Point3D p)
    {
      // 필요하다면 CultureInfo.InvariantCulture를 써도 됨
      return $"{p.X},{p.Y},{p.Z}";
    }

    // 외부에서 들어온 좌표(x,y,z)를 반올림 규칙에 맞춰 key 생성
    private static string MakeKey(double x, double y, double z)
    {
      var p = RoundPoint(x, y, z);
      return MakeKey(p);
    }

    private void LookupAdd(string key, int nodeID)
    {
      if (!_nodeLookup.TryGetValue(key, out var set))
      {
        set = new HashSet<int>();
        _nodeLookup[key] = set;
      }
      set.Add(nodeID);
    }

    private void LookupRemove(string key, int nodeID)
    {
      if (!_nodeLookup.TryGetValue(key, out var set))
        return;

      set.Remove(nodeID);

      if (set.Count == 0)
        _nodeLookup.Remove(key);
    }

    /// <summary>
    /// 좌표가 이미 존재하면(반올림 기준) 그 좌표를 가진 nodeID 중 하나를 반환,
    /// 없으면 새로 추가하고 새 nodeID 반환
    /// </summary>
    public int AddOrGet(double x, double y, double z)
    {
      Point3D rounded = RoundPoint(x, y, z);
      string key = MakeKey(rounded);

      if (_nodeLookup.TryGetValue(key, out var existingSet) && existingSet.Count > 0)
      {
        // 결정적으로 반환하고 싶으면 Min() 권장 (HashSet은 순서 보장 X)
        return existingSet.Min();
      }

      int newNodeID = _nextNodeID++;
      _nodes[newNodeID] = rounded;
      LookupAdd(key, newNodeID);

      LastNodeID = newNodeID;
      return newNodeID;
    }

    /// <summary>
    /// 지정된 ID로 노드를 추가 (파일 읽기, 복원 등에서 사용).
    /// - 좌표는 여기서도 동일하게 반올림하여 저장
    /// - _nextNodeID와 LastNodeID를 안전하게 갱신
    /// </summary>
    public void AddWithID(int nodeID, double x, double y, double z)
    {
      // 기존 nodeID가 이미 있으면, 이전 lookup에서도 빼줘야 무결성 유지됨
      if (_nodes.TryGetValue(nodeID, out var oldPoint))
      {
        string oldKey = MakeKey(oldPoint);
        LookupRemove(oldKey, nodeID);
      }

      Point3D rounded = RoundPoint(x, y, z);
      string key = MakeKey(rounded);

      _nodes[nodeID] = rounded;
      LookupAdd(key, nodeID);

      if (nodeID >= _nextNodeID)
        _nextNodeID = nodeID + 1;

      if (nodeID > LastNodeID)
        LastNodeID = nodeID;
    }

    /// <summary>
    /// 해당 ID의 노드 존재 여부
    /// </summary>
    public bool Contains(int nodeID)
    {
      return _nodes.ContainsKey(nodeID);
    }

    /// <summary>
    /// ID에 해당하는 노드를 삭제 (존재하지 않으면 예외)
    /// </summary>
    public void Remove(int nodeID)
    {
      if (!_nodes.TryGetValue(nodeID, out Point3D removedNode))
        throw new KeyNotFoundException($"Node ID {nodeID} does not exist.");

      _nodes.Remove(nodeID);

      string key = MakeKey(removedNode);
      LookupRemove(key, nodeID);

      // 삭제된 노드가 마지막 ID였다면 LastNodeID와 _nextNodeID 재조정
      if (nodeID == LastNodeID)
      {
        if (_nodes.Count > 0)
        {
          LastNodeID = _nodes.Keys.Max();
          _nextNodeID = LastNodeID + 1;
        }
        else
        {
          LastNodeID = 0;
          _nextNodeID = 1;
        }
      }
    }

    /// <summary>
    /// 동일 좌표(반올림 기준)에 존재하는 모든 nodeID를 반환한다.
    /// 여러 nodeID가 반환되는 것은 정상적인 상태이다.
    /// </summary>
    public IReadOnlyCollection<int> FindNodeIDs(double x, double y, double z)
    {
      string key = MakeKey(x, y, z);
      if (_nodeLookup.TryGetValue(key, out var set))
        return set;

      return Array.Empty<int>();
    }

    /// <summary>
    /// 특정 노드의 좌표 반환 (없으면 예외)
    /// </summary>
    public Point3D GetNodeCoordinates(int nodeID)
    {
      if (!_nodes.TryGetValue(nodeID, out Point3D point))
      {
        throw new KeyNotFoundException($"Node ID {nodeID} does not exist.");
      }
      return point;
    }

    /// <summary>
    /// 전체 노드 리스트 반환
    /// </summary>
    public List<KeyValuePair<int, Point3D>> GetAllNodes()
    {
      return new List<KeyValuePair<int, Point3D>>(_nodes);
    }

    /// <summary>
    /// 현재 노드 수
    /// </summary>
    public int GetNodeCount()
    {
      return _nodes.Count;
    }

    /// <summary>
    /// 특정 좌표에서 가장 가까운 NodeID 반환 (없으면 -1)
    /// </summary>
    public int FindClosestNodeID(double targetX, double targetY, double targetZ)
    {
      double minDistance = double.MaxValue;
      int closestNodeID = -1;

      foreach (var node in _nodes)
      {
        double dx = node.Value.X - targetX;
        double dy = node.Value.Y - targetY;
        double dz = node.Value.Z - targetZ;
        double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);

        if (distance < minDistance)
        {
          minDistance = distance;
          closestNodeID = node.Key;
        }
      }
      return closestNodeID;
    }

    /// <summary>
    /// 인덱서: nodeID로 좌표 접근
    /// </summary>
    public Point3D this[int nodeID]
    {
      get => GetNodeCoordinates(nodeID);
    }

    /// <summary>
    /// IEnumerable 구현 (foreach 지원)
    /// </summary>
    public IEnumerator<KeyValuePair<int, Point3D>> GetEnumerator()
    {
      return _nodes.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
      return GetEnumerator();
    }
  }
}
