using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModSyncCheck;

/// <summary>Makes a running mod use different settings, without restarting the game or changing its settings file.</summary>
/// <remarks>
///   Mods normally read their settings once when the game starts, and SMAPI has no way to ask a mod to read them again. This finds the settings
///   object the mod keeps in memory and copies the new values into it, which is what editing the settings in Generic Mod Config Menu does. It
///   only works for mods which keep their settings in a member named like <c>Config</c> on their entry class, and a mod which has already
///   used a setting to set something else up won't redo that.
/// </remarks>
internal static class LiveConfigApplier
{
    /// <summary>The members to search on a mod's entry class.</summary>
    private const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>How to turn settings into text for <see cref="GetSnapshot"/>.</summary>
    private static readonly JsonSerializerOptions SnapshotOptions = new() { IncludeFields = true, ReferenceHandler = ReferenceHandler.IgnoreCycles };

    /// <summary>Get whether a mod keeps its settings where they can be changed.</summary>
    /// <param name="mod">The mod's entry class instance.</param>
    /// <param name="error">The reason the settings can't be changed, if applicable.</param>
    public static bool CanApply(object mod, out string? error)
    {
        return LiveConfigApplier.FindConfig(mod, out error) != null;
    }

    /// <summary>Copy settings into the settings object a mod keeps in memory, if it can be found.</summary>
    /// <param name="mod">The mod's entry class instance.</param>
    /// <param name="readConfig">Read the settings to use as a new object of the given type.</param>
    /// <param name="error">The reason the settings couldn't be applied, if applicable.</param>
    /// <returns>Returns whether the settings were applied.</returns>
    public static bool TryApply(object mod, Func<Type, object?> readConfig, out string? error)
    {
        object? current = LiveConfigApplier.FindConfig(mod, out error);
        if (current == null)
            return false;

        object? loaded = readConfig(current.GetType());
        if (loaded == null)
        {
            error = "its settings file couldn't be read";
            return false;
        }

        // copy into the existing object, since other parts of the mod may hold a reference to it
        Type type = current.GetType();
        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!field.IsInitOnly)
                field.SetValue(current, field.GetValue(loaded));
        }
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
                property.SetValue(current, property.GetValue(loaded));
        }

        error = null;
        return true;
    }

    /// <summary>Get text which changes when the settings a mod keeps in memory change, so two snapshots can be compared.</summary>
    /// <param name="mod">The mod's entry class instance.</param>
    /// <returns>Returns the snapshot, or <c>null</c> if the settings can't be found or read.</returns>
    public static string? GetSnapshot(object mod)
    {
        object? config = LiveConfigApplier.FindConfig(mod, out _);
        if (config == null)
            return null;

        try
        {
            return JsonSerializer.Serialize(config, config.GetType(), LiveConfigApplier.SnapshotOptions);
        }
        catch (Exception)
        {
            return null; // some settings can't be turned into text, or the mod's own code failed while reading them
        }
    }

    /// <summary>Find the settings object a mod keeps in memory.</summary>
    /// <param name="mod">The mod's entry class instance.</param>
    /// <param name="error">The reason it couldn't be found, if applicable.</param>
    private static object? FindConfig(object mod, out string? error)
    {
        // get the candidate members by name, keeping one per distinct object
        Dictionary<object, string> candidates = new(ReferenceEqualityComparer.Instance);
        for (Type? type = mod.GetType(); type != null && type.Assembly == mod.GetType().Assembly; type = type.BaseType)
        {
            foreach (MemberInfo member in type.GetMembers(LiveConfigApplier.MemberFlags))
            {
                if (!member.Name.Contains("config", StringComparison.OrdinalIgnoreCase) || member.Name.StartsWith('<'))
                    continue;

                object? value = member switch
                {
                    FieldInfo field => field.GetValue(field.IsStatic ? null : mod),
                    PropertyInfo { CanRead: true } property when property.GetIndexParameters().Length == 0 => property.GetValue(property.GetMethod!.IsStatic ? null : mod),
                    _ => null
                };
                if (value != null && LiveConfigApplier.IsConfigType(value.GetType()))
                    candidates.TryAdd(value, member.Name);
            }
        }

        // prefer a member named exactly 'Config' if there are several
        if (candidates.Count > 1)
        {
            KeyValuePair<object, string>[] exact = candidates.Where(p => p.Value.TrimStart('_').Equals("config", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (exact.Length == 1)
            {
                error = null;
                return exact[0].Key;
            }
        }

        error = candidates.Count switch
        {
            0 => "it doesn't keep its settings where they can be found",
            1 => null,
            _ => "it has several settings objects"
        };
        return candidates.Count == 1 ? candidates.Keys.First() : null;
    }

    /// <summary>Get whether a type could be a mod's settings model.</summary>
    /// <param name="type">The type to check.</param>
    private static bool IsConfigType(Type type)
    {
        return
            type.IsClass
            && !type.IsAbstract
            && type != typeof(string)
            && !typeof(Delegate).IsAssignableFrom(type)
            && !typeof(System.Collections.IEnumerable).IsAssignableFrom(type)
            && type.Namespace?.StartsWith("System", StringComparison.Ordinal) != true
            && type.GetConstructor(Type.EmptyTypes) != null;
    }
}
