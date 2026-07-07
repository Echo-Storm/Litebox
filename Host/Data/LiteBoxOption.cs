// Reusable resolution + storage for LiteBox-OWN options that LaunchBox has no field for
// (StartupStayOnTop, per-emulator ScreenCaptureKey, and whatever we add next). These live in
// litebox-options.db, scope by entity — a per-entity row OVERRIDES the global value, its
// absence INHERITS it. Tri-state by design: no row = inherit, a row = an explicit override
// (a bool "true"/"false", or a string incl. an explicit "disabled" sentinel).
//
// The resolution order mirrors the LB-native tier (game < emulator < global); today only the
// emulator + global scopes are wired. One place so every consumer resolves identically, and
// adding the next per-emulator LiteBox option is a couple of lines.

#nullable enable

namespace LbApiHost.Host.Data;

internal static class LiteBoxOption
{
    public const string ScopeEmulator = "emulator";
    public const string ScopeGame     = "game";
    public const string ScopePlatform = "platform";

    /// <summary>The raw per-entity override, or null = inherit (no row). Drives the tri-state UI.</summary>
    public static string? GetOverride(string scope, string entityId, string key)
        => string.IsNullOrEmpty(entityId) ? null : LiteBoxOptionsDb.Get(scope, entityId, key);

    /// <summary>Set (non-empty) or clear (null/empty ⇒ back to inherit) a per-entity override.</summary>
    public static void SetOverride(string scope, string entityId, string key, string? value)
        => LiteBoxOptionsDb.Set(scope, entityId, key, value);

    /// <summary>Effective bool for an emulator launch: the emulator override when set, else the
    /// global value. (Extend with a game scope the same way when a per-game tier is added.)</summary>
    public static bool ResolveBool(string key, string? emulatorId, bool globalValue)
    {
        var ov = GetOverride(ScopeEmulator, emulatorId ?? "", key);
        return string.IsNullOrEmpty(ov) ? globalValue
                                        : string.Equals(ov, "true", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Effective string for an emulator launch: the emulator override when set, else the
    /// global value. An override of the DISABLED sentinel resolves to empty (feature off).</summary>
    public static string ResolveString(string key, string? emulatorId, string globalValue)
    {
        var ov = GetOverride(ScopeEmulator, emulatorId ?? "", key);
        if (string.IsNullOrEmpty(ov)) return globalValue;
        return ov == Disabled ? "" : ov;
    }

    /// <summary>Sentinel stored for a string option the user EXPLICITLY turned off for this entity
    /// (distinct from "no row = inherit"). ScreenCapture already treats "None" as disabled.</summary>
    public const string Disabled = "None";
}
