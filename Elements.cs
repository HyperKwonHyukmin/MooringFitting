namespace MooringFitting2026.Model.Entities
{
  /// <summary>
  /// 순수 Element 1개가 가지는 데이터 클래스
  /// </summary>
  public sealed class Element
  {
    public IReadOnlyList<int> NodeIDs { get; }
    public int PropertyID { get; }
    public IReadOnlyDictionary<string, string> ExtraData { get; }

    public Element(
      IEnumerable<int> nodeIDs,
      int propertyID,
      Dictionary<string, string>? extraData = null)
    {
      if (nodeIDs == null)
        throw new ArgumentNullException(nameof(nodeIDs));

      var list = nodeIDs.ToList();
      if (list.Count < 2)
        throw new ArgumentException("Element must have at least two nodes.");

      if (list.Distinct().Count() != list.Count)
        throw new ArgumentException("Element nodeIDs must be unique.");

      NodeIDs = list.AsReadOnly();
      PropertyID = propertyID;

      ExtraData = extraData != null
        ? new Dictionary<string, string>(extraData)
        : new Dictionary<string, string>();
    }
  public override string ToString()
  {
    string nodesPart = $"Nodes:[{string.Join(",", NodeIDs)}]";
    string propPart = $"PropertyID:{PropertyID}";

    string extraPart;
    if (ExtraData == null || ExtraData.Count == 0)
    {
      extraPart = "ExtraData:{}";
    }
    else
    {
      // 보기 좋게 key 정렬 + key=value로 출력
      var pairs = ExtraData
        .OrderBy(kv => kv.Key)
        .Select(kv =>
        {
          string k = kv.Key ?? "";
          string v = kv.Value ?? "";
          return $"{k}={v}";
        });

      extraPart = $"ExtraData:{{{string.Join(", ", pairs)}}}";
    }

    return $"{nodesPart}, {propPart}, {extraPart}";
  }
  public double GetMaxReferencedPropertyDimension(Properties properties)
  {
    if (properties == null)
      throw new ArgumentNullException(nameof(properties));

    var prop = properties[this.PropertyID]; // Element가 PropertyID를 가짐 :contentReference[oaicite:1]{index=1}

    var dim = prop.Dim;
    if (prop.Type == "I")
    {
      // I의 경우는 3번째 요소(인덱스 2가 대가리쪽)    
      var targetDim = Math.Round(dim[2] / 2, 1); // 범위는 절반으로
      return targetDim;
    }
    else if (prop.Type == "T")
    {
      // (T의 경우는 1번째 요소(인덱스 0가 대가리쪽) 
      var targetDim = Math.Round(dim[2] / 2, 1); // 범위는 절반으로
      return targetDim;
    }
    else
    {
      throw new ArgumentException("Error");
    }

  }
}
 public class Elements : IEnumerable<KeyValuePair<int, Element>>
 {
   private readonly Dictionary<int, Element> _elements = new();

   private int _nextElementID = 1;
   public int LastElementID { get; private set; } = 0;

   public Elements() { }

   public Element this[int id]
   {
     get
     {
       if (!_elements.TryGetValue(id, out var element))
         throw new KeyNotFoundException($"Element ID {id} does not exist.");
       return element;
     }
   }
  public int AddNew(
    List<int> nodeIDs,
    int propertyID,
    Dictionary<string, string>? extraData = null)
  {
    int newID = _nextElementID++;

    var element = new Element(nodeIDs, propertyID, extraData);
    _elements[newID] = element;

    LastElementID = newID;
    return newID;
  }
  public void AddWithID(
    int elementID,
    List<int> nodeIDs,
    int propertyID,
    Dictionary<string, string>? extraData = null)
  {
    var element = new Element(nodeIDs, propertyID, extraData);
    _elements[elementID] = element;

    if (elementID >= _nextElementID)
      _nextElementID = elementID + 1;

    if (elementID > LastElementID)
      LastElementID = elementID;
  }
 public void Remove(int elementID)
 {
   _elements.Remove(elementID);

   if (elementID == LastElementID)
   {
     if (_elements.Count > 0)
     {
       LastElementID = _elements.Keys.Max();
       _nextElementID = LastElementID + 1;
     }
     else
     {
       LastElementID = 0;
       _nextElementID = 1;
     }
   }
 }
  public bool Contains(int elementID)
    => _elements.ContainsKey(elementID);

  public IEnumerable<int> Keys
    => _elements.Keys;

  /// <summary>
  /// Element 갯수 반환
  /// </summary>
  public int Count
    => _elements.Count;

  /// <summary>
  /// 특정 Node가 Element 생성에 몇번 사용되었는가
  /// </summary>
  public int CountNodeUsage(int nodeID)
  {
    int count = 0;
    foreach (var element in _elements.Values)
      if (element.NodeIDs.Contains(nodeID))
        count++;
    return count;
  }
    public Dictionary<int, int> CountAllNodeUsages()
    {
      var dict = new Dictionary<int, int>();

      foreach (var element in _elements.Values)
      {
        foreach (int node in element.NodeIDs)
        {
          if (dict.ContainsKey(node)) dict[node]++;
          else dict[node] = 1;
        }
      }
      return dict;
    }


    public IReadOnlyDictionary<int, Element> AsReadOnly()
      => _elements;


    public IEnumerator<KeyValuePair<int, Element>> GetEnumerator()
      => _elements.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
      => GetEnumerator();

  }
}
