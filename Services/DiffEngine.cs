// Cross-file diff state and per-key/per-value status computation.
// Encapsulates the global indices (allKeys, allValues) and the per-key/per-value diff helpers.

using System;
using System.Collections.Generic;
using RegCompare.Models;

namespace RegCompare.Services;

/// <summary>
/// Holds the global indices that span the currently-checked subset of files and exposes the
/// per-key and per-value diff functions used by the column view models.
/// </summary>
/// <remarks>
/// One instance is rebuilt every time the user clicks Compare or toggles a file checkbox.
/// The instance is immutable after construction; callers can read it concurrently from any thread.
/// </remarks>
public sealed class DiffEngine
{
    /// <summary>The reference file index inside <see cref="Files"/>.</summary>
    public int ReferenceIndex { get; }

    /// <summary>The full set of currently-checked files used to build the indices.</summary>
    public IReadOnlyList<RegFile> Files { get; }

    /// <summary>
    /// keyPath -&gt; list of file indices (into <see cref="Files"/>) that contain this key.
    /// </summary>
    public Dictionary<string, List<int>> AllKeys { get; }

    /// <summary>
    /// keyPath -&gt; valueName -&gt; list of file indices that contain this value name under this key.
    /// </summary>
    public Dictionary<string, Dictionary<string, List<int>>> AllValues { get; }

    /// <summary>
    /// Every key path in <see cref="AllKeys"/>, sorted ordinal-ignore-case. Computed once here
    /// (on the background thread that builds the engine) so column rebuilds don't re-sort.
    /// </summary>
    public IReadOnlyList<string> SortedKeys { get; }

    /// <summary>
    /// Per file index: the set of key paths whose own status/values changed vs the reference,
    /// plus every ancestor path of such a key. Backs <see cref="KeyOrSubtreeChanged"/> as an O(1) lookup.
    /// </summary>
    private readonly HashSet<string>[] _subtreeChanged;

    /// <summary>Union of <see cref="_subtreeChanged"/> over all non-reference files.</summary>
    private readonly HashSet<string> _subtreeChangedAnywhere;

    /// <summary>
    /// Build a new diff engine over the given files, with the given reference index.
    /// </summary>
    public DiffEngine(IReadOnlyList<RegFile> files, int referenceIndex)
    {
        Files = files;
        ReferenceIndex = referenceIndex;

        AllKeys = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        AllValues = new Dictionary<string, Dictionary<string, List<int>>>(StringComparer.OrdinalIgnoreCase);

        for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
        {
            var data = files[fileIndex].Data;
            foreach (var (key, values) in data)
            {
                if (!AllKeys.TryGetValue(key, out var keyList))
                {
                    keyList = new List<int>();
                    AllKeys[key] = keyList;
                }
                keyList.Add(fileIndex);

                if (!AllValues.TryGetValue(key, out var valueMap))
                {
                    valueMap = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                    AllValues[key] = valueMap;
                }
                foreach (var valueName in values.Keys)
                {
                    if (!valueMap.TryGetValue(valueName, out var valList))
                    {
                        valList = new List<int>();
                        valueMap[valueName] = valList;
                    }
                    valList.Add(fileIndex);
                }
            }
        }

        var sorted = new string[AllKeys.Count];
        AllKeys.Keys.CopyTo(sorted, 0);
        Array.Sort(sorted, StringComparer.OrdinalIgnoreCase);
        SortedKeys = sorted;

        _subtreeChanged = new HashSet<string>[files.Count];
        _subtreeChangedAnywhere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _subtreeChanged[fileIndex] = set;
            if (fileIndex == referenceIndex) continue;

            foreach (var key in sorted)
            {
                var changed = GetKeyStatus(key, fileIndex) is DiffStatus.Missing or DiffStatus.Added;
                if (!changed && AllValues.TryGetValue(key, out var valMap))
                {
                    foreach (var valueName in valMap.Keys)
                    {
                        var vStatus = GetValueStatus(key, valueName, fileIndex);
                        if (vStatus is DiffStatus.Missing or DiffStatus.Added or DiffStatus.Different)
                        {
                            changed = true;
                            break;
                        }
                    }
                }
                if (!changed) continue;

                // Mark the key and every ancestor path; stop as soon as an ancestor is already
                // marked (its own ancestors are then marked too).
                var path = key;
                while (set.Add(path))
                {
                    _subtreeChangedAnywhere.Add(path);
                    var idx = path.LastIndexOf('\\');
                    if (idx < 0) break;
                    path = path[..idx];
                }
            }
        }
    }

    /// <summary>
    /// Diff status of a single key in <paramref name="fileIndex"/> compared to the reference file.
    /// Returns <see cref="DiffStatus.Hidden"/> when the key exists in neither (caller should skip rendering).
    /// </summary>
    public DiffStatus GetKeyStatus(string key, int fileIndex)
    {
        var existsHere = Files[fileIndex].Data.ContainsKey(key);
        var existsRef = Files[ReferenceIndex].Data.ContainsKey(key);

        if (fileIndex == ReferenceIndex) return existsHere ? DiffStatus.Reference : DiffStatus.Hidden;
        if (!existsHere && existsRef) return DiffStatus.Missing;
        if (existsHere && !existsRef) return DiffStatus.Added;
        if (!existsHere && !existsRef) return DiffStatus.Hidden;
        return DiffStatus.Exists;
    }

    /// <summary>
    /// Diff status of a single value <paramref name="valueName"/> under <paramref name="key"/>
    /// in <paramref name="fileIndex"/> compared to the reference file.
    /// </summary>
    public DiffStatus GetValueStatus(string key, string valueName, int fileIndex)
    {
        var here = Files[fileIndex].Data;
        var refData = Files[ReferenceIndex].Data;
        var existsHere = here.TryGetValue(key, out var hereVals) && hereVals.ContainsKey(valueName);
        var existsRef = refData.TryGetValue(key, out var refVals) && refVals.ContainsKey(valueName);

        if (fileIndex == ReferenceIndex) return existsHere ? DiffStatus.Reference : DiffStatus.Hidden;
        if (!existsHere && existsRef) return DiffStatus.Missing;
        if (existsHere && !existsRef) return DiffStatus.Added;
        if (!existsHere && !existsRef) return DiffStatus.Hidden;

        // Both sides exist: compare raw strings.
        var hereVal = hereVals![valueName];
        var refVal = refVals![valueName];
        return string.Equals(hereVal, refVal, StringComparison.Ordinal)
            ? DiffStatus.Identical
            : DiffStatus.Different;
    }

    /// <summary>
    /// True if the key (or any of its values) differs from the reference in *any* checked file.
    /// Used to bold/highlight items in the reference column.
    /// </summary>
    public bool KeyChangedAnywhere(string key)
    {
        for (var i = 0; i < Files.Count; i++)
        {
            if (i == ReferenceIndex) continue;
            if (!Files[i].Data.ContainsKey(key)) return true;
            if (AllValues.TryGetValue(key, out var valMap))
            {
                foreach (var valueName in valMap.Keys)
                {
                    var status = GetValueStatus(key, valueName, i);
                    if (status is DiffStatus.Missing or DiffStatus.Added or DiffStatus.Different) return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// True if a specific value differs from the reference in any checked file.
    /// </summary>
    public bool ValueChangedAnywhere(string key, string valueName)
    {
        var refData = Files[ReferenceIndex].Data;
        var refExists = refData.TryGetValue(key, out var refVals) && refVals.ContainsKey(valueName);
        var refValue = refExists ? refVals![valueName] : null;

        for (var i = 0; i < Files.Count; i++)
        {
            if (i == ReferenceIndex) continue;
            var data = Files[i].Data;
            var existsHere = data.TryGetValue(key, out var hereVals) && hereVals.ContainsKey(valueName);
            if (existsHere != refExists) return true;
            if (existsHere && refExists && !string.Equals(hereVals![valueName], refValue, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True if this key path or any of its values or any subkey differs from the reference in
    /// <paramref name="fileIndex"/>. Used by the auto-expand-on-compare logic.
    /// </summary>
    public bool KeyOrSubtreeChanged(string key, int fileIndex) => _subtreeChanged[fileIndex].Contains(key);

    /// <summary>
    /// True if this key, any of its values, or any subkey changed against the reference in any
    /// non-reference checked file. Used by the reference column's auto-expand-on-compare.
    /// </summary>
    public bool ChangedAnywhereForKey(string key) => _subtreeChangedAnywhere.Contains(key);
}
