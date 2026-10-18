using UnityEngine;
using UnityEngine.UI;

public class PlayerListPanel : MonoBehaviour
{
    private PlayerListEntry[] entries;

    void Start()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        var layout = gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 8, 8);
        layout.spacing = 4;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = false;

        entries = new PlayerListEntry[8];
        for (int i = 0; i < 8; i++)
        {
            var go = new GameObject($"Player_{i}", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var entry = go.AddComponent<PlayerListEntry>();
            var p = gm.Players[i];
            entry.Build(p, () => gm.ViewPlayer(p));
            entries[i] = entry;
        }

        entries[0].SetViewed(true);
        RefreshMatchup();

        gm.OnGoldChanged           += RefreshAll;
        gm.OnViewedPlayerChanged   += OnViewedPlayerChanged;
        gm.OnMatchupChanged        += RefreshMatchup;
    }

    void OnDestroy()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        gm.OnGoldChanged           -= RefreshAll;
        gm.OnViewedPlayerChanged   -= OnViewedPlayerChanged;
        gm.OnMatchupChanged        -= RefreshMatchup;
    }

    void RefreshAll()
    {
        foreach (var e in entries)
            e.Refresh();
    }

    void OnViewedPlayerChanged(PlayerState viewed)
    {
        var gm = GameManager.Instance;
        for (int i = 0; i < 8; i++)
            entries[i].SetViewed(gm.Players[i] == viewed);
    }

    void RefreshMatchup()
    {
        var gm = GameManager.Instance;
        for (int i = 0; i < 8; i++)
            entries[i].SetNextOpponent(gm.Players[i] == gm.NextOpponent);
    }
}
