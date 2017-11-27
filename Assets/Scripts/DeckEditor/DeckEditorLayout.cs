using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class DeckEditorLayout : MonoBehaviour
{
    public const float WidthCheck = 1300f;

    public Vector2 DeckButtonsLandscapePosition {
        get { return new Vector2(-750f, 0f); }
    }

    public Vector2 DeckButtonsPortraitPosition {
        get { return new Vector2(0f, -(GetComponent<RectTransform>().rect.height + 87.5f)); }
    }

    public RectTransform deckButtons;

    void Start()
    {
        if (GetComponent<RectTransform>().rect.width < WidthCheck)
            deckButtons.anchoredPosition = DeckButtonsPortraitPosition;
    }

    void OnRectTransformDimensionsChange()
    {
        if (!this.gameObject.activeInHierarchy)
            return;
        
        if (GetComponent<RectTransform>().rect.width < WidthCheck)
            deckButtons.anchoredPosition = DeckButtonsPortraitPosition;
        else
            deckButtons.anchoredPosition = DeckButtonsLandscapePosition;
    }
}
