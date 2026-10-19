using System;
using System.Collections.Generic;
using System.Linq;

namespace ModSyncCheck;

/// <summary>Which installed mods each installed mod needs to load, from their required dependencies and the mod each content pack is for.</summary>
internal class ModDependencies
{
    /*********
    ** Fields
    *********/
    /// <summary>The installed mods each installed mod needs, by unique ID.</summary>
    private readonly Dictionary<string, List<string>> Needs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The installed mods which need each installed mod, by unique ID.</summary>
    private readonly Dictionary<string, List<string>> NeededBy = new(StringComparer.OrdinalIgnoreCase);


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="needs">The unique IDs of the mods each installed mod needs, by the installed mod's unique ID. Mods which aren't installed are left out.</param>
    public ModDependencies(IReadOnlyDictionary<string, IEnumerable<string>> needs)
    {
        HashSet<string> installed = new(needs.Keys.Select(id => id.Trim()), StringComparer.OrdinalIgnoreCase);
        foreach ((string modId, IEnumerable<string> neededIds) in needs)
        {
            string id = modId.Trim();
            foreach (string neededId in neededIds.Select(neededId => neededId.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(id, neededId, StringComparison.OrdinalIgnoreCase) || !installed.Contains(neededId))
                    continue;

                ModDependencies.Add(this.Needs, id, neededId);
                ModDependencies.Add(this.NeededBy, neededId, id);
            }
        }
    }

    /// <summary>Get the mods which the given mods need, directly or through other mods, with the mod which needs each one directly.</summary>
    /// <param name="isRequired">Get whether a mod is one whose dependencies are needed.</param>
    public Dictionary<string, string> GetNeededBy(Func<string, bool> isRequired)
    {
        return ModDependencies.Walk(this.Needs, this.Needs.Keys.Where(isRequired));
    }

    /// <summary>Get the mods which need any of the given mods, directly or through other mods, with the mod each one needs directly.</summary>
    /// <param name="modIds">The unique IDs of the mods.</param>
    public Dictionary<string, string> GetDependents(IEnumerable<string> modIds)
    {
        return ModDependencies.Walk(this.NeededBy, modIds);
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Add a link to a map of mods.</summary>
    /// <param name="map">The map to change.</param>
    /// <param name="from">The unique ID of the mod to link from.</param>
    /// <param name="to">The unique ID of the mod to link to.</param>
    private static void Add(Dictionary<string, List<string>> map, string from, string to)
    {
        if (!map.TryGetValue(from, out List<string>? links))
            map[from] = links = new List<string>();
        links.Add(to);
    }

    /// <summary>Get the mods reached by following one or more links from the given mods, with the mod each one was first reached from.</summary>
    /// <param name="links">The mods each mod links to.</param>
    /// <param name="start">The unique IDs of the mods to start from.</param>
    private static Dictionary<string, string> Walk(Dictionary<string, List<string>> links, IEnumerable<string> start)
    {
        Dictionary<string, string> reached = new(StringComparer.OrdinalIgnoreCase);
        Queue<string> queue = new(start.Select(id => id.Trim()));
        while (queue.Count > 0)
        {
            string id = queue.Dequeue();
            if (!links.TryGetValue(id, out List<string>? next))
                continue;

            foreach (string nextId in next)
            {
                if (reached.TryAdd(nextId, id))
                    queue.Enqueue(nextId);
            }
        }
        return reached;
    }
}
