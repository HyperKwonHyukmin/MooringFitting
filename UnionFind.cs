namespace MooringFitting2026.Utils
{
  public class UnionFind
  {
    private readonly Dictionary<int, int> _parent;

    public UnionFind(IEnumerable<int> elements)
    {
      _parent = elements.ToDictionary(e => e, e => e);
    }

    public int Find(int x)
    {
      if (_parent[x] != x)
        _parent[x] = Find(_parent[x]);
      return _parent[x];
    }

    public void Union(int a, int b)
    {
      int rootA = Find(a);
      int rootB = Find(b);
      if (rootA != rootB)
        _parent[rootB] = rootA;
    }

    public Dictionary<int, List<int>> GetClusters()
    {
      var clusters = new Dictionary<int, List<int>>();

      foreach (var x in _parent.Keys)
      {
        int root = Find(x);
        if (!clusters.ContainsKey(root))
          clusters[root] = new List<int>();
        clusters[root].Add(x);
      }

      return clusters;
    }
  }

  /// <summary>
  /// Proximity 결과를 Union-Find로 정리하여
  /// 병합 cluster를 생성하는 Inspector
  /// </summary>
  public sealed class NodeClusterInspector
  {
    /// <summary>
    /// 근접 관계를 cluster로 묶어
    /// (source → target) 병합 쌍으로 정리
    /// </summary>
    public List<(int source, int target)> BuildMergePairs(
      IEnumerable<(int a, int b)> proximityPairs)
    {
      var pairs = proximityPairs.ToList();
      var result = new List<(int source, int target)>();

      if (pairs.Count == 0)
        return result;

      // 1. Union-Find 초기화
      var nodeIDs = pairs
        .SelectMany(p => new[] { p.a, p.b })
        .Distinct();

      var uf = new UnionFind(nodeIDs);

      // 2. Union
      foreach (var (a, b) in pairs)
        uf.Union(a, b);

      // 3. Cluster 추출
      var clusters = uf.GetClusters();

      // 4. Cluster별 병합 쌍 생성
      foreach (var cluster in clusters.Values)
      {
        if (cluster.Count < 2)
          continue;

        // 대표 node 선택 (결정적)
        int target = cluster.Min();

        foreach (int source in cluster)
        {
          if (source == target)
            continue;

          result.Add((source, target));
        }
      }

      return result;
    }
  }
}
