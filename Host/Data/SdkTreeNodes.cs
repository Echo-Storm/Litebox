// SDK source-tree adapters.
//
// The plugin SDK exposes the source tree through IList<IPlatform>:
//   IDataManager.GetRootPlatformsCategoriesPlaylists()  and  IPlatform.GetChildren()
// In real LaunchBox the category/playlist nodes in those lists are concrete objects that implement
// IPlatform AND (IPlatformCategory | IPlaylist) — the latter two interfaces do NOT derive from
// IPlatform (verified: their base-interface set is empty). Consumers like ExtendDB's web themes walk
// the IPlatform list and discriminate with `node is IPlatformCategory` / `node is IPlaylist`, then
// recurse via node.GetChildren(). LaunchBoxWeb / BigBoxWeb build their whole left tree this way.
//
// LiteBox's own HostPlatformCategory / HostPlaylist are NOT IPlatform (they can't be — a single
// typed list can't hold all three, and the host keeps children as `object`). So they can't ride the
// SDK's IList<IPlatform>, and HostDataManagerXml used to return only the flat platforms — which is
// why the web tree rendered as a flat platform list with no categories or playlists.
//
// These thin adapters fix that WITHOUT touching the host data classes the native GUI depends on:
// each derives from DummyPlatform (the generated stub gives the full IPlatform surface for free) and
// additionally implements the discriminating interface, forwarding the members the tree reads to the
// real host object. Categories build their children lazily, so the web's recursive descent works.

using System;
using System.Collections.Generic;
using System.Linq;
using Unbroken.LaunchBox.Plugins.Data;
using LbApiHost.Generated;

namespace LbApiHost.Host.Data;

internal static class SdkTree
{
    /// <summary>Represent a host tree node (platform / category / playlist) as an IPlatform for the
    /// SDK tree. Platforms already are IPlatform (pass through); categories/playlists get an adapter
    /// that ALSO implements IPlatformCategory / IPlaylist so `node is …` discrimination works.</summary>
    public static IPlatform Wrap(object node) => node switch
    {
        HostPlatformCategory c => new CategoryNode(c),
        HostPlaylist pl => new PlaylistNode(pl),
        IPlatform p => p,
        _ => null,
    };

    /// <summary>Wrap a set of host tree nodes into the SDK's IList&lt;IPlatform&gt; (nulls dropped).</summary>
    public static IList<IPlatform> WrapChildren(IEnumerable<object> nodes)
        => nodes?.Select(Wrap).Where(x => x != null).ToList() ?? new List<IPlatform>();
}

/// <summary>IPlatform + IPlatformCategory adapter over a <see cref="HostPlatformCategory"/>. Every
/// IPlatformCategory member already exists on DummyPlatform (its surface is a subset of IPlatform),
/// so this compiles by deriving from DummyPlatform; we override the few the web tree reads.</summary>
internal sealed class CategoryNode : DummyPlatform, IPlatformCategory
{
    private readonly HostPlatformCategory _c;
    public CategoryNode(HostPlatformCategory c) { _c = c; }

    public override string Name { get => _c.Name; set { } }
    public override string NestedName { get => _c.NestedName; set { } }
    public override string Notes { get => _c.Notes; set { } }
    public override string SortTitle { get => _c.SortTitle; set { } }
    public override bool HideInBigBox { get => _c.HideInBigBox; set { } }
    public override string ClearLogoImagePath { get => _c.ClearLogoImagePath; set { } }
    public override string BannerImagePath { get => _c.BannerImagePath; set { } }
    public override string BackgroundImagePath { get => _c.BackgroundImagePath; set { } }
    public override string DeviceImagePath { get => _c.DeviceImagePath; set { } }

    public override IList<IPlatform> GetChildren() => SdkTree.WrapChildren(_c.Children);

    public override IGame[] GetAllGames(bool includeHidden, bool includeBroken)
        => _c.GetAllGames(includeHidden, includeBroken) ?? Array.Empty<IGame>();
    public override IGame[] GetAllGames(bool includeHidden, bool includeBroken, bool a, bool b, bool d, bool e, bool f)
        => _c.GetAllGames(includeHidden, includeBroken) ?? Array.Empty<IGame>();
    public override int GetGameCount(bool includeHidden, bool includeBroken)
        => _c.GetGameCount(includeHidden, includeBroken);
    public override int GetGameCount(bool includeHidden, bool includeBroken, bool a, bool b, bool d, bool e, bool f)
        => _c.GetGameCount(includeHidden, includeBroken);
    public override bool HasGames(bool includeHidden, bool includeBroken)
        => _c.HasGames(includeHidden, includeBroken);
    public override bool HasGames(bool includeHidden, bool includeBroken, bool a, bool b, bool d, bool e, bool f)
        => _c.HasGames(includeHidden, includeBroken);
}

/// <summary>IPlatform + IPlaylist adapter over a <see cref="HostPlaylist"/>. DummyPlatform supplies
/// the IPlatform surface; the IPlaylist-only members (and the shared ones the tree reads) forward to
/// the real playlist. Playlists have no children, so GetChildren stays the DummyPlatform empty list.</summary>
internal sealed class PlaylistNode : DummyPlatform, IPlaylist
{
    private readonly HostPlaylist _pl;
    public PlaylistNode(HostPlaylist pl) { _pl = pl; }

    // Shared IPlatform/IPlaylist members the tree reads → forward to the real playlist.
    public override string Name { get => _pl.Name; set { } }
    public override string NestedName { get => _pl.NestedName; set { } }
    public override string Notes { get => _pl.Notes; set { } }
    public override string SortTitle { get => _pl.SortTitle; set { } }
    public override string VideoPath { get => _pl.VideoPath; set { } }
    public override string ImageType { get => _pl.ImageType; set { } }
    public override string Category { get => _pl.Category; set { } }
    public override string LastGameId { get => _pl.LastGameId; set { } }
    public override string BigBoxView { get => _pl.BigBoxView; set { } }
    public override string BigBoxTheme { get => _pl.BigBoxTheme; set { } }
    public override bool HideInBigBox { get => _pl.HideInBigBox; set { } }
    public override string ClearLogoImagePath { get => _pl.ClearLogoImagePath; set { } }
    public override string BannerImagePath { get => _pl.BannerImagePath; set { } }
    public override string BackgroundImagePath { get => _pl.BackgroundImagePath; set { } }
    public override string DeviceImagePath { get => _pl.DeviceImagePath; set { } }
    public override string DefaultBoxImagePath { get => _pl.DefaultBoxImagePath; set { } }
    public override string Default3DBoxImagePath { get => _pl.Default3DBoxImagePath; set { } }
    public override string DefaultCartImagePath { get => _pl.DefaultCartImagePath; set { } }
    public override string Default3DCartImagePath { get => _pl.Default3DCartImagePath; set { } }
    public override int GetGameCount(bool includeHidden, bool includeBroken) => _pl.GetGameCount(includeHidden, includeBroken);
    public override int GetGameCount(bool includeHidden, bool includeBroken, bool a, bool b, bool d, bool e, bool f) => _pl.GetGameCount(includeHidden, includeBroken);
    public override bool HasGames(bool includeHidden, bool includeBroken) => _pl.HasGames(includeHidden, includeBroken);
    public override bool HasGames(bool includeHidden, bool includeBroken, bool a, bool b, bool d, bool e, bool f) => _pl.HasGames(includeHidden, includeBroken);

    // IPlaylist-only members (not on IPlatform) — forward to the real playlist.
    public string PlaylistId => _pl.PlaylistId;
    public bool AutoPopulate { get => _pl.AutoPopulate; set { } }
    public bool IncludeWithPlatforms { get => _pl.IncludeWithPlatforms; set { } }
    public string SortBy { get => _pl.SortBy; set { } }
    public IGame[] GetAllGames(bool sort) => _pl.GetAllGames(sort) ?? Array.Empty<IGame>();
    public IPlaylistFilter[] GetAllPlaylistFilters() => _pl.GetAllPlaylistFilters();
    public IPlaylistGame[] GetAllPlaylistGames() => _pl.GetAllPlaylistGames();
    public IPlaylistFilter AddNewPlaylistFilter() => _pl.AddNewPlaylistFilter();
    public IPlaylistGame AddNewPlaylistGame() => _pl.AddNewPlaylistGame();
    public void ClearGames() => _pl.ClearGames();
    public bool TryRemovePlaylistFilter(IPlaylistFilter f) => _pl.TryRemovePlaylistFilter(f);
    public bool TryRemovePlaylistGame(IPlaylistGame g) => _pl.TryRemovePlaylistGame(g);
}
