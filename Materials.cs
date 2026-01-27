using MooringFitting2026.Model.Entities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MooringFitting2026.Model.Entities
{
  public sealed class Material
  {
    public string Name { get; }
    public double E { get; }
    public double Nu { get; }
    public double Rho { get; }

    public Material(
      string name,
      double elasticModulus,
      double poissonRatio,
      double density)
    {
      if (string.IsNullOrWhiteSpace(name))
        throw new ArgumentException("Material name is invalid.", nameof(name));

      Name = name;
      E = elasticModulus;
      Nu = poissonRatio;
      Rho = density;
    }

    public override string ToString()
    {
      return $"Material:{Name}, E:{E}, ν:{Nu}, ρ:{Rho}";
    }
  }

  public sealed class Materials : IEnumerable<KeyValuePair<int, Material>>
  {
    private int _nextMaterialID = 1;

    private readonly Dictionary<int, Material> _materials = new();
    private readonly Dictionary<string, int> _lookup = new();


    public Material this[int materialID]
    {
      get
      {
        if (!_materials.TryGetValue(materialID, out var mat))
          throw new KeyNotFoundException($"Material ID {materialID} does not exist.");
        return mat;
      }
    }

    public int AddOrGet(
      string name,
      double youngsModulus,
      double poissonRatio,
      double density)
    {
      string key = MakeKey(name, youngsModulus, poissonRatio, density);

      if (_lookup.TryGetValue(key, out int existingID))
        return existingID;

      int newID = _nextMaterialID++;
      _materials[newID] = new Material(
        name,
        youngsModulus,
        poissonRatio,
        density);

      _lookup[key] = newID;
      return newID;
    }

    public void Remove(int materialID)
    {
      if (!_materials.TryGetValue(materialID, out var mat))
        throw new KeyNotFoundException($"Material ID {materialID} does not exist.");

      string key = MakeKey(
        mat.Name,
        mat.E,
        mat.Nu,
        mat.Rho);

      _materials.Remove(materialID);
      _lookup.Remove(key);
    }

    public bool Contains(int materialID)
      => _materials.ContainsKey(materialID);

    public int Count
      => _materials.Count;

    public IEnumerable<int> Keys
      => _materials.Keys;

    public IReadOnlyDictionary<int, Material> AsReadOnly()
      => _materials;

    private static string MakeKey(
      string name,
      double youngsModulus,
      double poissonRatio,
      double density)
    {
      return string.Join("|",
        name,
        youngsModulus.ToString("G17", CultureInfo.InvariantCulture),
        poissonRatio.ToString("G17", CultureInfo.InvariantCulture),
        density.ToString("G17", CultureInfo.InvariantCulture));
    }


    public IEnumerator<KeyValuePair<int, Material>> GetEnumerator()
      => _materials.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
      => GetEnumerator();

  }
}
