using MooringFitting2026.Model.Entities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MooringFitting2026.Model.Entities
{
  public sealed class Property
  {
    public string Type { get; }
    public IReadOnlyList<double> Dim { get; }
    public int MaterialID { get; }

    public Property(
      string type,
      IEnumerable<double> dimensions,
      int materialID)
    {
      if (string.IsNullOrWhiteSpace(type))
        throw new ArgumentException("Property type is invalid.", nameof(type));

      if (dimensions == null)
        throw new ArgumentNullException(nameof(dimensions));

      var list = dimensions.ToList();
      if (list.Count == 0)
        throw new ArgumentException("Property dimensions cannot be empty.");

      Type = type;
      Dim = list.AsReadOnly();
      MaterialID = materialID;
    }

    public override string ToString()
    {
      return $"Type:{Type}, Dim:[{string.Join(",", Dim)}], MaterialID:{MaterialID}";
    }
  }


  public class Properties : IEnumerable<KeyValuePair<int, Property>>
  {
    private int _nextPropertyID = 1;

    private readonly Dictionary<int, Property> _properties = new();
    private readonly Dictionary<string, int> _lookup = new();

    public Property this[int propertyID]
    {
      get
      {
        if (!_properties.TryGetValue(propertyID, out var prop))
          throw new KeyNotFoundException($"Property ID {propertyID} does not exist.");
        return prop;
      }
    }

    public int AddOrGet(
      string type,
      IEnumerable<double> dimensions,
      int materialID)
    {
      string key = MakeKey(type, dimensions, materialID);

      if (_lookup.TryGetValue(key, out int existingID))
        return existingID;

      int newID = _nextPropertyID++;
      _properties[newID] = new Property(type, dimensions, materialID);
      _lookup[key] = newID;

      return newID;
    }

    public void Remove(int propertyID)
    {
      if (!_properties.TryGetValue(propertyID, out var prop))
        throw new KeyNotFoundException($"Property ID {propertyID} does not exist.");

      string key = MakeKey(prop.Type, prop.Dim, prop.MaterialID);

      _properties.Remove(propertyID);
      _lookup.Remove(key);
    }

    public bool Contains(int propertyID)
      => _properties.ContainsKey(propertyID);

    public int Count
      => _properties.Count;

    public IEnumerable<int> Keys
      => _properties.Keys;

    public IReadOnlyDictionary<int, Property> AsReadOnly()
      => _properties;

    private static string MakeKey(
      string type,
      IEnumerable<double> dims,
      int materialID)
    {
      // double → string 결정성 확보
      string dimKey = string.Join(
        ";",
        dims.Select(d => d.ToString("G17", CultureInfo.InvariantCulture)));

      return $"{type}|{dimKey}|{materialID}";
    }

    public IEnumerator<KeyValuePair<int, Property>> GetEnumerator()
      => _properties.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
      => GetEnumerator();

  }
}
