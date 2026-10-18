using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class BoardCellUI : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public int gridX, gridY;
    public event Action<int, int> OnClicked;

    // Set by BoardGridUI — controls how large all unit sprites appear uniformly.
    // Increase to make sprites bigger; decrease to shrink them.
    public float spriteScale = 1f;

    private Image cellBg;
    private Image unitImage;
    private RectTransform unitImageRT;
    private SpriteAnimator animator;
    private TextMeshProUGUI starsText;

    private bool isSelected;
    private bool hasUnit;

    static readonly Color CellDark    = new Color(0.22f, 0.33f, 0.18f);
    static readonly Color CellLight   = new Color(0.27f, 0.40f, 0.22f);
    static readonly Color CellSelect  = new Color(0.85f, 0.72f, 0.12f);
    static readonly Color CellHover   = new Color(0.35f, 0.50f, 0.28f);
    static readonly Color CellFull    = new Color(0.24f, 0.36f, 0.20f);

    public void Build(int x, int y)
    {
        gridX = x; gridY = y;

        cellBg = gameObject.AddComponent<Image>();
        UpdateBgColor();

        var unitGO = new GameObject("UnitSprite", typeof(RectTransform));
        unitGO.transform.SetParent(transform, false);
        unitImageRT = unitGO.GetComponent<RectTransform>();
        unitImageRT.anchorMin = new Vector2(0.5f, 0.5f);   // centered, no stretch
        unitImageRT.anchorMax = new Vector2(0.5f, 0.5f);
        unitImageRT.pivot = new Vector2(0.5f, 0.5f);
        unitImageRT.anchoredPosition = Vector2.zero;
        unitImageRT.sizeDelta = Vector2.zero;               // sized per-sprite in UpdateDisplay
        unitImage = unitGO.AddComponent<Image>();
        unitImage.color = Color.clear;
        animator = unitGO.AddComponent<SpriteAnimator>();

        // Stars indicator — bottom-left corner, no background so the sprite shows fully
        var starsTextGO = new GameObject("StarsText", typeof(RectTransform));
        starsTextGO.transform.SetParent(transform, false);
        var starsTextRT = starsTextGO.GetComponent<RectTransform>();
        starsTextRT.anchorMin = new Vector2(0, 0);
        starsTextRT.anchorMax = new Vector2(0.5f, 0.25f);
        starsTextRT.offsetMin = new Vector2(2, 2);
        starsTextRT.offsetMax = Vector2.zero;
        starsText = starsTextGO.AddComponent<TextMeshProUGUI>();
        starsText.fontSize = 10;
        starsText.fontStyle = FontStyles.Bold;
        starsText.alignment = TextAlignmentOptions.BottomLeft;
        starsText.color = new Color(1f, 0.88f, 0.15f);
        starsText.raycastTarget = false;
        starsText.text = "";
    }

    public void UpdateDisplay(UnitInstance unit)
    {
        hasUnit = unit != null;
        if (unit == null)
        {
            animator.Stop();
            unitImage.color = Color.clear;
            unitImageRT.sizeDelta = Vector2.zero;
            starsText.text = "";
        }
        else
        {
            var frames = unit.definition.idleFrames;
            if (frames != null && frames.Length > 0 && frames[0] != null)
            {
                unitImage.color = Color.white;
                animator.Play(frames, 8f);
                ApplySpriteScale(frames[0]);
            }
            else if (unit.definition.idleSprite != null)
            {
                animator.Stop();
                unitImage.sprite = unit.definition.idleSprite;
                unitImage.color = Color.white;
                ApplySpriteScale(unit.definition.idleSprite);
            }
            else
            {
                animator.Stop();
                unitImage.color = new Color(0.6f, 0.8f, 1f, 0.8f);
                unitImageRT.sizeDelta = new Vector2(40, 40);
            }
            starsText.text = unit.starLevel > 1 ? unit.starLevel.ToString() : "";
        }
        UpdateBgColor();
    }

    private void ApplySpriteScale(Sprite spr)
    {
        unitImageRT.sizeDelta = new Vector2(spr.rect.width * spriteScale, spr.rect.height * spriteScale);
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        UpdateBgColor();
    }

    private void UpdateBgColor()
    {
        if (cellBg == null) return;
        if (isSelected)
            cellBg.color = CellSelect;
        else
            cellBg.color = (gridX + gridY) % 2 == 0 ? CellDark : CellLight;
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Right)
            GameManager.Instance.TrySellUnit(gridX, gridY);
        else
            OnClicked?.Invoke(gridX, gridY);
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (!isSelected) cellBg.color = CellHover;
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (!isSelected) UpdateBgColor();
    }
}
